using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

public sealed class LegacyObservedEndpointsValidationTests : IClassFixture<TestAppFactory>
{
    private readonly HttpClient _client;

    public LegacyObservedEndpointsValidationTests(TestAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Legacy_manageSiteAssets_requires_mode()
    {
        var resp = await _client.PostAsJsonAsync("/api/siteassets/managesiteassets", new
        {
            mode = "" // invalid
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Legacy_manageSiteAssets_update_requires_assetId_and_update_payload()
    {
        var resp = await _client.PostAsJsonAsync("/api/siteassets/managesiteassets", new
        {
            mode = "update"
            // assetId missing
            // update missing
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Legacy_removeSiteAsset_requires_required_fields()
    {
        var resp = await _client.PostAsJsonAsync("/api/siteassets/removesiteasset", new
        {
            assetId = (long?)null,
            modifiedBy = "",
            correlationId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
