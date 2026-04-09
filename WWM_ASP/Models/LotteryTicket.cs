using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace WWM_ASP.Models;

[Table("lottery_tickets")]
public class LotteryTicket
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("lottery_draw_id")]
    public long LotteryDrawId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("picked_number")]
    public int PickedNumber { get; set; }

    [Column("picked_numbers", TypeName = "jsonb")]
    public string? PickedNumbersJson { get; set; }

    [Column("bet_amount")]
    public long BetAmount { get; set; }

    [Column("is_winner")]
    public bool? IsWinner { get; set; }

    [Column("payout")]
    public long Payout { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // ─── Navigation ───────────────────────────────────────────────────────────
    [ForeignKey(nameof(LotteryDrawId))]
    public LotteryDraw? Draw { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [NotMapped]
    public int[]? PickedNumbers
    {
        get => PickedNumbersJson == null ? null
             : JsonSerializer.Deserialize<int[]>(PickedNumbersJson);
        set => PickedNumbersJson = value == null ? null : JsonSerializer.Serialize(value);
    }
}
