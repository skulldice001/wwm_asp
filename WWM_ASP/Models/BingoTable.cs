using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("bingo_tables")]
public class BingoTable
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("status")]
    public string Status { get; set; } = "waiting";

    [Column("current_players")]
    public int CurrentPlayers { get; set; }

    [Column("max_players")]
    public int MaxPlayers { get; set; } = 9;

    [Column("min_players")]
    public int MinPlayers { get; set; } = 3;

    [Column("entry_fee")]
    public long EntryFee { get; set; } = 50;

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
