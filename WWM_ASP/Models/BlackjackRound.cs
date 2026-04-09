using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WWM_ASP.Models;

// ── JSON state sub-types ─────────────────────────────────────────────────────

public class BlackjackCard
{
    [JsonPropertyName("rank")] public string Rank { get; set; } = "";
    [JsonPropertyName("suit")] public string Suit { get; set; } = "";
}

public class BlackjackDealerState
{
    [JsonPropertyName("user_id")]    public long   UserId     { get; set; }
    [JsonPropertyName("name")]       public string Name       { get; set; } = "";
    [JsonPropertyName("is_ai")]      public bool   IsAi       { get; set; }
    [JsonPropertyName("cards")]      public List<BlackjackCard> Cards { get; set; } = [];
    [JsonPropertyName("hole_card")]  public BlackjackCard? HoleCard  { get; set; }
    [JsonPropertyName("score")]      public int    Score      { get; set; }
    [JsonPropertyName("blackjack")]  public bool   Blackjack  { get; set; }
    [JsonPropertyName("busted")]     public bool   Busted     { get; set; }
    [JsonPropertyName("is_revealed")]public bool   IsRevealed { get; set; }
}

public class BlackjackPlayerState
{
    [JsonPropertyName("user_id")]    public long   UserId    { get; set; }
    [JsonPropertyName("name")]       public string Name      { get; set; } = "";
    [JsonPropertyName("seat")]       public int    Seat      { get; set; }
    [JsonPropertyName("cards")]      public List<BlackjackCard> Cards { get; set; } = [];
    [JsonPropertyName("bet")]        public long   Bet       { get; set; }
    [JsonPropertyName("bet_placed")] public bool   BetPlaced { get; set; }
    [JsonPropertyName("can_double")] public bool   CanDouble { get; set; }
    [JsonPropertyName("stood")]      public bool   Stood     { get; set; }
    [JsonPropertyName("busted")]     public bool   Busted    { get; set; }
    [JsonPropertyName("blackjack")]  public bool   Blackjack { get; set; }
    [JsonPropertyName("result")]     public string? Result   { get; set; }
    [JsonPropertyName("payout")]     public long   Payout    { get; set; }
}

public class BlackjackState
{
    [JsonPropertyName("phase")]                public string Phase               { get; set; } = "betting";
    [JsonPropertyName("deck")]                 public List<BlackjackCard> Deck   { get; set; } = [];
    [JsonPropertyName("is_ai_mode")]           public bool   IsAiMode            { get; set; }
    [JsonPropertyName("dealer")]               public BlackjackDealerState Dealer { get; set; } = new();
    [JsonPropertyName("players")]              public Dictionary<string, BlackjackPlayerState> Players { get; set; } = [];
    [JsonPropertyName("turn_order")]           public List<long> TurnOrder        { get; set; } = [];
    [JsonPropertyName("current_turn_user_id")] public long? CurrentTurnUserId    { get; set; }
}

// ── Entity ───────────────────────────────────────────────────────────────────

[Table("blackjack_rounds")]
public class BlackjackRound
{
    [Key, Column("id")]
    public long Id { get; set; }

    [Column("blackjack_table_id")]
    public long BlackjackTableId { get; set; }

    [Column("phase")]
    public string Phase { get; set; } = "betting";

    [Column("state", TypeName = "jsonb")]
    public string StateJson { get; set; } = "{}";

    [Column("current_turn_user_id")]
    public long? CurrentTurnUserId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public BlackjackTable? Table { get; set; }

    // JSON accessor
    private static readonly JsonSerializerOptions _opts = new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    [NotMapped]
    public BlackjackState State
    {
        get => JsonSerializer.Deserialize<BlackjackState>(StateJson, _opts) ?? new BlackjackState();
        set => StateJson = JsonSerializer.Serialize(value, _opts);
    }

    // Sync the EF scalar columns from state
    public void SyncFromState(BlackjackState s)
    {
        Phase              = s.Phase;
        CurrentTurnUserId  = s.CurrentTurnUserId;
        State              = s;
    }
}
