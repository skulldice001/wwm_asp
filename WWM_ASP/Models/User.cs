using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("users")]
public class User
{
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    [MaxLength(255)]
    public string? Name { get; set; }

    /// <summary>Login username (unique, required)</summary>
    [Column("account")]
    [MaxLength(255)]
    public string? Account { get; set; }

    [Column("email")]
    [MaxLength(255)]
    public string? Email { get; set; }

    [Column("password")]
    public string? Password { get; set; }

    // Discord OAuth fields
    [Column("discord_id")]
    [MaxLength(255)]
    public string? DiscordId { get; set; }

    [Column("discord_token")]
    public string? DiscordToken { get; set; }

    [Column("discord_refresh_token")]
    public string? DiscordRefreshToken { get; set; }

    [Column("discord_avatar")]
    [MaxLength(255)]
    public string? DiscordAvatar { get; set; }

    [Column("avatar")]
    [MaxLength(255)]
    public string? Avatar { get; set; }

    // Profile fields
    [Column("country")]
    [MaxLength(255)]
    public string? Country { get; set; }

    [Column("online_from")]
    [MaxLength(255)]
    public string? OnlineFrom { get; set; }

    [Column("online_to")]
    [MaxLength(255)]
    public string? OnlineTo { get; set; }

    [Column("ingame_name")]
    [MaxLength(255)]
    public string? IngameName { get; set; }

    [Column("ingame_id")]
    [MaxLength(255)]
    public string? IngameId { get; set; }

    // Zoo-coin
    [Column("z_coins")]
    public int ZCoins { get; set; } = 5000;

    [Column("z_coins_frozen")]
    public int ZCoinsFrozen { get; set; } = 0;

    // Skill references
    [Column("main_skill_id")]
    public long? MainSkillId { get; set; }

    [Column("sub_skill_id")]
    public long? SubSkillId { get; set; }

    // Timestamps (Laravel convention)
    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }  // soft delete

    // ─── Computed helpers ──────────────────────────────────────────────────
    public int AvailableZCoins => Math.Max(0, ZCoins - ZCoinsFrozen);

    public bool IsDeleted => DeletedAt.HasValue;

    /// <summary>Best display name: Account > Name > "Unknown"</summary>
    public string DisplayName => Account ?? Name ?? "Unknown";

    /// <summary>Avatar URL — Discord CDN or local upload or initials placeholder</summary>
    public string AvatarUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(Avatar)) return Avatar;
            if (!string.IsNullOrEmpty(DiscordId) && !string.IsNullOrEmpty(DiscordAvatar))
                return $"https://cdn.discordapp.com/avatars/{DiscordId}/{DiscordAvatar}.png";
            return $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(DisplayName)}&background=random";
        }
    }

    // ─── Navigation properties ─────────────────────────────────────────────
    public ICollection<ZooCoinTransaction> ZCoinTransactions { get; set; } = [];
}
