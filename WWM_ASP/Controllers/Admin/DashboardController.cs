using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;

namespace WWM_ASP.Controllers.Admin;

[Authorize(Policy = "StaffAuth")]
[Route("admin/[controller]")]
public class DashboardController(AppDbContext db) : Controller
{
    [HttpGet("")]
    [HttpGet("~/admin")]
    [HttpGet("~/admin/dashboard")]
    public async Task<IActionResult> Index()
    {
        var membersCount     = await db.Users.CountAsync();
        var totalZCoins      = await db.Users.SumAsync(u => (long)u.ZCoins);
        var staffCount       = await db.Staffs.CountAsync();
        var transactionsToday = await db.ZooCoinTransactions
            .Where(t => t.CreatedAt >= DateTime.UtcNow.Date)
            .CountAsync();

        ViewBag.MembersCount      = membersCount;
        ViewBag.TotalZCoins       = totalZCoins;
        ViewBag.StaffCount        = staffCount;
        ViewBag.TransactionsToday = transactionsToday;

        return View("~/Views/Admin/Dashboard/Index.cshtml");
    }
}
