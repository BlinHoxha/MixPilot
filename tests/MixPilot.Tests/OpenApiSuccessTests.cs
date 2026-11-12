using System.Text.Json.Nodes;

namespace MixPilot.Tests;

public sealed class OpenApiSuccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Every_operation_advertises_a_successful_response_and_json_schema()
    {
        using var client = factory.Client("openapi-contract-test");
        var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"))!;
        foreach (var path in document["paths"]!.AsObject())
        foreach (var operation in path.Value!.AsObject())
        {
            var responses = operation.Value!["responses"]!.AsObject();
            var successful = responses.Where(r => r.Key.StartsWith('2')).ToArray();
            Assert.True(successful.Length > 0, $"Missing success response: {operation.Key} {path.Key}");
            foreach (var response in successful.Where(r => r.Key != "204"))
                Assert.NotNull(response.Value?["content"]?["application/json"]?["schema"]);
        }
    }
}