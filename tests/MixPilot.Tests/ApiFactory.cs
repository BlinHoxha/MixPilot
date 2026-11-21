using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MixPilot.Infrastructure;
using MixPilot.Application.Abstractions.Identity;

namespace MixPilot.Tests;

public sealed class ApiFactory : WebApplicationFactory<Api.Program>
{
    private readonly string connectionString = $"Data Source=mixpilot-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection keepAliveConnection;
    private readonly Action<IServiceCollection>? configureServices;
    public ApiFactory()
    {
        // Keep the database alive while each context opens its own connection for concurrent hosted-worker access.
        keepAliveConnection = new SqliteConnection(connectionString);
        keepAliveConnection.Open();
    }
    internal ApiFactory(Action<IServiceCollection> configureServices) : this() => this.configureServices = configureServices;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jobs:Enabled", "false");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDbContextOptionsConfiguration<MixPilotDbContext>>();
            services.RemoveAll<DbContextOptions<MixPilotDbContext>>(); services.RemoveAll<MixPilotDbContext>();
            services.AddDbContext<MixPilotDbContext>(o => o.UseSqlite(connectionString).ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>());
            services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
            configureServices?.Invoke(services);
        });
    }
    public MixPilotDbContext OwnerContext(string subject)
    {
        var ownerKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { "test-issuer", subject }))));
        var options = new DbContextOptionsBuilder<MixPilotDbContext>().UseSqlite(connectionString)
            .ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>().Options;
        return new MixPilotDbContext(options, new TestUser(ownerKey));
    }
    private sealed class TestUser(string key) : ICurrentUser { public string OwnerKey => key; }
    public HttpClient Client(string? subject = null)
    {
        var client = CreateClient(new() { AllowAutoRedirect = false });
        using var scope = Services.CreateScope(); scope.ServiceProvider.GetRequiredService<MixPilotDbContext>().Database.EnsureCreated();
        if (subject is not null) client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        return client;
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) keepAliveConnection.Dispose(); }
}

// SQLite cannot order DateTimeOffset/decimal natively. Test-only conversions preserve UTC ordering
// and exercise real relational constraints/transactions; the production provider remains PostgreSQL.
public sealed class SqliteTestModelCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(t => t.GetProperties()))
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (type == typeof(DateTimeOffset)) property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
            if (type == typeof(decimal)) property.SetValueConverter(new ValueConverter<decimal, double>(v => (double)v, v => (decimal)v));
        }
    }
}

public sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Subject", out var subject)) return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sub", subject.ToString()), new("iss", "test-issuer")], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new(principal, Scheme.Name)));
    }
}
