using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;

namespace WWM_ASP.Controllers.Admin;

[Authorize(Policy = "StaffAuth")]
[Route("admin/staff")]
public class StaffController(AppDbContext db) : Controller
{
    private long CurrentStaffId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<Staff?> CurrentStaffAsync() =>
        await db.Staffs.FindAsync(CurrentStaffId);

    // ─── Index ────────────────────────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index(bool showDeleted = false)
    {
        var me    = await CurrentStaffAsync();
        var query = db.Staffs.IgnoreQueryFilters().AsQueryable();

        if (!showDeleted) query = query.Where(s => s.DeletedAt == null);

        var staffList = await query.OrderBy(s => s.Id).ToListAsync();

        ViewBag.Me          = me;
        ViewBag.ShowDeleted = showDeleted;
        return View("~/Views/Admin/Staff/Index.cshtml", staffList);
    }

    // ─── Create ───────────────────────────────────────────────────────────

    [HttpGet("create")]
    [Authorize(Policy = "AdminRole")]
    public IActionResult Create() =>
        View("~/Views/Admin/Staff/Create.cshtml");

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AdminRole")]
    public async Task<IActionResult> Store(
        string  account,
        string? name,
        string? email,
        string  password,
        string  passwordConfirm,
        string  role)
    {
        var me = await CurrentStaffAsync();
        if (me == null) return Forbid();

        // Master can create any role; Admin can only create Observer/Librarian
        if (!me.IsMaster && role is Staff.RoleMaster or Staff.RoleAdmin)
        {
            ModelState.AddModelError("role", "You don't have permission to assign this role.");
        }

        if (password != passwordConfirm)
            ModelState.AddModelError("password", "Passwords do not match.");

        if (await db.Staffs.IgnoreQueryFilters().AnyAsync(s => s.Account == account))
            ModelState.AddModelError("account", "Account already exists.");

        if (!ModelState.IsValid)
            return View("~/Views/Admin/Staff/Create.cshtml");

        db.Staffs.Add(new Staff
        {
            Account   = account,
            Name      = name,
            Email     = email,
            Password  = BCrypt.Net.BCrypt.HashPassword(password),
            Role      = role,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        TempData["Success"] = "Staff member created.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Edit ─────────────────────────────────────────────────────────────

    [HttpGet("{id}/edit")]
    public async Task<IActionResult> Edit(long id)
    {
        var me     = await CurrentStaffAsync();
        var target = await db.Staffs.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id);

        if (me == null || target == null) return NotFound();
        if (!me.CanManage(target)) return Forbid();

        ViewBag.Roles = GetAllowedRoles(me);
        return View("~/Views/Admin/Staff/Edit.cshtml", target);
    }

    [HttpPost("{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(long id, string? name, string? email, string? role, string? password, string? passwordConfirm)
    {
        var me     = await CurrentStaffAsync();
        var target = await db.Staffs.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id);

        if (me == null || target == null) return NotFound();
        if (!me.CanManage(target)) return Forbid();

        if (!string.IsNullOrEmpty(role) && role != target.Role)
        {
            if (!me.IsMaster && role is Staff.RoleMaster or Staff.RoleAdmin)
            {
                ModelState.AddModelError("role", "You don't have permission to assign this role.");
            }
            else
            {
                target.Role = role;
            }
        }

        if (!string.IsNullOrEmpty(password))
        {
            if (password != passwordConfirm)
                ModelState.AddModelError("password", "Passwords do not match.");
            else
                target.Password = BCrypt.Net.BCrypt.HashPassword(password);
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Roles = GetAllowedRoles(me);
            return View("~/Views/Admin/Staff/Edit.cshtml", target);
        }

        target.Name      = name;
        target.Email     = email;
        target.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Staff member updated.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Disable / Enable ─────────────────────────────────────────────────

    [HttpPost("{id}/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(long id)
    {
        var me     = await CurrentStaffAsync();
        var target = await db.Staffs.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id);

        if (me == null || target == null) return NotFound();
        if (!me.CanManage(target) || target.Id == me.Id) return Forbid();

        target.DeletedAt = DateTime.UtcNow;
        target.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Staff member disabled.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id}/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable(long id)
    {
        var me     = await CurrentStaffAsync();
        var target = await db.Staffs.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == id);

        if (me == null || target == null) return NotFound();
        if (!me.CanManage(target)) return Forbid();

        target.DeletedAt = null;
        target.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Success"] = "Staff member enabled.";
        return RedirectToAction(nameof(Index));
    }

    // ─── Helper ───────────────────────────────────────────────────────────

    private static string[] GetAllowedRoles(Staff me) => me.IsMaster
        ? [Staff.RoleMaster, Staff.RoleAdmin, Staff.RoleObserver, Staff.RoleLibrarian]
        : [Staff.RoleObserver, Staff.RoleLibrarian];
}
