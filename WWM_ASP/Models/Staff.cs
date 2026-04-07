using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WWM_ASP.Models;

[Table("staffs")]
public class Staff
{
    [Column("id")]
    public long Id { get; set; }

    [Column("name")]
    [MaxLength(255)]
    public string? Name { get; set; }

    [Column("account")]
    [MaxLength(255)]
    public string? Account { get; set; }

    [Column("email")]
    [MaxLength(255)]
    public string? Email { get; set; }

    [Column("password")]
    public string? Password { get; set; }

    [Column("role")]
    [MaxLength(50)]
    public string Role { get; set; } = RoleObserver;

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; }

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [Column("deleted_at")]
    public DateTime? DeletedAt { get; set; }

    // ─── Role constants ────────────────────────────────────────────────────
    public const string RoleMaster    = "master";
    public const string RoleAdmin     = "admin";
    public const string RoleObserver  = "observer";
    public const string RoleLibrarian = "librarian";

    // ─── Role helpers ──────────────────────────────────────────────────────
    public bool IsMaster    => Role == RoleMaster;
    public bool IsAdmin     => Role is RoleMaster or RoleAdmin;
    public bool IsObserver  => Role == RoleObserver;
    public bool IsLibrarian => Role == RoleLibrarian;
    public bool IsDeleted   => DeletedAt.HasValue;

    public bool CanManageLibrary =>
        Role is RoleMaster or RoleAdmin or RoleLibrarian;

    public int RoleRank => Role switch
    {
        RoleMaster    => 3,
        RoleAdmin     => 2,
        RoleObserver  => 1,
        RoleLibrarian => 1,
        _             => 0,
    };

    /// <summary>Can this staff manage another staff member?</summary>
    public bool CanManage(Staff other) =>
        Id == other.Id || RoleRank > other.RoleRank;

    public string DisplayName => Account ?? Name ?? "Staff";

    public string RoleLabel => Role switch
    {
        RoleMaster    => "Master",
        RoleAdmin     => "Admin",
        RoleObserver  => "Observer",
        RoleLibrarian => "Librarian",
        _             => Role,
    };
}
