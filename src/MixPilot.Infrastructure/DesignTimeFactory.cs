using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MixPilot.Application.Abstractions.Identity;

namespace MixPilot.Infrastructure;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<MixPilotDbContext>
{
    public MixPilotDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable("ConnectionStrings__MixPilot")
            ?? "Host=localhost;Port=5432;Database=mixpilot;Username=mixpilot;Password=mixpilot_dev";
        return new(new DbContextOptionsBuilder<MixPilotDbContext>().UseNpgsql(connection).Options, new DesignUser());
    }
    private sealed class DesignUser : ICurrentUser { public string OwnerKey => "design-time"; }
}

