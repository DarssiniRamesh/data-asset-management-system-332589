using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

[RequiresDatabase]
public sealed class DbBackedDependencySafeDeleteTests : IClassFixture<DbTestAppFactory>
{
    private readonly DbTestAppFactory _factory;

    public DbBackedDependencySafeDeleteTests(DbTestAppFactory factory)
    {
        _factory = factory;
    }

    [DbFact]
    public async Task Delete_asset_is_persistent_and_subsequent_get_returns_404()
    {
        using var client = _factory.CreateAuthedClient();

        var createResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-1",
            assetGroup = "group-1",
            processGroup = "process-1",
            assetName = "delete-me",
            permitEuId = "permit-3",
            globalUniqueAssetId = "guaid-3"
        });

        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var asset = await createResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(asset);

        var delResp = await client.DeleteAsync($"/api/assets/{asset!.assetId}");
        Assert.Equal(HttpStatusCode.NoContent, delResp.StatusCode);

        var getResp = await client.GetAsync($"/api/assets/{asset.assetId}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    private sealed record AssetDto(long assetId);
}
