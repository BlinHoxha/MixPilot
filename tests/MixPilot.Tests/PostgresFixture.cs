using Microsoft.EntityFrameworkCore;
using MixPilot.Application.Abstractions.Identity;
using MixPilot.Infrastructure;
using Npgsql;

namespace MixPilot.Tests;

[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresFixture.Variable)))
            Skip = $"Set {PostgresFixture.Variable} to run isolated PostgreSQL integration tests.";
    }
}

// Owned by each test instance, not IClassFixture: every test gets a fresh database.
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Variable = "MIXPILOT_TEST_POSTGRES";
    private readonly string database = $"mixpilot_test_{Guid.NewGuid():N}";
    private string? maintenance;
    private string connectionString = "";
    private bool created;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new NpgsqlConnectionStringBuilder(configured)
        {
            Database = "postgres", Pooling = false, Timeout = 5, CommandTimeout = 10,
            IncludeErrorDetail = false
        };
        maintenance = builder.ConnectionString;
        builder.Database = database;
        connectionString = builder.ConnectionString;
        try
        {
            await using var admin = new NpgsqlConnection(maintenance);
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await command.ExecuteNonQueryAsync();
            created = true;
            await using var db = Create("migration-owner");
            await db.Database.MigrateAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public MixPilotDbContext Create(string ownerKey) => new(
        new DbContextOptionsBuilder<MixPilotDbContext>()
            .UseNpgsql(connectionString, options => options.CommandTimeout(10)).Options,
        new TestUser(ownerKey));

    public async Task DisposeAsync()
    {
        if (!created) return;
        await using var admin = new NpgsqlConnection(maintenance);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
        created = false;
    }

    private sealed class TestUser(string key) : ICurrentUser { public string OwnerKey => key; }
}
