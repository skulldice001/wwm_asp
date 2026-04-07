using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WWM_ASP.Models;

// ─── State classes (serialized to/from the jsonb column) ──────────────────────

public class TaixiuState
{
    [JsonPropertyName("phase")]               public string          Phase             { get; set; } = "betting";
    [JsonPropertyName("dice")]                public int[]?          Dice              { get; set; }
    [JsonPropertyName("sum")]                 public int?            Sum               { get; set; }
    [JsonPropertyName("outcome")]             public string?         Outcome           { get; set; }
    [JsonPropertyName("bet_deadline_at")]     public long?           BetDeadlineAt     { get; set; }
    [JsonPropertyName("result_deadline_at")]  public long?           ResultDeadlineAt  { get; set; }
    [JsonPropertyName("bets")]                public List<TaixiuBetEntry> Bets         { get; set; } = [];
    [JsonPropertyName("log")]                 public List<string>    Log               { get; set; } = [];
}

public class TaixiuBetEntry
{
    [JsonPropertyName("user_id")] public long    UserId  { get; set; }
    [JsonPropertyName("name")]    public string  Name    { get; set; } = "";
    [JsonPropertyName("choice")]  public string  Choice  { get; set; } = "";
    [JsonPropertyName("amount")]  public int     Amount  { get; set; }
    [JsonPropertyName("payout")]  public int?    Payout  { get; set; }
    [JsonPropertyName("result")]  public string? Result  { get; set; }
}

// ─── EF Core model ─────────────────────────────────────────────────────────────

[Table("taixiu_games")]
public class TaixiuGame
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("taixiu_table_id")]
    public long TaixiuTableId { get; set; }

    [Column("state", TypeName = "jsonb")]
    public string StateJson { get; set; } = "{}";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // ─── Convenience accessor ──────────────────────────────────────────────
    private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

    [NotMapped]
    public TaixiuState State
    {
        get => JsonSerializer.Deserialize<TaixiuState>(StateJson, _opts) ?? new();
        set => StateJson = JsonSerializer.Serialize(value);
    }
}
