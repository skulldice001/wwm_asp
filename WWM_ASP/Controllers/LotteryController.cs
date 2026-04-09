using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WWM_ASP.Data;
using WWM_ASP.Models;

namespace WWM_ASP.Controllers;

[Authorize(AuthenticationSchemes = "user")]
[Route("entertainment/lottery")]
public class LotteryController(AppDbContext db) : Controller
{
    private long UserId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── GET /entertainment/lottery ───────────────────────────────────────────

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId  = UserId;
        var daily   = await GetOrCreateOpenAsync("daily");
        var weekly  = await GetOrCreateOpenAsync("weekly");
        var jackpot = await GetOrCreateJackpotAsync();

        var dailyTickets  = await db.LotteryTickets.Where(t => t.LotteryDrawId == daily.Id  && t.UserId == userId).ToListAsync();
        var weeklyTickets = await db.LotteryTickets.Where(t => t.LotteryDrawId == weekly.Id && t.UserId == userId).ToListAsync();
        var jackpotTickets = await db.LotteryTickets.Where(t => t.LotteryDrawId == jackpot.Id && t.UserId == userId)
            .OrderByDescending(t => t.Id).Take(5).ToListAsync();

        var recentDaily  = await db.LotteryDraws.Where(d => d.Type == "daily"  && d.Status == "settled").OrderByDescending(d => d.DrawAt).Take(7).ToListAsync();
        var recentWeekly = await db.LotteryDraws.Where(d => d.Type == "weekly" && d.Status == "settled").OrderByDescending(d => d.DrawAt).Take(5).ToListAsync();
        var recentJackpot = await db.LotteryDraws.Where(d => d.Type == "jackpot" && d.Status == "settled").OrderByDescending(d => d.DrawnAt).Take(5).ToListAsync();

        var user = await db.Users.FindAsync(userId);

        ViewBag.Daily        = daily;
        ViewBag.Weekly       = weekly;
        ViewBag.Jackpot      = jackpot;
        ViewBag.DailyTickets  = dailyTickets;
        ViewBag.WeeklyTickets = weeklyTickets;
        ViewBag.JackpotTickets = jackpotTickets;
        ViewBag.RecentDaily   = recentDaily;
        ViewBag.RecentWeekly  = recentWeekly;
        ViewBag.RecentJackpot = recentJackpot;
        ViewBag.Balance       = user?.ZCoins ?? 0;
        ViewBag.MyHistory     = await MyHistoryAsync(userId);
        ViewBag.LastSettledDaily  = await LastSettledDrawAsync("daily",  userId);
        ViewBag.LastSettledWeekly = await LastSettledDrawAsync("weekly", userId);

