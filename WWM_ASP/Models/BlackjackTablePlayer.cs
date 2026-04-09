using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("blackjack_table_players")]
public class BlackjackTablePlayer
{
    [Key, Column("id")]
    public long Id { get; set; }

    [Column("blackjack_table_id")]
    public long BlackjackTableId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("joined_at")]
    public DateTime? JoinedAt { get; set; }

    [Column("role")]
    public string Role { get; set; } = "player";  // player | dealer

    [Column("seat")]
    public byte? Seat { get; set; }

    [Column("is_ready")]
    public bool IsReady { get; set; }

    // Navigation
    public User?          User  { get; set; }
    public BlackjackTable? Table { get; set; }
}
