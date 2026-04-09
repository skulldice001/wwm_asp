using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace WWM_ASP.Models;

[Table("lottery_draws")]
public class LotteryDraw
{
    // ─── Constants ────────────────────────────────────────────────────────────
    public const int DAILY_MULTIPLIER   = 10;
    public const int WEEKLY_MULTIPLIER  = 70;
    public const int DAILY_PICK_COUNT   = 2;
    public const int WEEKLY_PICK_COUNT  = 1;
    public const int JACKPOT_PICK_COUNT = 1;
    public const int JACKPOT_PRICE      = 500;
    public const int NUMBER_MIN         = 1;
    public const int NUMBER_MAX         = 99;

    // ─── Columns ──────────────────────────────────────────────────────────────
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("type")]
    public string Type { get; set; } = "daily";   // daily | weekly | jackpot

    [Column("status")]
    public string Status { get; set; } = "open";  // open | drawn | settled

    [Column("draw_at")]
    public DateTime? DrawAt { get; set; }

    [Column("opens_at")]
    public DateTime? OpensAt { get; set; }

    [Column("drawn_at")]
    public DateTime? DrawnAt { get; set; }

    [Column("winning_numbers", TypeName = "jsonb")]
    public string? WinningNumbersJson { get; set; }

    [Column("pick_count")]
    public short PickCount { get; set; }

    [Column("multiplier")]
    public short Multiplier { get; set; }

    [Column("ticket_price")]
    public long TicketPrice { get; set; }

    [Column("total_tickets")]
    public long TotalTickets { get; set; }

    [Column("total_pot")]
    public long TotalPot { get; set; }

    [Column("total_payout")]
    public long TotalPayout { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // ─── Computed ─────────────────────────────────────────────────────────────
    [NotMapped]
    public int[]? WinningNumbers
    {
        get => WinningNumbersJson == null ? null
             : JsonSerializer.Deserialize<int[]>(WinningNumbersJson);
        set => WinningNumbersJson = value == null ? null : JsonSerializer.Serialize(value);
    }

    // ─── Timing helpers ───────────────────────────────────────────────────────
    public bool IsOpen()
    {
        if (Status != "open") return false;
        if (OpensAt.HasValue && DateTime.UtcNow < OpensAt.Value) return false;
        // Daily: closes 1 hour before draw; weekly/jackpot: closes at draw_at
        if (!DrawAt.HasValue) return false;
        var cutoff = Type == "daily" ? DrawAt.Value.AddHours(-1) : DrawAt.Value;
        return DateTime.UtcNow < cutoff;
    }

    public int SecondsUntilClose()
    {
        if (!DrawAt.HasValue) return 0;
        var cutoff = Type == "daily" ? DrawAt.Value.AddHours(-1) : DrawAt.Value;
        return (int)Math.Max(0, (cutoff - DateTime.UtcNow).TotalSeconds);
    }

    public int SecondsUntilDraw()
        => DrawAt.HasValue ? (int)Math.Max(0, (DrawAt.Value - DateTime.UtcNow).TotalSeconds) : 0;

    public int SecondsUntilOpen()
        => OpensAt.HasValue ? (int)Math.Max(0, (OpensAt.Value - DateTime.UtcNow).TotalSeconds) : 0;

    // ─── Next schedule helpers ────────────────────────────────────────────────
    public static DateTime NextDailyDrawAt()
    {
        var today20 = DateTime.UtcNow.Date.AddHours(13); // 20:00 Vietnam = 13:00 UTC
        return DateTime.UtcNow < today20 ? today20 : today20.AddDays(1);
    }

    public static DateTime NextWeeklyDrawAt()
    {
        // Saturday 21:00 Vietnam = Saturday 14:00 UTC
        var now = DateTime.UtcNow;
        var day = now.Date;
        while (day.DayOfWeek != DayOfWeek.Saturday) day = day.AddDays(1);
        var sat14 = day.AddHours(14);
        if (now >= sat14) sat14 = sat14.AddDays(7);
        return sat14;
    }

    public static DateTime NextJackpotDrawAt()
    {
        var today1430 = DateTime.UtcNow.Date.AddHours(13).AddMinutes(30); // 20:30 VN = 13:30 UTC
        return DateTime.UtcNow < today1430 ? today1430 : today1430.AddDays(1);
    }
}
