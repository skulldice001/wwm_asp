using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("taixiu_tables")]
public class TaixiuTable
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("min_bet")]
    public long MinBet { get; set; } = 100;

    [Column("max_bet")]
    public long MaxBet { get; set; } = 10000;

    [Column("max_players")]
    public int MaxPlayers { get; set; } = 20;

    [Column("current_players")]
    public int CurrentPlayers { get; set; }

    [Column("status")]
    public string Status { get; set; } = "waiting";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
