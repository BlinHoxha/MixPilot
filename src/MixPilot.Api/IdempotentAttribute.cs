using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Domain;

namespace MixPilot.Api;

[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncResourceFilter
{
    private const int MaxRequestBytes = 1_048_576;
    private const int MaxStoredResponseBytes = 262_144;

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
        {
            await next();
            return;
        }

        string key = header.ToString();
        if (header.Count != 1 || !Regex.IsMatch(key, "^[A-Za-z0-9_-]{1,100}$"))
            throw new RuleViolation("invalid_idempotency_key", "Idempotency-Key must be 1-100 letters, digits, underscores or hyphens.", 400);

        var store = http.RequestServices.GetRequiredService<IIdempotencyStore>();
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();
        var now = clock.GetUtcNow();
        var route = context.ActionDescriptor.AttributeRouteInfo?.Template ?? http.Request.Path.Value ?? "";
        var bodyHash = await HashBody(http.Request, http.RequestAborted);
        var existing = await store.Find(key, http.RequestAborted);
        if (existing is not null && existing.ExpiresAt <= now)
        {
            await store.Release(existing.Id, CancellationToken.None);
            existing = null;
        }
        if (existing is not null)
        {
            if (existing.Method != http.Request.Method || existing.Route != route || existing.BodyHash != bodyHash)
                throw new RuleViolation("idempotency_key_reused", "This key was used with another request.");
            if (existing.State != "completed")
                throw new RuleViolation("idempotency_in_progress", "A request with this key is already in progress.", 409);
            http.Response.Headers["Idempotent-Replayed"] = "true";
            if (existing.Location is not null) http.Response.Headers.Location = existing.Location;
            context.Result = new ContentResult
            {
                Content = existing.ResponseBody,
                ContentType = existing.ContentType ?? "application/json",
                StatusCode = existing.StatusCode
            };
            return;
        }

        var reservation = new IdempotencyRecord
        {
            Key = key, Method = http.Request.Method, Route = route, BodyHash = bodyHash,
            CreatedAt = now, ExpiresAt = now.AddHours(24)
        };
        await store.Reserve(reservation, http.RequestAborted);

        var originalBody = http.Response.Body;
        await using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            ResourceExecutedContext executed;
            try { executed = await next(); }
            catch
            {
                await store.Release(reservation.Id, CancellationToken.None);
                throw;
            }
            if (executed.Exception is { } error && !executed.ExceptionHandled)
            {
                if (error is RuleViolation { StatusCode: < 500 })
                {
                    var handler = new ApiExceptionHandler(
                        http.RequestServices.GetRequiredService<ILogger<ApiExceptionHandler>>(),
                        http.RequestServices.GetRequiredService<IProblemDetailsService>());
                    executed.ExceptionHandled = await handler.TryHandleAsync(http, error, http.RequestAborted);
                }
                if (!executed.ExceptionHandled)
                {
                    await store.Release(reservation.Id, CancellationToken.None);
                    return;
                }
            }

            var bytes = buffer.ToArray();
            if (http.Response.StatusCode >= 500 || bytes.Length > MaxStoredResponseBytes)
            {
                if (bytes.Length > MaxStoredResponseBytes)
                    http.RequestServices.GetRequiredService<ILogger<IdempotentAttribute>>()
                        .LogWarning("Idempotency response exceeded {Limit} bytes; key released.", MaxStoredResponseBytes);
                await store.Release(reservation.Id, CancellationToken.None);
            }
            else
            {
                await store.Complete(reservation.Id, http.Response.StatusCode, Encoding.UTF8.GetString(bytes),
                    http.Response.ContentType ?? "application/json",
                    string.IsNullOrEmpty(http.Response.Headers.Location) ? null : http.Response.Headers.Location.ToString(),
                    CancellationToken.None);
            }
            http.Response.Body = originalBody;
            await originalBody.WriteAsync(bytes, http.RequestAborted);
        }
        finally { http.Response.Body = originalBody; }
    }

    private static async Task<string> HashBody(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering(bufferThreshold: 64 * 1024, bufferLimit: MaxRequestBytes);
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, ct);
        request.Body.Position = 0;
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
    }
}
