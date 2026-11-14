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

public sealed class JobOwnerContext(IHttpContextAccessor accessor)
{
    private string? ownerKey;
    public string? OwnerKey => ownerKey;

    public void SetForWorker(string key)
    {
        if (accessor.HttpContext is not null)
            throw new InvalidOperationException("A job owner cannot be assigned during an HTTP request.");
        if (ownerKey is not null)
            throw new InvalidOperationException("The job owner is already assigned for this scope.");
        if (key.Length != 64 || !key.All(Uri.IsHexDigit))
            throw new ArgumentException("A valid owner key is required.", nameof(key));
        ownerKey = key;
    }
}

public sealed class ScopedCurrentUser(JobOwnerContext job, HttpCurrentUser http) : ICurrentUser
{
    public string OwnerKey => job.OwnerKey ?? http.OwnerKey;
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

