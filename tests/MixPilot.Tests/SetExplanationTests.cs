using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using Framework.Contracts.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MixPilot.Application.AI;
using MixPilot.Contracts;

namespace MixPilot.Tests;

public sealed class SetExplanationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static async Task<SetResponse> CreateSet(HttpClient client, string title)
    {
        var track = await client.PostAsJsonAsync("/api/v1/tracks", new CreateTrackRequest
        { Artist = "Artist", Title = title, DurationSeconds = 300, Bpm = 124, Energy = 5, AuthorizationConfirmed = true });
        track.EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync("/api/v1/sets/generate", new GenerateSetRequest
        { Name = title, Requirements = new() { DurationSeconds = 300 } }, Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CommonResult<SetResponse>>(Json))!.Data!;
    }

    [Fact]
    public async Task Framework_grounded_answers_use_only_the_authorized_set_and_do_not_edit_it()
    {
        using var alice = factory.Client(Guid.NewGuid().ToString());
        using var bob = factory.Client(Guid.NewGuid().ToString());
        var first = await CreateSet(alice, "Alice public test track");
        var second = await CreateSet(bob, "Bob private test track");
        var response = await alice.PostAsJsonAsync($"/api/v1/sets/{first.Id}/explanation", new ExplainSetRequest());
        response.EnsureSuccessStatusCode();
        var explanation = (await response.Content.ReadFromJsonAsync<CommonResult<SetExplanationResponse>>(Json))!.Data!;
        Assert.True(explanation.Explanation.Grounded);
        Assert.Equal("local-extractive", explanation.Explanation.Provider);
        Assert.NotEmpty(explanation.Explanation.Citations);
        Assert.All(explanation.Explanation.Citations, c => Assert.Equal(first.Id, c.DocumentId));
        Assert.DoesNotContain(second.Name, explanation.Explanation.Answer);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.PostAsJsonAsync($"/api/v1/sets/{second.Id}/explanation", new ExplainSetRequest())).StatusCode);
        var unchanged = (await alice.GetFromJsonAsync<CommonResult<SetResponse>>($"/api/v1/sets/{first.Id}", Json))!.Data!;
        Assert.Equal(first.Revision, unchanged.Revision);
    }

    [Fact]
    public async Task Provider_failure_does_not_break_deterministic_planning()
    {
        using var customized = factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IChatModel>();
            services.AddSingleton<IChatModel, FailingChatModel>();
        }));
        using var client = customized.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        // Initialize the shared relational test database.
        using var bootstrap = factory.Client("bootstrap");
        var set = await CreateSet(client, "Provider failure test");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync($"/api/v1/sets/{set.Id}/explanation", new ExplainSetRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/sets/{set.Id}")).StatusCode);
    }

    [Fact]
    public async Task Disabled_AI_is_explicit_and_does_not_disable_set_generation()
    {
        using var customized = factory.WithWebHostBuilder(b => b.UseSetting("AI:Enabled", "false"));
        using var client = customized.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        using var bootstrap = factory.Client("bootstrap");
        var set = await CreateSet(client, "Disabled AI test");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,
            (await client.PostAsJsonAsync($"/api/v1/sets/{set.Id}/explanation", new ExplainSetRequest())).StatusCode);
        var capabilities = (await client.GetFromJsonAsync<CommonResult<CapabilitiesResponse>>("/api/v1/capabilities", Json))!.Data!;
        Assert.DoesNotContain("setExplanations", capabilities.EnabledFeatures);
    }

    [Fact]
    public async Task Evidence_retriever_rejects_unprepared_or_mismatched_scope()
    {
        var evidence = new SetExplanationEvidence();
        var tenant = Guid.NewGuid();
        var id = Guid.NewGuid();
        var query = new KnowledgeSearchQuery("Explain", SetDocumentProfileCatalog.ProfileName, tenant, null, [id], 20);
        Assert.Empty(await evidence.SearchAsync(query, default));
        evidence.Prepare(new(id, "Saved", 1, 1, new(), 300, [], [], DateTimeOffset.UtcNow), tenant);
        Assert.Empty(await evidence.SearchAsync(query with { TenantId = Guid.NewGuid() }, default));
        Assert.Empty(await evidence.SearchAsync(query with { ScopeId = Guid.NewGuid() }, default));
        Assert.Empty(await evidence.SearchAsync(query with { DocumentIds = [Guid.NewGuid()] }, default));
        Assert.Single(await evidence.SearchAsync(query, default));
    }

    private sealed class FailingChatModel : IChatModel
    {
        public Task<GroundedGenerationResult> GenerateAsync(GroundedGenerationRequest request, CancellationToken ct) =>
            throw new HttpRequestException("Test provider failure");
    }
}
