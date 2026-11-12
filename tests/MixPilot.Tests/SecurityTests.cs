using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using MixPilot.Api;
using MixPilot.Domain;

namespace MixPilot.Tests;

public sealed class SecurityTests
{
    private static HttpCurrentUser User(string issuer, string subject) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new("iss", issuer), new("sub", subject)], "test"))
        }
    });

    [Fact]
    public void Subject_ids_are_namespaced_by_the_validated_issuer()
    {
        Assert.NotEqual(User("issuer-a", "same-user").OwnerKey, User("issuer-b", "same-user").OwnerKey);
        Assert.Equal(User("issuer-a", "same-user").OwnerKey, User("issuer-a", "same-user").OwnerKey);
        Assert.Throws<RuleViolation>(() => new HttpCurrentUser(new HttpContextAccessor { HttpContext = null }).OwnerKey);
    }

    [Fact]
    public void Development_identity_cannot_be_enabled_in_production()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Authentication:UseDevelopmentIdentity", "true");
        });
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("development identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
