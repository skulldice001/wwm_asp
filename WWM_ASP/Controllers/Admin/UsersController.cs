using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;
using WWM_ASP.Services;

namespace WWM_ASP.Controllers.Admin;

[Authorize(Policy = "StaffAuth")]
[Route("admin/users")]
public class UsersController(AppDbContext db, ZooCoinService coinService) : Controller
{
    // ─── Helpers ──────────────────────────────────────────────────────────

    private long CurrentStaffId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool CurrentStaffIsAdmin =>
        User.HasClaim("role", Staff.RoleMaster) || User.HasClaim("role", Staff.RoleAdmin);

    private bool CurrentStaffIsMaster =>
        User.HasClaim("role", Staff.RoleMaster);

    // ─── Index ────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(string? search, bool showDeleted = false)
    {
        var query = db.Users.IgnoreQueryFilters().AsQueryable();

        if (!showDeleted)
            query = query.Where(u => u.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim().ToLower();
            query  = query.Where(u =>
                (u.Account    != null && u.Account.ToLower().Contains(search)) ||
                (u.Name       != null && u.Name.ToLower().Contains(search))    ||
                (u.IngameName != null && u.IngameName.ToLower().Contains(search)));
        }

        var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

        ViewBag.Search      = search;
        ViewBag.ShowDeleted = showDeleted;
        return View("~/Views/Admin/Users/Index.cshtml", users);
    }

    // ─── Show ─────────────────────────────────────────────────────────────

    [HttpGet("{id}")]
    public async Task<IActionResult> Show(long id)
    {
        var user = await db.Users
            .IgnoreQueryFilters()
            .Include(u => u.ZCoinTransactions.OrderByDescending(t => t.CreatedAt).Take(10))
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null) return NotFound();

        return View("~/Views/Admin/Users/Show.cshtml", user);
    }

    // ─── Create ───────────────────────────────────────────────────────────

    [HttpGet("create")]
    public IActionResult Create()
    {
        if (!CurrentStaffIsAdmin) return Forbid();
        return View("~/Views/Admin/Users/Create.cshtml");
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Store(
        string  account,
        string? name,
        string? email,
        string  password,
        string  passwordConfirm,
        string? ingameName,
        string? country)
    {
        if (!CurrentStaffIsAdmin) return Forbid();

        // Validate
        if (string.IsNullOrWhiteSpace(account))
            ModelState.AddModelError("account", "Account is required.");

        if (password != passwordConfirm)
            ModelState.AddModelError("password", "Passwords do not match.");

        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Account == account))
            ModelState.AddModelError("account", "Account already exists.");

        if (!string.IsNullOrEmpty(email) &&
            await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email))
            ModelState.AddModelError("email", "Email already in use.");

        if (!ModelState.IsValid)
            return View("~/Views/Admin/Users/Create.cshtml");

        db.Users.Add(new User
        {
            Account    = account,
            Name       = name,
            Email      = string.IsNullOrEmpty(email) ? null : email,
            Password   = BCrypt.Net.BCrypt.HashPassword(password),
            IngameName = ingameName,
            Country    = country,
            ZCoins     = 5000,
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        TempData["Success"] = "User created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Disable (soft delete) ────────────────────────────────────────────

    [HttpPost("{id}/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(long id)
    {
        if (!CurrentStaffIsAdmin) return Forbid();

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "User disabled.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Enable (restore) ─────────────────────────────────────────────────

    [HttpPost("{id}/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable(long id)
    {
        if (!CurrentStaffIsAdmin) return Forbid();

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        user.DeletedAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "User enabled.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Freeze coins ─────────────────────────────────────────────────────

    [HttpPost("{id}/freeze-coins")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FreezeCoins(long id, int amount)
    {
        if (!CurrentStaffIsAdmin) return Forbid();

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        var (ok, error) = await coinService.FreezeAsync(user, amount);

        if (ok) TempData["Success"] = "Frozen coins updated.";
        else    TempData["Error"]   = error;

        return RedirectToAction(nameof(Show), new { id });
    }

    // ─── Adjust coins ─────────────────────────────────────────────────────

    [HttpPost("{id}/adjust-coins")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdjustCoins(long id, string adjustType, int adjustAmount, string? adjustNote)
    {
        if (!CurrentStaffIsMaster) return Forbid();

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        var (ok, error) = await coinService.AdjustAsync(user, adjustType, adjustAmount, adjustNote, CurrentStaffId);

        if (ok) TempData["Success"] = "Zoo-coins adjusted.";
        else    TempData["Error"]   = error;

        return RedirectToAction(nameof(Show), new { id });
    }

    // ─── Coin history ─────────────────────────────────────────────────────

    [HttpGet("{id}/coin-history")]
    public async Task<IActionResult> CoinHistory(long id, int page = 1)
    {
        if (!CurrentStaffIsAdmin) return Forbid();

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return NotFound();

        const int pageSize = 30;
        var transactions = await db.ZooCoinTransactions
            .Where(t => t.UserId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var total = await db.ZooCoinTransactions.CountAsync(t => t.UserId == id);

        ViewBag.User     = user;
        ViewBag.Page     = page;
        ViewBag.PageSize = pageSize;
        ViewBag.Total    = total;
        return View("~/Views/Admin/Users/CoinHistory.cshtml", transactions);
    }
}
