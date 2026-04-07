using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("taixiu_table_players")]
public class TaixiuTablePlayer
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("taixiu_table_id")]
    public long TaixiuTableId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("joined_at")]
    public DateTime? JoinedAt { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
