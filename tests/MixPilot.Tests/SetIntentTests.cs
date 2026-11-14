using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using Framework.Contracts.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.AI;
using MixPilot.Contracts;

namespace MixPilot.Tests;

public sealed class SetIntentTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private const string ValidOutput = """
        {"durationMinutes":60,"minBpm":120,"maxBpm":130,"genre":"House","mood":"Late","hasVocals":false,"energyCurve":"wave","assumptions":[]}
        """;

    private static ApiFactory WithModel(FakeModel model) => new(services =>
    {
        services.RemoveAll<IStructuredGenerationModel>();
        services.AddSingleton<IStructuredGenerationModel>(model);
    });

    private static async Task<string> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>();

    [Fact]
    public void Schema_and_pure_mapping_validate_and_correct_values()
    {
        StructuredJson.ValidateSchema(SetIntentSchema.Value);
        using var output = JsonDocument.Parse("""
            {"durationMinutes":999,"minBpm":280,"maxBpm":80,"genre":"Unknown","mood":"late","hasVocals":null,"energyCurve":null,"assumptions":[]}
            """);
        var mapped = SetIntentMapping.Map(output.RootElement, new TrackVocabulary(["House"], ["Late"]), "HU", "fake", null);
        Assert.Equal(3600, mapped.Requirements.DurationSeconds);
        Assert.Equal(110, mapped.Requirements.MinBpm);
        Assert.Equal(140, mapped.Requirements.MaxBpm);
        Assert.Null(mapped.Requirements.Genre);
        Assert.Equal("Late", mapped.Requirements.Mood);
        Assert.Equal("HU", mapped.Requirements.Territory);
        Assert.Equal(PlanningMode.Planning, mapped.Requirements.Mode);
        Assert.Empty(mapped.Requirements.ExcludedTrackIds);
        Assert.Contains(mapped.Assumptions, value => value.Contains("Duration"));
        Assert.Contains(mapped.Assumptions, value => value.Contains("BPM range"));
        Assert.Single(mapped.Unresolved);
    }

    [Fact]
    public async Task Endpoint_uses_only_owner_vocabulary_and_returns_valid_preview()
    {
        var model = new FakeModel { Output = ValidOutput };
        using var factory = WithModel(model);
        var alice = Guid.NewGuid().ToString();
        var bob = Guid.NewGuid().ToString();
        using var aliceClient = factory.Client(alice);
        using var bobClient = factory.Client(bob);
        var created = await aliceClient.PostAsJsonAsync("/api/v1/tracks", new CreateTrackRequest
        { Artist = "SecretArtist", Title = "SecretTrackTitle", Genre = "House", Mood = "Late", DurationSeconds = 300, AuthorizationConfirmed = true });
        created.EnsureSuccessStatusCode();
        var track = (await created.Content.ReadFromJsonAsync<CommonResult<TrackResponse>>(Json))!.Data!;
        await using (var db = factory.OwnerContext(alice))
        {
            var entity = await db.Tracks.SingleAsync(t => t.Id == track.Id);
            entity.SourceUrl = "https://example.test/private-audio";
            await db.SaveChangesAsync();
        }
        var bobTrack = await bobClient.PostAsJsonAsync("/api/v1/tracks", new CreateTrackRequest
        { Artist = "Other", Title = "Other title", Genre = "Techno", Mood = "Dark", DurationSeconds = 300, AuthorizationConfirmed = true });
        bobTrack.EnsureSuccessStatusCode();

        var response = await aliceClient.PostAsJsonAsync("/api/v1/set-intents", new SetIntentRequest { Prompt = "A 60 minute house set", Territory = "DE" });
        response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<CommonResult<SetIntentResponse>>(Json))!.Data!;
        Assert.Equal("House", preview.Requirements.Genre);
        Assert.Equal("Late", preview.Requirements.Mood);
        Assert.Equal("DE", preview.Requirements.Territory);
        Assert.Equal(PlanningMode.Planning, preview.Requirements.Mode);
        Assert.Empty(preview.Requirements.ExcludedTrackIds);
        Assert.Equal("fake", preview.Provider);
        Assert.Equal("fake-model", preview.Model);
        Assert.Empty(preview.Unresolved);
        Assert.NotNull(model.LastRequest);
        Assert.Equal(512, model.LastRequest.MaxOutputTokens);
        Assert.Equal("A 60 minute house set", model.LastRequest.Input);
        Assert.Contains("House", model.LastRequest.Instructions);
        Assert.Contains("Late", model.LastRequest.Instructions);
        Assert.DoesNotContain("Techno", model.LastRequest.Instructions);
        Assert.DoesNotContain("Dark", model.LastRequest.Instructions);
        foreach (var secret in new[] { "SecretArtist", "SecretTrackTitle", track.Id.ToString(), "private-audio", "https://example.test" })
            Assert.DoesNotContain(secret, model.LastRequest.Instructions + model.LastRequest.Input);

        var other = await bobClient.PostAsJsonAsync("/api/v1/set-intents", new SetIntentRequest { Prompt = "House", Territory = "US" });
        other.EnsureSuccessStatusCode();
        var otherPreview = (await other.Content.ReadFromJsonAsync<CommonResult<SetIntentResponse>>(Json))!.Data!;
        Assert.Null(otherPreview.Requirements.Genre);
        Assert.Null(otherPreview.Requirements.Mood);
        Assert.Equal(2, otherPreview.Unresolved.Length);
    }

    [Theory]
    [InlineData("refused", "")]
    [InlineData("incomplete", "")]
    [InlineData("completed", "{}")]
    public async Task Noncompleted_or_schema_mismatched_model_response_is_uninterpretable(string status, string output)
    {
        var model = new FakeModel { Output = output, Status = Enum.Parse<StructuredGenerationStatus>(status, true) };
        using var factory = WithModel(model);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var response = await client.PostAsJsonAsync("/api/v1/set-intents", new SetIntentRequest { Prompt = "House", Territory = "HU" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("prompt_not_interpretable", await Code(response));
    }

    [Theory]
    [InlineData(false, "ai_provider_unavailable")]
    [InlineData(true, "ai_provider_timeout")]
    public async Task Provider_failures_return_service_unavailable(bool timeout, string code)
    {
        var model = new FakeModel { Failure = timeout ? new OperationCanceledException() : new HttpRequestException("Offline") };
        using var factory = WithModel(model);
        using var client = factory.Client(Guid.NewGuid().ToString());
        var response = await client.PostAsJsonAsync("/api/v1/set-intents", new SetIntentRequest { Prompt = "House", Territory = "HU" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(code, await Code(response));
    }

    [Theory]
    [InlineData("invalid_response")]
    [InlineData("response_too_large")]
    [InlineData("another_structured_error")]
    public async Task Every_structured_generation_error_is_uninterpretable(string code)
    {
        using var factory = WithModel(new FakeModel { Failure = new StructuredGenerationException(code) });
        using var client = factory.Client(Guid.NewGuid().ToString());
        var response = await client.PostAsJsonAsync("/api/v1/set-intents", new SetIntentRequest { Prompt = "House", Territory = "HU" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("prompt_not_interpretable", await Code(response));
    }

    [Fact]
    public void Unresolved_model_text_is_bounded()
    {
        using var output = JsonDocument.Parse(ValidOutput.Replace("House", new string('x', 400)));
        var result = SetIntentMapping.Map(output.RootElement, new TrackVocabulary([], ["Late"]), "HU", "fake", null);
        Assert.Single(result.Unresolved);
        Assert.DoesNotContain(new string('x', 101), result.Unresolved[0]);
        Assert.Contains(new string('x', 100), result.Unresolved[0]);
    }

    [Fact]
    public async Task Availability_capabilities_and_request_validation_follow_configuration()
    {
        using var plain = new ApiFactory();
        using var plainClient = plain.Client(Guid.NewGuid().ToString());
        var request = new SetIntentRequest { Prompt = "House", Territory = "HU" };
        var unavailable = await plainClient.PostAsJsonAsync("/api/v1/set-intents", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("prompt_interpretation_unavailable", await Code(unavailable));
        var plainCapabilities = (await plainClient.GetFromJsonAsync<CommonResult<CapabilitiesResponse>>("/api/v1/capabilities", Json))!.Data!;
        Assert.False(plainCapabilities.PromptInterpretationAvailable);
        Assert.Contains("promptInterpretation", plainCapabilities.PlannedFeatures);
        Assert.Equal(HttpStatusCode.BadRequest, (await plainClient.PostAsJsonAsync("/api/v1/set-intents",
            new SetIntentRequest { Prompt = new string('x', 1001), Territory = "HU" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await plainClient.PostAsJsonAsync("/api/v1/set-intents",
            new SetIntentRequest { Prompt = "House", Territory = "hu" })).StatusCode);

        var model = new FakeModel { Output = ValidOutput };
        using var configured = WithModel(model);
        using var configuredClient = configured.Client(Guid.NewGuid().ToString());
        var capabilities = (await configuredClient.GetFromJsonAsync<CommonResult<CapabilitiesResponse>>("/api/v1/capabilities", Json))!.Data!;
        Assert.True(capabilities.PromptInterpretationAvailable);
        Assert.Contains("promptInterpretation", capabilities.EnabledFeatures);
        Assert.DoesNotContain("promptInterpretation", capabilities.PlannedFeatures);

        using var disabled = configured.WithWebHostBuilder(builder => builder.UseSetting("AI:Enabled", "false"));
        using var disabledClient = disabled.CreateClient();
        disabledClient.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        var disabledCapabilities = (await disabledClient.GetFromJsonAsync<CommonResult<CapabilitiesResponse>>("/api/v1/capabilities", Json))!.Data!;
        Assert.False(disabledCapabilities.PromptInterpretationAvailable);
        var disabledResponse = await disabledClient.PostAsJsonAsync("/api/v1/set-intents", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, disabledResponse.StatusCode);
    }

    private sealed class FakeModel : IStructuredGenerationModel
    {
        public string? Output { get; init; }
        public StructuredGenerationStatus Status { get; init; } = StructuredGenerationStatus.Completed;
        public Exception? Failure { get; init; }
        public StructuredGenerationRequest? LastRequest { get; private set; }
        public Task<StructuredGenerationResult> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct)
        {
            LastRequest = request;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new StructuredGenerationResult(Status, Output, "fake", new(null, null)) { Model = "fake-model" });
        }
    }
}
