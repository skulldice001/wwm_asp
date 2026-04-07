using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace WWM_ASP.Models;

[Table("events")]
public class Event
{
    [Column("id")]
    public long Id { get; set; }

    [Column("title")]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("type")]
    [MaxLength(50)]
    public string Type { get; set; } = TypeCasual;   // casual | guild_war | lucky_draw

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "upcoming";  // upcoming | ongoing | completed | cancelled

    [Column("rules")]
    public string? Rules { get; set; }

    [Column("rewards")]
    public string? Rewards { get; set; }

    [Column("start_time")]
    public DateTime? StartTime { get; set; }

    [Column("end_time")]
    public DateTime? EndTime { get; set; }

    [Column("location")]
    [MaxLength(255)]
    public string? Location { get; set; }

    [Column("discord_id")]
    [MaxLength(255)]
    public string? DiscordId { get; set; }

    [Column("formation_data", TypeName = "jsonb")]
    public string? FormationDataJson { get; set; }

    [Column("lucky_draw_data", TypeName = "jsonb")]
    public string? LuckyDrawDataJson { get; set; }

    [Column("created_by")]
    public long? CreatedBy { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    // ─── Type constants ────────────────────────────────────────────────────
    public const string TypeCasual    = "casual";
    public const string TypeGuildWar  = "guild_war";
    public const string TypeLuckyDraw = "lucky_draw";

    // ─── Helpers ───────────────────────────────────────────────────────────
    public bool IsDeleted   => DeletedAt.HasValue;
    public bool IsLuckyDraw => Type == TypeLuckyDraw;
    public bool IsGuildWar  => Type == TypeGuildWar;
    public bool IsActive    => Status is "upcoming" or "ongoing";

    public bool LuckyDrawPending
    {
        get
        {
            if (!IsLuckyDraw || LuckyDrawData == null) return false;
            var drawAt  = LuckyDrawData.GetValueOrDefault("draw_at")?.ToString();
            var drawnAt = LuckyDrawData.GetValueOrDefault("drawn_at")?.ToString();
            return !string.IsNullOrEmpty(drawAt) &&
                   string.IsNullOrEmpty(drawnAt) &&
                   DateTime.TryParse(drawAt, out var dt) &&
                   DateTime.UtcNow >= dt;
        }
    }

    public bool LuckyDrawDrawn
    {
        get
        {
            if (!IsLuckyDraw || LuckyDrawData == null) return false;
            var drawnAt = LuckyDrawData.GetValueOrDefault("drawn_at")?.ToString();
            return !string.IsNullOrEmpty(drawnAt);
        }
    }

    public string TypeLabel => Type switch
    {
        TypeCasual    => "Casual",
        TypeGuildWar  => "Guild War",
        TypeLuckyDraw => "Lucky Draw",
        _             => Type,
    };

    public string StatusLabel => Status switch
    {
        "upcoming"  => "Upcoming",
        "ongoing"   => "Ongoing",
        "completed" => "Completed",
        "cancelled" => "Cancelled",
        _           => Status,
    };

    public string StatusBadge => Status switch
    {
        "upcoming"  => "badge-warning",
        "ongoing"   => "badge-success",
        "completed" => "badge-secondary",
        "cancelled" => "badge-danger",
        _           => "badge-light",
    };

    // ─── JSON accessors ────────────────────────────────────────────────────
    [NotMapped]
    public Dictionary<string, object?>? LuckyDrawData
    {
        get => string.IsNullOrEmpty(LuckyDrawDataJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object?>>(LuckyDrawDataJson);
        set => LuckyDrawDataJson = value == null ? null : JsonSerializer.Serialize(value);
    }

    // ─── Navigation ────────────────────────────────────────────────────────
    [ForeignKey(nameof(CreatedBy))]
    public Staff? Creator { get; set; }

    public ICollection<EventParticipant> EventParticipants { get; set; } = [];
}
