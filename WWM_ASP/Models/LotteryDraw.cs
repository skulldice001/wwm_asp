using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("lottery_draws")]
public class LotteryDraw
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("type")]
    public string Type { get; set; } = "daily";

    [Column("status")]
    public string Status { get; set; } = "open";

    [Column("draw_at")]
    public DateTime DrawAt { get; set; }

    [Column("drawn_at")]
    public DateTime? DrawnAt { get; set; }

    [Column("pick_count")]
    public short PickCount { get; set; }

    [Column("multiplier")]
    public short Multiplier { get; set; }

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
}
