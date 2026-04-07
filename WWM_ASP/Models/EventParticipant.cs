using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("event_user")]
public class EventParticipant
{
    [Column("event_id")]
    public long EventId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("preferred_time")]
    [MaxLength(20)]
    public string? PreferredTime { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    // ─── Navigation ────────────────────────────────────────────────────────
    [ForeignKey(nameof(EventId))]
    public Event? Event { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
