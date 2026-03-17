using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

[RequiresDatabase]
public sealed class DbBackedLegacyCopyGeneratesUniqueGlobalIdTests : IClassFixture<DbTestAppFactory>
{
    private readonly DbTestAppFactory _factory;

    public DbBackedLegacyCopyGeneratesUniqueGlobalIdTests(DbTestAppFactory factory)
    {
        _factory = factory;
    }

    [DbFact]
    public async Task Copy_legacy_generates_new_global_unique_asset_id_each_time_to_avoid_conflicts()
    {
        using var client = _factory.CreateAuthedClient();

        // Create a source asset.
        var createResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-1",
            assetGroup = "group-1",
            processGroup = "process-1",
            assetName = "legacy-copy-src",
            permitEuId = "permit-src",
            globalUniqueAssetId = $"guaid-src-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var source = await createResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(source);
        Assert.True(source!.assetId > 0);

        // Perform legacy copy twice. If global_unique_asset_id were reused, second call would 409.
        var copy1 = await client.PostAsJsonAsync($"/api/assets/{source.assetId}/copy-legacy", new
        {
            targetSiteId = "site-1",
            targetAssetName = "legacy-copy-target-1",
            idempotencyKey = "k1"
        });

        Assert.Equal(HttpStatusCode.Created, copy1.StatusCode);
        var copyResp1 = await copy1.Content.ReadFromJsonAsync<CopyAssetResponseDto>();
        Assert.NotNull(copyResp1);

        var copy2 = await client.PostAsJsonAsync($"/api/assets/{source.assetId}/copy-legacy", new
        {
            targetSiteId = "site-1",
            targetAssetName = "legacy-copy-target-2",
            idempotencyKey = "k2"
        });

        Assert.Equal(HttpStatusCode.Created, copy2.StatusCode);
        var copyResp2 = await copy2.Content.ReadFromJsonAsync<CopyAssetResponseDto>();
        Assert.NotNull(copyResp2);

        Assert.NotEqual(copyResp1!.targetAsset.assetId, copyResp2!.targetAsset.assetId);

        // Fetch both target assets and assert their globalUniqueAssetId values are distinct and not equal to source.
        var t1 = await GetAssetAsync(client, copyResp1.targetAsset.assetId);
        var t2 = await GetAssetAsync(client, copyResp2.targetAsset.assetId);

        Assert.False(string.IsNullOrWhiteSpace(t1.globalUniqueAssetId));
        Assert.False(string.IsNullOrWhiteSpace(t2.globalUniqueAssetId));
        Assert.NotEqual(t1.globalUniqueAssetId, t2.globalUniqueAssetId);
        Assert.NotEqual(t1.globalUniqueAssetId, (await GetAssetAsync(client, source.assetId)).globalUniqueAssetId);
        Assert.NotEqual(t2.globalUniqueAssetId, (await GetAssetAsync(client, source.assetId)).globalUniqueAssetId);
    }

    private static async Task<AssetDetailsDto> GetAssetAsync(HttpClient client, long id)
    {
        var resp = await client.GetAsync($"/api/assets/{id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<AssetDetailsDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private sealed record AssetDto(long assetId);

    private sealed record CopyAssetResponseDto(AssetDto targetAsset);

    private sealed record AssetDetailsDto(long assetId, string globalUniqueAssetId);
}
