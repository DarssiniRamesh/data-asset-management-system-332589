using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

/// <summary>
/// DB-backed integration tests that cover BRD copy semantics and lineage behavior.
/// Focuses on closing Gap 3/4/5 from the gap summary.
/// </summary>
public sealed class DbBackedCopyAndLineageTests : IClassFixture<DbTestAppFactory>
{
    private readonly HttpClient _client;

    public DbBackedCopyAndLineageTests(DbTestAppFactory factory)
    {
        _client = factory.CreateAuthedClient();
    }

    [Fact]
    public async Task Copy_asset_creates_new_asset_id_and_does_not_mutate_source()
    {
        // REQ: FR-03 - Copy asset creates a new asset record with new ID; source is not mutated.
        var sourcePermit = $"PERMIT-SRC-{Guid.NewGuid():N}";
        var sourceGlobal = $"GUID-{Guid.NewGuid():N}";

        var createSource = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Source Asset",
            permitEuId = sourcePermit,
            globalUniqueAssetId = sourceGlobal,
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-src-create"
        });
        Assert.Equal(HttpStatusCode.Created, createSource.StatusCode);

        var srcJson = await createSource.Content.ReadFromJsonAsync<JsonElement>();
        var sourceId = srcJson.GetProperty("assetId").GetInt64();

        var copyOpId = $"op-{Guid.NewGuid():N}";
        var targetGlobal = $"GUID-{Guid.NewGuid():N}";
        var targetPermit = $"PERMIT-TGT-{Guid.NewGuid():N}";

        var copy = await _client.PostAsJsonAsync($"/api/assets/{sourceId}/copy", new
        {
            copyOperationId = copyOpId,
            copyPerformedBy = "tester",
            correlationId = "corr-copy-1",
            targetAsset = new
            {
                siteId = "S-DB",
                assetGroup = "AG",
                processGroup = "PG",
                processGroupOtherText = (string?)null,
                assetName = "Target Asset",
                permitEuId = targetPermit,
                globalUniqueAssetId = targetGlobal,
                assetDescription = (string?)null,
                stationaryFlag = (bool?)null,
                parentPseudoAssetId = (long?)null
            }
        });

        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);

        var copyJson = await copy.Content.ReadFromJsonAsync<JsonElement>();
        var targetAsset = copyJson.GetProperty("targetAsset");
        var targetId = targetAsset.GetProperty("assetId").GetInt64();

        Assert.NotEqual(sourceId, targetId);

        // Source should still be readable with original values.
        var getSource = await _client.GetAsync($"/api/assets/{sourceId}");
        Assert.Equal(HttpStatusCode.OK, getSource.StatusCode);

        var getSrcJson = await getSource.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Source Asset", getSrcJson.GetProperty("assetName").GetString());
        Assert.Equal(sourcePermit, getSrcJson.GetProperty("permitEuId").GetString());
        Assert.Equal(sourceGlobal, getSrcJson.GetProperty("globalUniqueAssetId").GetString());
    }

    [Fact]
    public async Task Copy_creates_lineage_and_query_by_copy_operation_id_returns_result()
    {
        // REQ: FR-03 / BRD §6.13 - Copy lineage is created and can be queried by filters.
        var createSource = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Source For Lineage",
            permitEuId = $"PERMIT-LIN-SRC-{Guid.NewGuid():N}",
            globalUniqueAssetId = $"GUID-{Guid.NewGuid():N}",
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-lin-src"
        });
        Assert.Equal(HttpStatusCode.Created, createSource.StatusCode);

        var srcJson = await createSource.Content.ReadFromJsonAsync<JsonElement>();
        var sourceId = srcJson.GetProperty("assetId").GetInt64();

        var copyOpId = $"op-{Guid.NewGuid():N}";

        var copy = await _client.PostAsJsonAsync($"/api/assets/{sourceId}/copy", new
        {
            copyOperationId = copyOpId,
            copyPerformedBy = "tester",
            correlationId = "corr-lin-copy",
            targetAsset = new
            {
                siteId = "S-DB",
                assetGroup = "AG",
                processGroup = "PG",
                processGroupOtherText = (string?)null,
                assetName = "Target For Lineage",
                permitEuId = $"PERMIT-LIN-TGT-{Guid.NewGuid():N}",
                globalUniqueAssetId = $"GUID-{Guid.NewGuid():N}",
                assetDescription = (string?)null,
                stationaryFlag = (bool?)null,
                parentPseudoAssetId = (long?)null
            }
        });
        Assert.Equal(HttpStatusCode.Created, copy.StatusCode);

        // Query lineage by copyOperationId.
        var query = await _client.GetAsync($"/api/asset-copy-lineage?CopyOperationId={Uri.EscapeDataString(copyOpId)}&Limit=50");
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);

        var rows = await query.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.True(rows.GetArrayLength() >= 1);

        // Ensure at least one row matches our operation id.
        var found = false;
        foreach (var item in rows.EnumerateArray())
        {
            if (item.GetProperty("copyOperationId").GetString() == copyOpId)
            {
                found = true;
                Assert.Equal(sourceId, item.GetProperty("sourceAssetId").GetInt64());
                break;
            }
        }

        Assert.True(found, "Expected lineage query to include an item with the copyOperationId used for the copy.");
    }

    [Fact]
    public async Task Lineage_query_without_any_filters_returns_400()
    {
        // REQ: BRD §6.13 - Conservative query semantics (must provide at least one filter).
        var resp = await _client.GetAsync("/api/asset-copy-lineage");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
