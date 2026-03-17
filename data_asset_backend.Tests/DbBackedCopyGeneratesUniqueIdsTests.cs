using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

[RequiresDatabase]
public sealed class DbBackedCopyGeneratesUniqueIdsTests : IClassFixture<DbTestAppFactory>
{
    private readonly DbTestAppFactory _factory;

    public DbBackedCopyGeneratesUniqueIdsTests(DbTestAppFactory factory)
    {
        _factory = factory;
    }

    [DbFact]
    public async Task Copy_canonical_overrides_target_ids_server_side_so_reusing_source_ids_does_not_conflict()
    {
        using var client = _factory.CreateAuthedClient();

        // Create a source asset with unique IDs.
        var sourceGlobal = $"guaid-src-{Guid.NewGuid():N}";
        var sourcePermit = $"permit-src-{Guid.NewGuid():N}";

        var createResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-1",
            assetGroup = "group-1",
            processGroup = "process-1",
            assetName = "copy-src",
            permitEuId = sourcePermit,
            globalUniqueAssetId = sourceGlobal
        });

        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);

        var created = await createResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(created);
        var sourceAssetId = created!.assetId;

        // Intentionally send a copy request that reuses the source IDs.
        // Server must override these during copy.
        var copyResp = await client.PostAsJsonAsync($"/api/assets/{sourceAssetId}/copy", new
        {
            copyOperationId = $"op-{Guid.NewGuid():N}",
            copyPerformedBy = "tester",
            correlationId = $"corr-{Guid.NewGuid():N}",
            targetAsset = new
            {
                siteId = "site-1",
                assetGroup = "group-1",
                processGroup = "process-1",
                processGroupOtherText = (string?)null,
                assetName = "copy-target",
                permitEuId = sourcePermit,
                globalUniqueAssetId = sourceGlobal,
                assetDescription = (string?)null,
                stationaryFlag = (bool?)null,
                parentPseudoAssetId = (long?)null
            }
        });

        Assert.Equal(HttpStatusCode.Created, copyResp.StatusCode);

        var copyBody = await copyResp.Content.ReadFromJsonAsync<CopyAssetResponseDto>();
        Assert.NotNull(copyBody);

        var targetId = copyBody!.targetAsset.assetId;
        Assert.True(targetId > 0);
        Assert.NotEqual(sourceAssetId, targetId);

        // Fetch the target asset and assert the IDs are not equal to source.
        var targetAsset = await GetAssetAsync(client, targetId);
        Assert.NotEqual(sourceGlobal, targetAsset.globalUniqueAssetId);
        Assert.NotEqual(sourcePermit, targetAsset.permitEuId);
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

    private sealed record AssetDetailsDto(long assetId, string globalUniqueAssetId, string permitEuId);
}
