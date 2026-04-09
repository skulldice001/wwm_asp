using Microsoft.EntityFrameworkCore;
using WWM_ASP.Models;

namespace WWM_ASP.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User>               Users               => Set<User>();
    public DbSet<Staff>              Staffs              => Set<Staff>();
    public DbSet<ZooCoinTransaction> ZooCoinTransactions => Set<ZooCoinTransaction>();
    public DbSet<LibraryArticle>     LibraryArticles     => Set<LibraryArticle>();
    public DbSet<Event>              Events              => Set<Event>();
    public DbSet<EventParticipant>   EventParticipants   => Set<EventParticipant>();

    // Entertainment tables
    public DbSet<PokerTable>        PokerTables        => Set<PokerTable>();
    public DbSet<TaixiuTable>       TaixiuTables       => Set<TaixiuTable>();
    public DbSet<TaixiuTablePlayer> TaixiuTablePlayers => Set<TaixiuTablePlayer>();
    public DbSet<TaixiuGame>        TaixiuGames        => Set<TaixiuGame>();
    public DbSet<TaixiuMessage>     TaixiuMessages     => Set<TaixiuMessage>();
    public DbSet<BlackjackTable>    BlackjackTables    => Set<BlackjackTable>();
    public DbSet<BingoTable>        BingoTables        => Set<BingoTable>();
    public DbSet<TienLenTable>      TienLenTables      => Set<TienLenTable>();
    public DbSet<LotteryDraw>       LotteryDraws       => Set<LotteryDraw>();
    public DbSet<LotteryTicket>     LotteryTickets     => Set<LotteryTicket>();

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

        // ─── LibraryArticle ───────────────────────────────────────────────
        model.Entity<LibraryArticle>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasQueryFilter(a => a.DeletedAt == null);

            e.HasOne(a => a.Creator)
             .WithMany()
             .HasForeignKey(a => a.CreatedBy)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // ─── Event ────────────────────────────────────────────────────────
        model.Entity<Event>(e =>
        {
            e.HasKey(ev => ev.Id);
            e.HasQueryFilter(ev => ev.DeletedAt == null);

            e.HasOne(ev => ev.Creator)
             .WithMany()
             .HasForeignKey(ev => ev.CreatedBy)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(ev => ev.EventParticipants)
             .WithOne(ep => ep.Event)
             .HasForeignKey(ep => ep.EventId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ─── EventParticipant ─────────────────────────────────────────────
        model.Entity<EventParticipant>(e =>
        {
            e.HasKey(ep => new { ep.EventId, ep.UserId });

            e.HasOne(ep => ep.User)
             .WithMany()
             .HasForeignKey(ep => ep.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
