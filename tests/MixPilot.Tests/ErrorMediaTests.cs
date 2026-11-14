using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MixPilot.Contracts;

namespace MixPilot.Tests;

public sealed class ErrorMediaTests
{
    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/json")]
    public async Task Rule_violations_ignore_accept_and_keep_problem_details(string accept)
    {
        using var factory = new ApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Accept.ParseAdd(accept);
        var unavailable = await client.PostAsJsonAsync("/api/v1/set-intents", new SetIntentRequest { Prompt = "House", Territory = "HU" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("application/problem+json", unavailable.Content.Headers.ContentType!.MediaType);
        var problem = (await unavailable.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("prompt_interpretation_unavailable", problem["code"]!.GetValue<string>());
        Assert.Equal("The request could not be completed", problem["title"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(problem["detail"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(problem["traceId"]!.GetValue<string>()));

        var missing = await client.GetAsync($"/api/v1/sets/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType!.MediaType);
        var missingProblem = (await missing.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("not_found", missingProblem["code"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(missingProblem["detail"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(missingProblem["traceId"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Unexpected_error_hides_exception_details()
    {
        using var factory = new ApiFactory(services =>
        {
            services.RemoveAll<IStructuredGenerationModel>();
            services.AddSingleton<IStructuredGenerationModel, ThrowingModel>();
        });
        using var client = factory.Client(Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Accept.ParseAdd("text/plain");
        var response = await client.PostAsJsonAsync("/api/v1/set-intents", new SetIntentRequest { Prompt = "House", Territory = "HU" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var problem = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal("internal_error", problem["code"]!.GetValue<string>());
        Assert.Equal("An unexpected error occurred", problem["title"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(problem["traceId"]!.GetValue<string>()));
        Assert.DoesNotContain("Secret test exception", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApi_success_responses_advertise_only_json()
    {
        using var factory = new ApiFactory();
        using var client = factory.Client(Guid.NewGuid().ToString());
        var document = (await client.GetFromJsonAsync<JsonObject>("/swagger/v1/swagger.json"))!;
        foreach (var path in document["paths"]!.AsObject())
        foreach (var operation in path.Value!.AsObject())
        foreach (var response in operation.Value!["responses"]!.AsObject())
        {
            if (!response.Key.StartsWith('2')) continue;
            var content = response.Value?["content"]?.AsObject();
            if (content is not null) Assert.Equal(["application/json"], content.Select(item => item.Key).ToArray());
        }
    }

    private sealed class ThrowingModel : IStructuredGenerationModel
    {
        public Task<StructuredGenerationResult> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("Secret test exception");
    }
}
