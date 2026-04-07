using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("tienlen_tables")]
public class TienLenTable
{
    [Key] [Column("id")]
    public long Id { get; set; }

    [Column("owner_id")]
    public long OwnerId { get; set; }

    [Column("name")]
    public string Name { get; set; } = "";

    [Column("variant")]
    public string Variant { get; set; } = "mien_nam";

    [Column("entry_fee")]
    public int EntryFee { get; set; } = 100;

    [Column("status")]
    public string Status { get; set; } = "waiting";

    [Column("is_ai_mode")]
    public bool IsAiMode { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}
