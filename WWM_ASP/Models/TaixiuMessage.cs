using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("taixiu_messages")]
public class TaixiuMessage
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("table_id")]
    public long TableId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("message")]
    [MaxLength(500)]
    public string Message { get; set; } = "";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
