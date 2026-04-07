using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("zoo_coin_transactions")]
public class ZooCoinTransaction
{
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("type")]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;  // add, deduct, transfer_in, transfer_out, game_win, game_loss, etc.

    [Column("amount")]
    public int Amount { get; set; }

    [Column("balance_before")]
    public int BalanceBefore { get; set; }

    [Column("balance_after")]
    public int BalanceAfter { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    [Column("staff_id")]
    public long? StaffId { get; set; }

    [Column("related_user_id")]
    public long? RelatedUserId { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // ─── Navigation ───────────────────────────────────────────────────────
    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }

    [ForeignKey(nameof(StaffId))]
    public Staff? Staff { get; set; }

    [ForeignKey(nameof(RelatedUserId))]
    public User? RelatedUser { get; set; }
}
