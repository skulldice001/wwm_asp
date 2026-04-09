using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("blackjack_messages")]
public class BlackjackMessage
{
    [Key, Column("id")]
    public long Id { get; set; }

    [Column("table_id")]
    public long TableId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("message")]
    public string Message { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public User? User { get; set; }
}