        ViewData["ActivePage"] = "entertainment";
        return View("~/Views/Entertainment/Lottery.cshtml");
    }

    // ── POST /entertainment/lottery/ticket — buy daily/weekly ticket ─────────

    [HttpPost("ticket")]
    public async Task<IActionResult> BuyTicket([FromBody] BuyTicketRequest req)
    {
        if (req.PickedNumber < LotteryDraw.NUMBER_MIN || req.PickedNumber > LotteryDraw.NUMBER_MAX)
            return Json(new { error = "Số hợp lệ từ 01 đến 99." });

        if (!new[] { 10, 100, 1000, 10000 }.Contains(req.BetAmount))
            return Json(new { error = "Mức cược không hợp lệ." });

        var draw = await db.LotteryDraws.FindAsync(req.DrawId);
        if (draw == null || !draw.IsOpen())
            return Json(new { error = "Giải xổ số đã đóng hoặc không tồn tại." });

        var userId = UserId;
        if (await db.LotteryTickets.AnyAsync(t => t.LotteryDrawId == draw.Id && t.UserId == userId))
            return Json(new { error = "Bạn đã mua vé cho giải này rồi." });

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            if (user.AvailableZCoins < req.BetAmount)
                return Json(new { error = "Không đủ Zoo để mua vé." });

            long balBefore = user.ZCoins;
            user.ZCoins    -= req.BetAmount;
            user.UpdatedAt = DateTime.UtcNow;

            db.ZooCoinTransactions.Add(new ZooCoinTransaction
            {
                UserId        = userId,
                Type          = "lottery_bet",
                Amount        = req.BetAmount,
                BalanceBefore = balBefore,
                BalanceAfter  = user.ZCoins,
                Note          = $"Mua vé xổ số {(draw.Type == "weekly" ? "tuần" : "ngày")}",
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            });

            var ticket = new LotteryTicket
            {
                LotteryDrawId = draw.Id,
                UserId        = userId,
                PickedNumber  = req.PickedNumber,
                BetAmount     = req.BetAmount,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            };
            db.LotteryTickets.Add(ticket);

            draw.TotalTickets++;
            draw.TotalPot   += req.BetAmount;
            draw.UpdatedAt   = DateTime.UtcNow;

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return Json(new
            {
                ok      = true,
                ticket  = new { ticket.Id, ticket.PickedNumber, ticket.BetAmount, ticket.IsWinner, ticket.Payout },
                balance = user.ZCoins,
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return Json(new { error = ex.Message });
        }
    }

    // ── POST /entertainment/lottery/jackpot — buy jackpot ticket ────────────

    [HttpPost("jackpot")]
    public async Task<IActionResult> BuyJackpot([FromBody] BuyJackpotRequest req)
    {
        if (req.Number < 0 || req.Number > 999)
            return Json(new { error = "Số hợp lệ từ 000 đến 999." });

        var jackpot = await GetOrCreateJackpotAsync();
        if (jackpot.Status != "open")
            return Json(new { error = "Jackpot chưa mở." });

        var userId = UserId;
        if (await db.LotteryTickets.AnyAsync(t => t.LotteryDrawId == jackpot.Id && t.UserId == userId))
            return Json(new { error = "Bạn đã mua vé Jackpot hôm nay rồi." });

        int price = LotteryDraw.JACKPOT_PRICE;

        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var user = await db.Users.FirstAsync(u => u.Id == userId);
            if (user.AvailableZCoins < price)
                return Json(new { error = $"Không đủ Zoo. Cần {price} Zoo." });

            long balBefore = user.ZCoins;
            user.ZCoins    -= price;
            user.UpdatedAt = DateTime.UtcNow;

            db.ZooCoinTransactions.Add(new ZooCoinTransaction
            {
                UserId        = userId,
                Type          = "lottery_bet",
                Amount        = price,
                BalanceBefore = balBefore,
                BalanceAfter  = user.ZCoins,
                Note          = $"Mua vé Jackpot số {req.Number:D3}",
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            });

            var ticket = new LotteryTicket
            {
                LotteryDrawId = jackpot.Id,
                UserId        = userId,
                PickedNumber  = req.Number,
                BetAmount     = price,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
            };
            db.LotteryTickets.Add(ticket);

            jackpot.TotalTickets++;
            jackpot.TotalPot   += price;
            jackpot.UpdatedAt   = DateTime.UtcNow;

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            return Json(new
            {
                ok      = true,
                ticket  = new { ticket.Id, ticket.PickedNumber, ticket.BetAmount, ticket.IsWinner, ticket.Payout },
                balance = user.ZCoins,
                pot     = jackpot.TotalPot,
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return Json(new { error = ex.Message });
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<LotteryDraw> GetOrCreateOpenAsync(string type)
    {
        var draw = await db.LotteryDraws
            .Where(d => d.Type == type && d.Status == "open" && d.DrawAt > DateTime.UtcNow)
            .OrderByDescending(d => d.DrawAt)
            .FirstOrDefaultAsync();

        if (draw != null) return draw;

        var drawAt = type == "weekly" ? LotteryDraw.NextWeeklyDrawAt() : LotteryDraw.NextDailyDrawAt();

        var existing = await db.LotteryDraws.FirstOrDefaultAsync(d => d.Type == type && d.DrawAt == drawAt);
        if (existing != null)
        {
            if (existing.Status != "open") { existing.Status = "open"; existing.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
            return existing;
        }

        draw = new LotteryDraw
        {
            Type       = type,
            Status     = "open",
            DrawAt     = drawAt,
            OpensAt    = DateTime.UtcNow,
            PickCount  = (short)(type == "weekly" ? LotteryDraw.WEEKLY_PICK_COUNT : LotteryDraw.DAILY_PICK_COUNT),
            Multiplier = (short)(type == "weekly" ? LotteryDraw.WEEKLY_MULTIPLIER : LotteryDraw.DAILY_MULTIPLIER),
            CreatedAt  = DateTime.UtcNow,
            UpdatedAt  = DateTime.UtcNow,
        };
        db.LotteryDraws.Add(draw);
        await db.SaveChangesAsync();
        return draw;
    }

    private async Task<LotteryDraw> GetOrCreateJackpotAsync()
    {
        var draw = await db.LotteryDraws
            .Where(d => d.Type == "jackpot" && d.Status == "open" && d.DrawAt > DateTime.UtcNow)
            .OrderByDescending(d => d.DrawAt)
            .FirstOrDefaultAsync();

        if (draw != null) return draw;

        var drawAt = LotteryDraw.NextJackpotDrawAt();
        var existing = await db.LotteryDraws.FirstOrDefaultAsync(d => d.Type == "jackpot" && d.DrawAt == drawAt);
        if (existing != null) return existing;

        // Carry over pot from previous jackpot if no winner
        long carryover = 0;
        var prev = await db.LotteryDraws.Where(d => d.Type == "jackpot" && d.Status == "settled").OrderByDescending(d => d.DrawnAt).FirstOrDefaultAsync();
        if (prev != null && prev.TotalPayout == 0) carryover = prev.TotalPot;

        draw = new LotteryDraw
        {
            Type        = "jackpot",
            Status      = "open",
            DrawAt      = drawAt,
            OpensAt     = drawAt.Date, // opens at midnight
            PickCount   = (short)LotteryDraw.JACKPOT_PICK_COUNT,
            Multiplier  = 1,
            TicketPrice = LotteryDraw.JACKPOT_PRICE,
            TotalPot    = carryover,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
        db.LotteryDraws.Add(draw);
        await db.SaveChangesAsync();
        return draw;
    }

    private async Task<List<object>> MyHistoryAsync(long userId)
    {
        var tickets = await db.LotteryTickets
            .Where(t => t.UserId == userId)
            .Include(t => t.Draw)
            .OrderByDescending(t => t.CreatedAt)
            .Take(20)
            .ToListAsync();

        return tickets.Select(t => (object)new
        {
            id             = t.Id,
            draw_type      = t.Draw?.Type,
            draw_at        = t.Draw?.Type == "jackpot"
                ? t.Draw.DrawnAt?.ToLocalTime().ToString("dd/MM HH:mm") ?? "—"
                : t.Draw?.DrawAt?.ToLocalTime().ToString("dd/MM HH:mm") ?? "—",
            draw_status    = t.Draw?.Status,
            picked_number  = t.PickedNumber,
            bet_amount     = t.BetAmount,
            is_winner      = t.IsWinner,
            payout         = t.Payout,
        }).ToList();
    }

    private async Task<object?> LastSettledDrawAsync(string type, long userId)
    {
        var draw = await db.LotteryDraws
            .Where(d => d.Type == type && d.Status == "settled")
            .OrderByDescending(d => d.DrawAt)
            .FirstOrDefaultAsync();

        if (draw == null) return null;

        var ticket = await db.LotteryTickets.FirstOrDefaultAsync(t => t.LotteryDrawId == draw.Id && t.UserId == userId);

        return new
        {
            id              = draw.Id,
            draw_at         = draw.DrawAt?.ToLocalTime().ToString("dd/MM HH:mm") ?? "—",
            winning_numbers = draw.WinningNumbers,
            total_tickets   = draw.TotalTickets,
            total_payout    = draw.TotalPayout,
            my_ticket       = ticket == null ? null : (object)new
            {
                picked_number = ticket.PickedNumber,
                bet_amount    = ticket.BetAmount,
                is_winner     = ticket.IsWinner,
                payout        = ticket.Payout,
            },
        };
    }

    // ─── Request DTOs ─────────────────────────────────────────────────────────
    public record BuyTicketRequest(long DrawId, int PickedNumber, int BetAmount);
    public record BuyJackpotRequest(int Number);
}
