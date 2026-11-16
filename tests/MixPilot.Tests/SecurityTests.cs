using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using MixPilot.Api;
using MixPilot.Domain;
using System.Net;
using System.Text.Json.Nodes;

namespace MixPilot.Tests;

public sealed class SecurityTests
{
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    [InlineData("*/*")]
    public async Task Status_responses_use_problem_json_regardless_of_accept(string accept)
    {
        using var factory = new ApiFactory();
        using var anonymous = factory.Client();
        using var authenticated = factory.Client(Guid.NewGuid().ToString());
        foreach (var (client, path, status, code) in new[]
        {
            (anonymous, "/api/v1/sets", HttpStatusCode.Unauthorized, "authentication_required"),
            (authenticated, "/api/v1/no-such-route", HttpStatusCode.NotFound, "not_found")
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.TryAddWithoutValidation("Accept", accept);
            using var response = await client.SendAsync(request);
            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            Assert.Equal(code, problem["code"]?.GetValue<string>());
            Assert.Equal((int)status, problem["status"]?.GetValue<int>());
            Assert.False(string.IsNullOrWhiteSpace(problem["traceId"]?.GetValue<string>()));
            Assert.False(string.IsNullOrWhiteSpace(problem["title"]?.GetValue<string>()));
        }
    }
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
