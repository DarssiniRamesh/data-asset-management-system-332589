using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

public sealed class DbBackedAssetLifecycleTests : IClassFixture<DbTestAppFactory>
{
    private readonly DbTestAppFactory _factory;

    public DbBackedAssetLifecycleTests(DbTestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_then_get_asset_roundtrips_through_database()
    {
        using var client = _factory.CreateAuthedClient();

        var createResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-1",
            assetGroup = "group-1",
            processGroup = "process-1",
            assetName = "asset-a",
            permitEuId = "permit-1",
            globalUniqueAssetId = "guaid-1"
        });

        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var created = await createResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(created);
        Assert.True(created!.assetId > 0);

        var getResp = await client.GetAsync($"/api/assets/{created.assetId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);

        var fetched = await getResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(fetched);
        Assert.Equal(created.assetId, fetched!.assetId);
        Assert.Equal("asset-a", fetched.assetName);
    }

    private sealed record AssetDto(long assetId, string assetName);
}
