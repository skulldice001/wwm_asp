using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("poker_tables")]
public class PokerTable
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("type")]
    public string Type { get; set; } = "no_limit_holdem";

    [Column("small_blind")]
    public decimal SmallBlind { get; set; }

    [Column("big_blind")]
    public decimal BigBlind { get; set; }

    [Column("min_buy_in")]
    public decimal MinBuyIn { get; set; }

    [Column("max_buy_in")]
    public decimal MaxBuyIn { get; set; }

    [Column("current_players")]
    public int CurrentPlayers { get; set; }

    [Column("max_players")]
    public int MaxPlayers { get; set; } = 9;

    [Column("status")]
    public string Status { get; set; } = "waiting";

    [Column("is_ai_mode")]
    public bool IsAiMode { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
