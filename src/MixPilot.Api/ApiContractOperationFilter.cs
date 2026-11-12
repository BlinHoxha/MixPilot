using Microsoft.OpenApi.Models;
using MixPilot.Contracts;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MixPilot.Api;

public sealed class ApiContractOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // IDs are stable for code generation and independent of route formatting conventions.
        operation.OperationId = context.MethodInfo.DeclaringType!.Name.Replace("Controller", "") + "_" + context.MethodInfo.Name;
        var schema = context.SchemaGenerator.GenerateSchema(typeof(ApiProblemResponse), context.SchemaRepository);
        if (operation.Responses.ContainsKey("503"))
            operation.Responses["503"] = new OpenApiResponse
            {
                Description = "AI is disabled or its provider is unavailable",
                Content = { ["application/problem+json"] = new() { Schema = schema } }
            };
        foreach (var (code, description) in new[]
        {
            ("400", "Request validation failed"), ("401", "Authentication required"),
            ("403", "Permission denied"), ("404", "Resource not found"),
            ("409", "Revision or resource conflict"), ("422", "Domain constraints not satisfied"),
            ("500", "Unexpected server error")
        })
        {
            operation.Responses[code] = new OpenApiResponse
            {
                Description = description,
                Content = { ["application/problem+json"] = new() { Schema = schema } }
            };
        }
    }
}
