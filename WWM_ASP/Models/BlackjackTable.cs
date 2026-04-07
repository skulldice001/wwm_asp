using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("blackjack_tables")]
public class BlackjackTable
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("min_bet")]
    public long MinBet { get; set; } = 100;

    [Column("max_bet")]
    public long MaxBet { get; set; } = 10000;

    [Column("current_players")]
    public int CurrentPlayers { get; set; }

    [Column("max_players")]
    public int MaxPlayers { get; set; } = 7;

    [Column("status")]
    public string Status { get; set; } = "waiting";

    [Column("is_preset")]
    public bool IsPreset { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
