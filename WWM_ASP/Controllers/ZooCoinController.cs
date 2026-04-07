using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;
using WWM_ASP.Services;

namespace WWM_ASP.Controllers;

[Authorize(Policy = "UserAuth")]
[Route("zoo-coins")]
public class ZooCoinController(AppDbContext db, ZooCoinService coins) : Controller
{
    private const int PageSize = 20;

    private long CurrentUserId =>
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ─── History ──────────────────────────────────────────────────────────

    [HttpGet("history")]
    public async Task<IActionResult> History(int page = 1)
    {
        var userId = CurrentUserId;

        int total = await db.ZooCoinTransactions
            .Where(t => t.UserId == userId)
            .CountAsync();

        var transactions = await db.ZooCoinTransactions
            .Where(t => t.UserId == userId)
            .Include(t => t.RelatedUser)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        var user = await db.Users.FindAsync(userId);

        ViewBag.User     = user;
        ViewBag.Page     = page;
        ViewBag.PageSize = PageSize;
        ViewBag.Total    = total;
        ViewBag.Pages    = (int)Math.Ceiling((double)total / PageSize);

        return View("~/Views/ZooCoin/History.cshtml", transactions);
    }

    // ─── Transfer ─────────────────────────────────────────────────────────

    [HttpPost("transfer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(string recipientAccount, int amount)
    {
        var sender = await db.Users.FindAsync(CurrentUserId);
        if (sender == null) return NotFound();

        if (amount <= 0)
        {
            TempData["TransferError"] = "Amount must be greater than 0.";
            return RedirectToAction(nameof(History));
        }

        var recipient = await db.Users
            .FirstOrDefaultAsync(u => u.Account == recipientAccount);

        if (recipient == null)
        {
            TempData["TransferError"] = $"User '{recipientAccount}' not found.";
            return RedirectToAction(nameof(History));
        }

        if (recipient.Id == sender.Id)
        {
            TempData["TransferError"] = "You cannot transfer to yourself.";
            return RedirectToAction(nameof(History));
        }

        var (ok, error) = await coins.TransferAsync(sender, recipient, amount);

        if (!ok)
        {
            TempData["TransferError"] = error;
        }
        else
        {
            TempData["TransferSuccess"] = $"Transferred {amount:N0} Zoo-coins to {recipient.DisplayName}.";
        }

        return RedirectToAction(nameof(History));
    }
}
