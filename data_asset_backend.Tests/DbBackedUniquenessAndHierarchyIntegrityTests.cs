using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

/// <summary>
/// DB-backed regression tests for validations/integrity that require persistence:
/// - Permit EU ID uniqueness (create + update)
/// - Reporting attribute mapping uniqueness per asset
/// - Parent pseudo hierarchy integrity (no self-parent, no cycles)
/// </summary>
public sealed class DbBackedUniquenessAndHierarchyIntegrityTests : IClassFixture<DbTestAppFactory>
{
    private readonly DbTestAppFactory _factory;

    public DbBackedUniquenessAndHierarchyIntegrityTests(DbTestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_asset_with_duplicate_permit_eu_id_returns_409()
    {
        using var client = _factory.CreateAuthedClient();

        var permit = $"permit-dup-{Guid.NewGuid():N}";

        var first = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-dup",
            assetGroup = "group-dup",
            processGroup = "process-dup",
            assetName = "asset-1",
            permitEuId = permit,
            globalUniqueAssetId = $"guaid-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-dup",
            assetGroup = "group-dup",
            processGroup = "process-dup",
            assetName = "asset-2",
            permitEuId = permit, // duplicate
            globalUniqueAssetId = $"guaid-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Update_asset_to_duplicate_permit_eu_id_returns_409()
    {
        using var client = _factory.CreateAuthedClient();

        var permitA = $"permit-a-{Guid.NewGuid():N}";
        var permitB = $"permit-b-{Guid.NewGuid():N}";

        var aResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-u",
            assetGroup = "group-u",
            processGroup = "process-u",
            assetName = "asset-a",
            permitEuId = permitA,
            globalUniqueAssetId = $"guaid-{Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.Created, aResp.StatusCode);
        var a = await aResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(a);

        var bResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-u",
            assetGroup = "group-u",
            processGroup = "process-u",
            assetName = "asset-b",
            permitEuId = permitB,
            globalUniqueAssetId = $"guaid-{Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.Created, bResp.StatusCode);
        var b = await bResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(b);

        // Attempt to update B to use A's permit -> should conflict.
        var update = await client.PutAsJsonAsync($"/api/assets/{b!.assetId}", new
        {
            siteId = "site-u",
            assetGroup = "group-u",
            processGroup = "process-u",
            assetName = "asset-b-updated",
            permitEuId = permitA, // duplicate
            globalUniqueAssetId = b.globalUniqueAssetId,
            parentPseudoAssetId = (long?)null
        });

        Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
    }

    [Fact]
    public async Task Reporting_attribute_mapping_duplicate_combination_for_same_asset_returns_400()
    {
        using var client = _factory.CreateAuthedClient();

        var assetResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-ram",
            assetGroup = "group-ram",
            processGroup = "process-ram",
            assetName = "asset-ram",
            permitEuId = $"permit-ram-{Guid.NewGuid():N}",
            globalUniqueAssetId = $"guaid-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, assetResp.StatusCode);
        var asset = await assetResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(asset);

        // Current API schema uses attributeName/attributeValue/reportingProgramId and enforces uniqueness
        // for the (assetId, reportingProgramId, attributeName, attributeValue) combination.
        const long reportingProgramId = 1001;

        var first = await client.PostAsJsonAsync($"/api/assets/{asset!.assetId}/reporting-attribute-mappings", new
        {
            attributeName = "AttrA",
            attributeValue = "ValA",
            reportingProgramId,
            createdBy = "tester",
            correlationId = "corr-ram-1"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/assets/{asset.assetId}/reporting-attribute-mappings", new
        {
            attributeName = "AttrA",
            attributeValue = "ValA",
            reportingProgramId,
            createdBy = "tester",
            correlationId = "corr-ram-2"
        });

        // Child flow currently reports duplicate combinations as validation errors (400), not conflict.
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Hierarchy_integrity_rejects_self_parent_and_cycles_400()
    {
        using var client = _factory.CreateAuthedClient();

        var assetResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-h",
            assetGroup = "group-h",
            processGroup = "process-h",
            assetName = "asset-h",
            permitEuId = $"permit-h-{Guid.NewGuid():N}",
            globalUniqueAssetId = $"guaid-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, assetResp.StatusCode);
        var asset = await assetResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(asset);

        // Self-parent should be rejected.
        var selfParentUpdate = await client.PutAsJsonAsync($"/api/assets/{asset!.assetId}", new
        {
            siteId = "site-h",
            assetGroup = "group-h",
            processGroup = "process-h",
            assetName = "asset-h",
            permitEuId = asset.permitEuId,
            globalUniqueAssetId = asset.globalUniqueAssetId,
            parentPseudoAssetId = asset.assetId
        });

        Assert.Equal(HttpStatusCode.BadRequest, selfParentUpdate.StatusCode);

        // Create second asset and try to create a cycle A->B and B->A.
        var bResp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "site-h",
            assetGroup = "group-h",
            processGroup = "process-h",
            assetName = "asset-h-b",
            permitEuId = $"permit-hb-{Guid.NewGuid():N}",
            globalUniqueAssetId = $"guaid-{Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.Created, bResp.StatusCode);
        var b = await bResp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(b);

        // Update A to parent=B (allowed)
        var aToB = await client.PutAsJsonAsync($"/api/assets/{asset.assetId}", new
        {
            siteId = "site-h",
            assetGroup = "group-h",
            processGroup = "process-h",
            assetName = "asset-h",
            permitEuId = asset.permitEuId,
            globalUniqueAssetId = asset.globalUniqueAssetId,
            parentPseudoAssetId = b!.assetId
        });
        Assert.Equal(HttpStatusCode.OK, aToB.StatusCode);

        // Update B to parent=A (should be rejected as cycle)
        var bToA = await client.PutAsJsonAsync($"/api/assets/{b.assetId}", new
        {
            siteId = "site-h",
            assetGroup = "group-h",
            processGroup = "process-h",
            assetName = "asset-h-b",
            permitEuId = b.permitEuId,
            globalUniqueAssetId = b.globalUniqueAssetId,
            parentPseudoAssetId = asset.assetId
        });

        Assert.Equal(HttpStatusCode.BadRequest, bToA.StatusCode);
    }

    private sealed record AssetDto(
        long assetId,
        string assetName,
        string permitEuId,
        string globalUniqueAssetId);
}
