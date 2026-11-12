using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MixPilot.Application.Abstractions.Identity;
using MixPilot.Domain;

namespace MixPilot.Api;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string OwnerKey
    {
        get
        {
            var user = accessor.HttpContext?.User;
            string? subject = user?.FindFirstValue("sub"), issuer = user?.FindFirstValue("iss");
            if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer))
                throw new RuleViolation("authentication_required", "An authenticated subject and issuer are required.", 401);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { issuer, subject }))));
        }
    }
}

// Explicit opt-in for a single local demo user; never accepts identity headers from a client.
public sealed class DevelopmentAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new("sub", "local-dj"), new("iss", "mixpilot-development")], Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new(principal, Scheme.Name)));
    }
}

