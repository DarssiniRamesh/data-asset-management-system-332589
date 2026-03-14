using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

public sealed class IdempotencyCopyAssetTests : IClassFixture<TestAppFactory>
{
    private readonly HttpClient _client;

    public IdempotencyCopyAssetTests(TestAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CopyAsset_with_same_idempotency_key_returns_same_response_on_retry()
    {
        // Note: Tests intentionally avoid assuming database availability.
        // In typical CI for this repo, DB is not configured => CopyAsset returns 503.
        // Idempotency must still replay the exact prior response for retries.
        var request = new
        {
            copyOperationId = "op-123",
            copyPerformedBy = "tester",
            correlationId = "corr-123",
            targetAsset = new
            {
                siteId = "S1",
                assetGroup = "AG",
                processGroup = "PG",
                processGroupOtherText = (string?)null,
                assetName = "A1",
                permitEuId = "P1",
                globalUniqueAssetId = "GUID-1",
                assetDescription = (string?)null,
                stationaryFlag = (bool?)null,
                parentPseudoAssetId = (long?)null
            }
        };

        var idempotencyKey = "same-key-1";

        using var msg1 = new HttpRequestMessage(HttpMethod.Post, "/api/assets/1/copy")
        {
            Content = JsonContent.Create(request)
        };
        msg1.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);

        using var resp1 = await _client.SendAsync(msg1);
        var body1 = await resp1.Content.ReadAsStringAsync();

        using var msg2 = new HttpRequestMessage(HttpMethod.Post, "/api/assets/1/copy")
        {
            Content = JsonContent.Create(request)
        };
        msg2.Headers.TryAddWithoutValidation("X-Idempotency-Key", idempotencyKey);

        using var resp2 = await _client.SendAsync(msg2);
        var body2 = await resp2.Content.ReadAsStringAsync();

        Assert.Equal(resp1.StatusCode, resp2.StatusCode);
        Assert.Equal(body1, body2);

        // Optional but useful: middleware adds replay diagnostic header.
        Assert.True(resp2.Headers.TryGetValues("X-Idempotency-Replayed", out var replayValues));
        Assert.Contains("true", replayValues);

        // For current repo expectations when DB isn't configured:
        Assert.Equal(HttpStatusCode.ServiceUnavailable, resp1.StatusCode);
    }
}
