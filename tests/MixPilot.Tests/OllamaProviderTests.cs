using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIFramework.Core.Abstractions;
using AIFramework.Core.Models;
using AIFramework.Ollama;
using Framework.Contracts.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using MixPilot.Application.Abstractions.Identity;
using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;
using MixPilot.Api;
using MixPilot.Contracts;
using MixPilot.Infrastructure;

namespace MixPilot.Tests;

public sealed class OllamaProviderTests
{
    private static readonly SetIntentRequest Request = new() { Prompt = "House for an hour", Territory = "HU" };

    [Fact]
    public void Development_registration_keeps_explanations_and_resolves_ollama_case_insensitively()
    {
        using var factory = new ApiFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("AI:Provider", "oLlAmA"));
        using var client = host.CreateClient();
        Assert.IsType<OllamaGenerationModel>(host.Services.GetRequiredService<IStructuredGenerationModel>());
        Assert.NotNull(host.Services.GetRequiredService<IChatModel>());
    }

    [Theory]
    [InlineData("Ollama")]
    [InlineData("ollama")]
    public void Production_rejects_ollama(string provider)
    {
        using var factory = new ApiFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")
            .UseSetting("Authentication:UseDevelopmentIdentity", "false")
            .UseSetting("Authentication:Authority", "https://identity.example.test/")
            .UseSetting("Authentication:Audience", "mixpilot")
            .UseSetting("AI:Enabled", "true").UseSetting("AI:Provider", provider)
            .UseSetting("AI:Ollama:Endpoint", "http://localhost:11434/")
            .UseSetting("AI:Ollama:Model", "qwen3:4b"));
        var error = Assert.Throws<InvalidOperationException>(() => host.CreateClient());
        Assert.Contains("The Ollama AI provider is intended for Development only.", error.ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("http://example.com/", "qwen3:4b")]
    [InlineData("http://localhost:11434/api", "qwen3:4b")]
    [InlineData("http://localhost:11434/", "bad model")]
    [InlineData("http://localhost:11434/", "qwen3:4b", "invalid", null)]
    [InlineData("http://localhost:11434/", "qwen3:4b", "511", null)]
    [InlineData("http://localhost:11434/", "qwen3:4b", null, "601")]
    public void Invalid_configuration_fails_during_registration(string? endpoint, string? model,
        string? contextTokens = null, string? timeoutSeconds = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:MixPilot"] = "Host=localhost;Database=test",
            ["AI:Enabled"] = "true", ["AI:Provider"] = "Ollama",
            ["AI:Ollama:Endpoint"] = endpoint, ["AI:Ollama:Model"] = model,
            ["AI:Ollama:ContextTokens"] = contextTokens, ["AI:Ollama:TimeoutSeconds"] = timeoutSeconds
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        Assert.ThrowsAny<Exception>(() => new ServiceCollection().AddMixPilot(config));
    }

    [Theory]
    [InlineData("valid", HttpStatusCode.OK, null)]
    [InlineData("invalid", HttpStatusCode.UnprocessableEntity, "prompt_not_interpretable")]
    [InlineData("length", HttpStatusCode.UnprocessableEntity, "prompt_not_interpretable")]
    [InlineData("notfound", HttpStatusCode.ServiceUnavailable, "ai_provider_unavailable")]
    [InlineData("slow", HttpStatusCode.ServiceUnavailable, "ai_provider_timeout")]
    public async Task Loopback_replies_map_to_set_intent_contract(string kind, HttpStatusCode status, string? code)
    {
        await using var stub = new OllamaStub(kind);
        using var factory = new ApiFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("AI:Provider", "Ollama")
            .UseSetting("AI:Ollama:Endpoint", stub.Endpoint)
            .UseSetting("AI:Ollama:TimeoutSeconds", kind == "slow" ? "1" : "120"));
        using var client = host.CreateClient();
        using (var scope = host.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<MixPilotDbContext>().Database.EnsureCreated();
        client.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync("/api/v1/set-intents", Request);
        Assert.Equal(status, response.StatusCode);
        if (code is not null)
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        else
        {
            var result = (await response.Content.ReadFromJsonAsync<JsonObject>())!["data"]!;
            Assert.Equal("ollama", result["provider"]!.GetValue<string>());
            Assert.Equal("qwen3:4b", result["model"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Closed_loopback_port_is_unavailable()
    {
        int port;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        using var factory = new ApiFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("AI:Provider", "Ollama")
            .UseSetting("AI:Ollama:Endpoint", $"http://127.0.0.1:{port}/"));
        using var client = host.CreateClient();
        using (var scope = host.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<MixPilotDbContext>().Database.EnsureCreated();
        client.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync("/api/v1/set-intents", Request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("ai_provider_unavailable", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Schema_invalid_ollama_reply_fails_prompt_job_with_same_code()
    {
        await using var stub = new OllamaStub("invalid");
        using var factory = new ApiFactory();
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("AI:Provider", "Ollama")
            .UseSetting("AI:Ollama:Endpoint", stub.Endpoint));
        using var client = host.CreateClient();
        using (var scope = host.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<MixPilotDbContext>().Database.EnsureCreated();
        client.DefaultRequestHeaders.Add("X-Test-Subject", Guid.NewGuid().ToString());
        using var submission = await client.PostAsJsonAsync("/api/v1/set-generation-jobs",
            new CreateSetGenerationJobRequest { Name = "Prompt", Prompt = Request.Prompt, Territory = Request.Territory });
        submission.EnsureSuccessStatusCode();
        var job = (await submission.Content.ReadFromJsonAsync<CommonResult<SetGenerationJobResponse>>())!.Data!;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var claim = (await scope.ServiceProvider.GetRequiredService<IJobClaimRepository>()
                .ClaimNext(DateTimeOffset.UtcNow, CancellationToken.None))!;
            await using var ownerScope = host.Services.CreateAsyncScope();
            ownerScope.ServiceProvider.GetRequiredService<JobOwnerContext>().SetForWorker(claim.OwnerKey);
            await ownerScope.ServiceProvider.GetRequiredService<ISetGenerationJobProcessor>()
                .Process(claim.Id, claim.Attempt, CancellationToken.None);
        }
        var failed = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{job.Id}"))!.Data!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal("prompt_not_interpretable", failed.ErrorCode);
    }

    [Theory]
    [InlineData(false, "ai_provider_unavailable", HttpStatusCode.ServiceUnavailable)]
    [InlineData(true, "prompt_not_interpretable", HttpStatusCode.UnprocessableEntity)]
    public async Task Model_error_is_mapped_for_preview_and_prompt_job(bool invalidOutput, string code, HttpStatusCode status)
    {
        using var factory = new ApiFactory(services =>
        {
            services.RemoveAll<IStructuredGenerationModel>();
            services.AddSingleton<IStructuredGenerationModel>(new ThrowingModel(invalidOutput));
        });
        using var client = factory.Client(Guid.NewGuid().ToString());
        using var preview = await client.PostAsJsonAsync("/api/v1/set-intents", Request);
        Assert.Equal(status, preview.StatusCode);
        Assert.Equal(code, (await preview.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());

        // The job's failure path records the same code as the synchronous preview.
        using var submission = await client.PostAsJsonAsync("/api/v1/set-generation-jobs",
            new CreateSetGenerationJobRequest { Name = "Prompt", Prompt = Request.Prompt, Territory = Request.Territory });
        submission.EnsureSuccessStatusCode();
        var job = (await submission.Content.ReadFromJsonAsync<CommonResult<SetGenerationJobResponse>>())!.Data!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var claim = (await scope.ServiceProvider.GetRequiredService<IJobClaimRepository>()
                .ClaimNext(DateTimeOffset.UtcNow, CancellationToken.None))!;
            await using var ownerScope = factory.Services.CreateAsyncScope();
            ownerScope.ServiceProvider.GetRequiredService<JobOwnerContext>().SetForWorker(claim.OwnerKey);
            await ownerScope.ServiceProvider.GetRequiredService<ISetGenerationJobProcessor>()
                .Process(claim.Id, claim.Attempt, CancellationToken.None);
        }
        var failed = (await client.GetFromJsonAsync<CommonResult<SetGenerationJobResponse>>($"/api/v1/jobs/{job.Id}"))!.Data!;
        Assert.Equal("failed", failed.Status);
        Assert.Equal(code, failed.ErrorCode);
    }

    private sealed class ThrowingModel(bool invalidOutput) : IStructuredGenerationModel
    {
        public Task<StructuredGenerationResult> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct) =>
            throw (invalidOutput ? new StructuredGenerationException("invalid_response") : new ArgumentException("Context bound exceeded."));
    }

    private sealed class OllamaStub : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly Task server;
        public string Endpoint { get; }

        public OllamaStub(string kind)
        {
            listener.Start();
            Endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            server = Serve(kind);
        }

        private async Task Serve(string kind)
        {
            try
            {
                using var socket = await listener.AcceptTcpClientAsync();
                await using var stream = socket.GetStream();
                byte[] buffer = new byte[4096];
                var headers = new StringBuilder();
                while (!headers.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    int read = await stream.ReadAsync(buffer);
                    if (read == 0) return;
                    headers.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }
                if (kind == "slow") await Task.Delay(1500);
                var output = kind == "invalid" ? "{}" :
                    "{\"durationMinutes\":60,\"minBpm\":120,\"maxBpm\":130,\"genre\":null,\"mood\":null,\"hasVocals\":false,\"energyCurve\":\"wave\",\"assumptions\":[]}";
                var body = JsonSerializer.Serialize(new
                {
                    model = "qwen3:4b", done = true, done_reason = kind == "length" ? "length" : "stop",
                    message = new { content = output }
                });
                var bytes = Encoding.UTF8.GetBytes(body);
                var header = kind == "notfound" ? "HTTP/1.1 404 Not Found" : "HTTP/1.1 200 OK";
                var prefix = Encoding.ASCII.GetBytes($"{header}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(prefix);
                await stream.WriteAsync(bytes);
            }
            catch (Exception e) when (e is SocketException or IOException or ObjectDisposedException) { }
        }

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            await server;
        }
    }
}
