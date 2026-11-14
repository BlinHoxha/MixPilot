using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MixPilot.Application.Abstractions.Identity;
using MixPilot.Application.Services;
using MixPilot.Application.Mapping;
using MixPilot.Infrastructure.Persistence.Repositories;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using MixPilot.Domain;
using MixPilot.Infrastructure;

namespace MixPilot.Tests;

public sealed class PersistenceTests
{
    private sealed class User(string key) : ICurrentUser { public string OwnerKey => key; }

    [Fact]
    public async Task Database_detects_two_concurrent_writers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MixPilotDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>().Options;
        await using var first = new MixPilotDbContext(options, new User("owner"));
        await first.Database.EnsureCreatedAsync();
        var track = new Track { Id = Guid.NewGuid(), Artist = "A", Title = "B", DurationSeconds = 300 };
        first.Tracks.Add(track); await first.SaveChangesAsync();
        await using var second = new MixPilotDbContext(options, new User("owner"));
        var competing = await second.Tracks.SingleAsync();
        track.Title = "First edit"; track.Revision++; await first.SaveChangesAsync();
        competing.Title = "Second edit"; competing.Revision++;
        Assert.Equal("revision_conflict", (await Assert.ThrowsAsync<RuleViolation>(() => new TrackRepository(second, NullLogger<TrackRepository>.Instance, new MapperConfiguration(c => c.AddProfile<MixPilotMappingProfile>()).CreateMapper()).SaveChanges(default))).Code);
    }

    [Fact]
    public void PostgreSql_migration_generates_schema_without_a_server()
    {
        using var db = new DesignTimeFactory().CreateDbContext([]);
        Assert.Equal(4, db.Database.GetMigrations().Count());
        string script = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE \"Tracks\"", script);
        Assert.Contains("CREATE TABLE \"SetVersion\"", script);
        Assert.Contains("ADD \"ApprovedVersion\"", script);
        Assert.Contains("ADD \"ApprovedAt\"", script);
        Assert.Contains("CREATE TABLE \"IdempotencyRecords\"", script);
        Assert.Contains("IX_IdempotencyRecords_OwnerKey_Key", script);
        Assert.Contains("FK_CollectionTrack_Tracks_TrackId", script);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Domain_and_contracts_do_not_reference_infrastructure_or_the_host()
    {
        foreach (var assembly in new[] { typeof(Track).Assembly, typeof(Contracts.TrackResponse).Assembly, typeof(TrackService).Assembly })
        {
            var names = assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
            Assert.DoesNotContain(names, n => n.Contains("EntityFrameworkCore") || n.Contains("MixPilot.Infrastructure") || n.Contains("MixPilot.Api"));
        }
    }
}

