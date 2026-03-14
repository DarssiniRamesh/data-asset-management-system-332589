using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

public sealed class HealthAndValidationEndpointsTests : IClassFixture<TestAppFactory>
{
    private readonly HttpClient _client;

    public HealthAndValidationEndpointsTests(TestAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Root_health_returns_200()
    {
        var resp = await _client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Healthz_returns_200_even_when_db_is_not_configured()
    {
        var resp = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Body is expected to be JSON, but we don't hard-rely on exact shape here.
        var contentType = resp.Content.Headers.ContentType?.MediaType;
        Assert.True(contentType is null || contentType.Contains("json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validity_check_rejects_empty_value()
    {
        var resp = await _client.PostAsJsonAsync("/api/validity-check", new { value = "" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ValidityCheckResponse>();
        Assert.NotNull(body);
        Assert.False(body!.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(body.Reason));
    }

    [Fact]
    public async Task Validity_check_accepts_simple_value()
    {
        var resp = await _client.PostAsJsonAsync("/api/validity-check", new { value = "ABC123" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ValidityCheckResponse>();
        Assert.NotNull(body);
        Assert.True(body!.IsValid);
        Assert.True(string.IsNullOrWhiteSpace(body.Reason));
    }

    private sealed class ValidityCheckResponse
    {
        public bool IsValid { get; set; }
        public string? Reason { get; set; }
    }
}
