using System.Text.Json;
using Framework.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MixPilot.Application.Abstractions.Identity;
using MixPilot.Domain;

namespace MixPilot.Infrastructure;

public sealed class MixPilotDbContext(DbContextOptions<MixPilotDbContext> options, ICurrentUser user) : FrameworkDbContext(options)
{
    public string OwnerKey => user.OwnerKey;
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<DjProfile> Profiles => Set<DjProfile>();
    public DbSet<DjSet> Sets => Set<DjSet>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<SetGenerationJob> SetGenerationJobs => Set<SetGenerationJob>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        base.OnModelCreating(model);
        ConfigureOwned(model.Entity<Track>());
        ConfigureOwned(model.Entity<Collection>());
        ConfigureOwned(model.Entity<DjProfile>());
        ConfigureOwned(model.Entity<DjSet>());
        model.Entity<Track>(b =>
        {
            b.Property(t => t.Artist).HasMaxLength(200); b.Property(t => t.Title).HasMaxLength(300);
            b.Property(t => t.Version).HasMaxLength(200); b.Property(t => t.IdentityKey).HasMaxLength(710);
            b.Property(t => t.Genre).HasMaxLength(100); b.Property(t => t.Mood).HasMaxLength(100);
            b.Property(t => t.MusicalKey).HasMaxLength(3); b.Property(t => t.Bpm).HasPrecision(6, 2);
            b.Property(t => t.Source).HasConversion<string>().HasMaxLength(32);
            b.Property(t => t.Rights).HasConversion(r => JsonSerializer.Serialize(r, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<RightsProfile>(json, (JsonSerializerOptions?)null)!);
            b.HasIndex(t => new { t.OwnerKey, t.IdentityKey });
        });
        model.Entity<Collection>(b =>
        {
            b.Property(c => c.Name).HasMaxLength(150);
            b.HasMany(c => c.Tracks).WithOne().HasForeignKey(t => t.CollectionId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<CollectionTrack>(b =>
        {
            b.HasKey(t => new { t.CollectionId, t.TrackId });
            b.HasOne<Track>().WithMany().HasForeignKey(t => t.TrackId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<DjProfile>(b =>
        {
            b.HasIndex(p => p.OwnerKey).IsUnique();
            b.Property(p => p.DisplayName).HasMaxLength(100); b.Property(p => p.PreferredGenre).HasMaxLength(100);
            b.Property(p => p.MinBpm).HasPrecision(6, 2); b.Property(p => p.MaxBpm).HasPrecision(6, 2);
        });
        model.Entity<DjSet>(b =>
        {
            b.Property(s => s.Name).HasMaxLength(150);
            b.HasMany(s => s.Versions).WithOne().HasForeignKey(v => v.SetId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<SetVersion>().HasKey(v => new { v.SetId, v.Number });
        model.Entity<IdempotencyRecord>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.OwnerKey).HasMaxLength(64).IsRequired();
            b.Property(r => r.Key).HasMaxLength(100).IsRequired();
            b.Property(r => r.Method).HasMaxLength(10).IsRequired();
            b.Property(r => r.Route).HasMaxLength(200).IsRequired();
            b.Property(r => r.BodyHash).HasMaxLength(64).IsRequired();
            b.Property(r => r.State).HasMaxLength(16).IsRequired();
            b.Property(r => r.ResponseBody).HasMaxLength(262144);
            b.Property(r => r.ContentType).HasMaxLength(100);
            b.Property(r => r.Location).HasMaxLength(500);
            b.HasIndex(r => new { r.OwnerKey, r.Key }).IsUnique();
            b.HasQueryFilter(r => r.OwnerKey == OwnerKey);
        });
        model.Entity<SetGenerationJob>(b =>
        {
            b.HasKey(j => j.Id);
            b.Property(j => j.OwnerKey).HasMaxLength(64).IsRequired();
            b.Property(j => j.Name).HasMaxLength(150).IsRequired();
            b.Property(j => j.RequirementsJson).HasMaxLength(65536);
            b.Property(j => j.Prompt).HasMaxLength(1000);
            b.Property(j => j.Territory).HasMaxLength(2);
            b.Property(j => j.Status).HasMaxLength(16).IsRequired();
            b.Property(j => j.ErrorCode).HasMaxLength(100);
            b.Property(j => j.CorrelationId).HasMaxLength(100).IsRequired();
            b.HasIndex(j => new { j.OwnerKey, j.CreatedAt });
            b.HasIndex(j => new { j.Status, j.LeaseExpiresAt });
            b.HasQueryFilter(j => j.OwnerKey == OwnerKey);
        });
    }

    private void ConfigureOwned<T>(EntityTypeBuilder<T> entity) where T : OwnedEntity
    {
        entity.HasKey(e => e.Id);
        entity.Property(e => e.OwnerKey).HasMaxLength(64).IsRequired();
        entity.Property(e => e.Revision).IsConcurrencyToken();
        entity.HasIndex(e => new { e.OwnerKey, e.CreatedAt });
        entity.HasQueryFilter(e => e.OwnerKey == OwnerKey);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<SetGenerationJob>())
        {
            if (entry.State == EntityState.Added) entry.Entity.OwnerKey = OwnerKey;
            else if ((entry.State is EntityState.Modified or EntityState.Deleted) &&
                     (entry.Entity.OwnerKey != OwnerKey || entry.OriginalValues.GetValue<string>(nameof(SetGenerationJob.OwnerKey)) != OwnerKey))
                throw new RuleViolation("forbidden", "This operation is not permitted.", 403);
        }
        foreach (var entry in ChangeTracker.Entries<IdempotencyRecord>())
        {
            if (entry.State == EntityState.Added) entry.Entity.OwnerKey = OwnerKey;
            else if ((entry.State is EntityState.Modified or EntityState.Deleted) &&
                     (entry.Entity.OwnerKey != OwnerKey || entry.OriginalValues.GetValue<string>(nameof(IdempotencyRecord.OwnerKey)) != OwnerKey))
                throw new RuleViolation("forbidden", "This operation is not permitted.", 403);
        }
        foreach (var entry in ChangeTracker.Entries<OwnedEntity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.OwnerKey = OwnerKey;
            else if ((entry.State is EntityState.Modified or EntityState.Deleted) &&
                     (entry.Entity.OwnerKey != OwnerKey || entry.OriginalValues.GetValue<string>(nameof(OwnedEntity.OwnerKey)) != OwnerKey))
                throw new RuleViolation("forbidden", "This operation is not permitted.", 403);
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

