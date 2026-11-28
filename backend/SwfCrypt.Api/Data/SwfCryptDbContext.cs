using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace SwfCrypt.Api.Data;

public class SwfCryptDbContext(DbContextOptions<SwfCryptDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<SwfFile> SwfFiles => Set<SwfFile>();
    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();
    public DbSet<AutopsyReport> AutopsyReports => Set<AutopsyReport>();
    public DbSet<ReviewDecision> ReviewDecisions => Set<ReviewDecision>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        if (Database.IsSqlite())
        {
            var dtoConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));

            var nullableDtoConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset?, long?>(
                v => v.HasValue ? v.Value.UtcTicks : null,
                v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);

            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTimeOffset))
                    {
                        property.SetValueConverter(dtoConverter);
                    }
                    else if (property.ClrType == typeof(DateTimeOffset?))
                    {
                        property.SetValueConverter(nullableDtoConverter);
                    }
                }
            }
        }

        builder.Entity<SwfFile>(entity =>
        {
            entity.HasIndex(e => e.Sha256).IsUnique();
            entity.HasIndex(e => e.ReviewStatus);
            entity.HasIndex(e => e.Year);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Author).HasMaxLength(160);
            entity.Property(e => e.Source).HasMaxLength(300).IsRequired();
            entity.Property(e => e.License).HasMaxLength(160).IsRequired();
            entity.Property(e => e.StorageKey).HasMaxLength(260).IsRequired();

            entity.HasOne(e => e.Owner)
                .WithMany(u => u.UploadedFiles)
                .HasForeignKey(e => e.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AnalysisJob>(entity =>
        {
            entity.HasIndex(e => e.State);
            entity.HasOne(e => e.SwfFile)
                .WithOne(f => f.AnalysisJob)
                .HasForeignKey<AnalysisJob>(e => e.SwfFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AutopsyReport>(entity =>
        {
            entity.HasIndex(e => e.Verdict);
            entity.HasIndex(e => e.ActionScriptKind);
            entity.HasOne(e => e.SwfFile)
                .WithOne(f => f.AutopsyReport)
                .HasForeignKey<AutopsyReport>(e => e.SwfFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ReviewDecision>(entity =>
        {
            entity.HasIndex(e => e.DecidedAt);
            entity.HasOne(e => e.SwfFile)
                .WithMany(f => f.ReviewDecisions)
                .HasForeignKey(e => e.SwfFileId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Reviewer)
                .WithMany(u => u.ReviewDecisions)
                .HasForeignKey(e => e.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.Action);
            entity.HasOne(e => e.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}

