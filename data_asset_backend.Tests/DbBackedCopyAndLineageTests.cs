using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

[RequiresDatabase]
public sealed class DbBackedCopyAndLineageTests : IClassFixture<DbTestAppFactory>
{
    private readonly DbTestAppFactory _factory;

    public DbBackedCopyAndLineageTests(DbTestAppFactory factory)
    {
        _factory = factory;
    }

    [DbFact]
    public async Task Copy_asset_creates_lineage_records_persisted_in_db()
    {
        using var client = _factory.CreateAuthedClient();

        var createResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-1",
            assetGroup = "group-1",
            processGroup = "process-1",
            assetName = "copy-src",
            permitEuId = "permit-2",
            globalUniqueAssetId = "guaid-2"
        });

        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var source = await createResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(source);

        var copyResp = await client.PostAsJsonAsync($"/api/assets/{source!.assetId}/copy", new
        {
            targetSiteId = "site-1",
            targetAssetName = "copy-target",
            idempotencyKey = "copy-idem-1"
        });

        Assert.Equal(HttpStatusCode.Created, copyResp.StatusCode);

        var copy = await copyResp.Content.ReadFromJsonAsync<CopyAssetResponseDto>();
        Assert.NotNull(copy);

        // Query lineage by copyOperationId; should return at least one row.
        var lineageResp = await client.GetAsync($"/api/asset-copy-lineage?CopyOperationId={WebUtility.UrlEncode(copy!.copyOperationId)}");
        Assert.Equal(HttpStatusCode.OK, lineageResp.StatusCode);

        var lineage = await lineageResp.Content.ReadFromJsonAsync<AssetCopyLineageDto[]>();
        Assert.NotNull(lineage);
        Assert.NotEmpty(lineage!);
    }

    private sealed record AssetDto(long assetId);

    private sealed record CopyAssetResponseDto(string copyOperationId);

    private sealed record AssetCopyLineageDto(string copyOperationId);
}
