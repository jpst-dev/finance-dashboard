using FinanceDashboard.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FinanceDashboard.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<BalanceCheckpoint> Checkpoints => Set<BalanceCheckpoint>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    // Read per query by the global filters (EF parameterizes it).
    private string? CurrentUserId => currentUser.UserId;

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Account>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Institution).HasMaxLength(200);
            e.Property(x => x.ExternalIdentifier).HasMaxLength(200);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.Ignore(x => x.IsLiability);
            e.HasIndex(x => x.UserId);
            e.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        b.Entity<Category>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.UserId);
            e.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            e.Property(x => x.OriginalDescription).HasMaxLength(500);
            e.Property(x => x.NormalizedDescription).HasMaxLength(500);
            e.Property(x => x.SourceKey).HasMaxLength(200);
            e.Property(x => x.CounterpartyName).HasMaxLength(200);
            e.Property(x => x.CounterpartyDocument).HasMaxLength(64);
            e.Property(x => x.CardSuffix).HasMaxLength(4);
            e.Property(x => x.CardKind).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(32);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.Date });
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.AccountId, x.SourceKey, x.Ordinal })
                .IsUnique()
                .HasFilter("\"SourceKey\" IS NOT NULL");
            e.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        b.Entity<BalanceCheckpoint>(e =>
        {
            e.ToTable("Checkpoints");
            e.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(32);
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.Date });
            e.HasIndex(x => x.UserId);
            e.HasQueryFilter(x => x.UserId == CurrentUserId);
        });

        b.Entity<ImportBatch>(e =>
        {
            e.Property(x => x.FileHash).HasMaxLength(64);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.DeclaredTotal).HasColumnType("numeric(18,2)");
            e.Property(x => x.LedgerBalance).HasColumnType("numeric(18,2)");
            e.Property(x => x.PreviewJson).HasColumnType("jsonb");
            e.Property(x => x.WarningsJson).HasColumnType("jsonb");
            e.HasOne<Account>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.FileHash })
                .IsUnique()
                .HasFilter("\"State\" = 'Confirmed'");
            e.HasQueryFilter(x => x.UserId == CurrentUserId);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampUser();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampUser();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampUser()
    {
        foreach (var entry in ChangeTracker.Entries<IUserOwned>().Where(e => e.State == EntityState.Added))
        {
            if (!string.IsNullOrEmpty(entry.Entity.UserId)) continue;
            entry.Entity.UserId = CurrentUserId
                ?? throw new InvalidOperationException("No authenticated user to own the new record.");
        }
    }
}
