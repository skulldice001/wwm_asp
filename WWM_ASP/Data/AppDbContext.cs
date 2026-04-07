using Microsoft.EntityFrameworkCore;
using WWM_ASP.Models;

namespace WWM_ASP.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User>               Users               => Set<User>();
    public DbSet<Staff>              Staffs              => Set<Staff>();
    public DbSet<ZooCoinTransaction> ZooCoinTransactions => Set<ZooCoinTransaction>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);

        // ─── User ─────────────────────────────────────────────────────────
        model.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Account).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.DiscordId).IsUnique();

            // Soft delete: exclude deleted rows by default
            e.HasQueryFilter(u => u.DeletedAt == null);
        });

        // ─── Staff ────────────────────────────────────────────────────────
        model.Entity<Staff>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Account).IsUnique();
            e.HasIndex(s => s.Email).IsUnique();

            e.HasQueryFilter(s => s.DeletedAt == null);
        });

        // ─── ZooCoinTransaction ───────────────────────────────────────────
        model.Entity<ZooCoinTransaction>(e =>
        {
            e.HasKey(t => t.Id);

            e.HasOne(t => t.User)
             .WithMany(u => u.ZCoinTransactions)
             .HasForeignKey(t => t.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(t => t.Staff)
             .WithMany()
             .HasForeignKey(t => t.StaffId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(t => t.RelatedUser)
             .WithMany()
             .HasForeignKey(t => t.RelatedUserId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
