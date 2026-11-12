using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MixPilot.Domain;

namespace MixPilot.Api;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return false;
        var rule = exception as RuleViolation;
        int status = rule?.StatusCode ?? 500;
        if (status >= 500) logger.LogError(exception, "Request failed: {TraceId}", context.TraceIdentifier);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new()
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status, Title = status >= 500 ? "An unexpected error occurred" : "The request could not be completed",
                Detail = rule?.Message, Instance = context.Request.Path,
                Type = $"https://httpstatuses.com/{status}",
                Extensions = { ["code"] = rule?.Code ?? "internal_error", ["traceId"] = context.TraceIdentifier }
            }
        });
    }
}
