using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;

namespace WWM_ASP.Controllers;

[Authorize(AuthenticationSchemes = "user")]
public class EntertainmentController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var poker     = await db.PokerTables.ToListAsync();
        var taixiu    = await db.TaixiuTables.ToListAsync();
        var blackjack = await db.BlackjackTables.ToListAsync();
        var bingo     = await db.BingoTables.ToListAsync();
        var tienlen   = await db.TienLenTables.Where(t => !t.IsAiMode).ToListAsync();

        var dailyPot  = await db.LotteryDraws
            .Where(d => d.Type == "daily"  && d.Status == "open")
            .OrderByDescending(d => d.DrawAt)
            .Select(d => d.TotalPot)
            .FirstOrDefaultAsync();

        var weeklyPot = await db.LotteryDraws
            .Where(d => d.Type == "weekly" && d.Status == "open")
            .OrderByDescending(d => d.DrawAt)
            .Select(d => d.TotalPot)
            .FirstOrDefaultAsync();

        ViewBag.Stats = new
        {
            Poker     = TableStats(poker.Select(t => t.Status)),
            Blackjack = TableStats(blackjack.Select(t => t.Status)),
            Taixiu    = TableStats(taixiu.Select(t => t.Status)),
            Bingo     = TableStats(bingo.Select(t => t.Status)),
            TienLen   = TableStats(tienlen.Select(t => t.Status)),
            LotteryDailyPot  = dailyPot,
            LotteryWeeklyPot = weeklyPot,
        };

        return View();
    }

    private static (int Total, int Playing, int Waiting) TableStats(IEnumerable<string> statuses)
    {
        var list = statuses.ToList();
        return (list.Count, list.Count(s => s == "playing"), list.Count(s => s == "waiting"));
    }
}
