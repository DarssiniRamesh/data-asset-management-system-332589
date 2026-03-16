using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DataAssetBackend.Tests;

/// <summary>
/// DB-backed integration tests that verify BRD-aligned lifecycle behavior:
/// - FR-01 Add Asset (happy path persistence)
/// - FR-02 Edit Asset (happy path persistence)
/// - FR-04 Delete Asset (dependency-safe delete semantics)
/// - DQ-Uniqueness for Permit EU ID
/// - DQ-Traceability for correlation id propagation (middleware + stored field is not asserted here)
/// </summary>
public sealed class DbBackedAssetLifecycleTests : IClassFixture<DbTestAppFactory>
{
    private readonly HttpClient _client;

    public DbBackedAssetLifecycleTests(DbTestAppFactory factory)
    {
        _client = factory.CreateAuthedClient();
    }

    [Fact]
    public async Task Create_asset_persists_and_returns_201_with_id()
    {
        // REQ: FR-01 - Add asset persists successfully when required fields are present.
        var resp = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Asset Lifecycle 1",
            permitEuId = "PERMIT-UNIQ-1",
            globalUniqueAssetId = $"GUID-{Guid.NewGuid():N}",
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-create-1"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("assetId", out var idProp));
        Assert.True(idProp.GetInt64() > 0);

        // REQ: DQ-Traceability - Correlation header is always present on responses.
        Assert.True(resp.Headers.TryGetValues("X-Correlation-Id", out var headerValues));
        Assert.Contains("test-corr-fixed", headerValues);
    }

    [Fact]
    public async Task Create_asset_rejects_duplicate_permit_eu_id_with_409()
    {
        // REQ: DQ-Uniqueness - Permit EU ID must be unique across non-deleted assets (backend enforcement).
        var permit = $"PERMIT-DUP-{Guid.NewGuid():N}";

        var req = new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Asset A",
            permitEuId = permit,
            globalUniqueAssetId = $"GUID-{Guid.NewGuid():N}",
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-dup-1"
        };

        var r1 = await _client.PostAsJsonAsync("/api/assets", req);
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        var r2 = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Asset B",
            permitEuId = permit, // duplicate
            globalUniqueAssetId = $"GUID-{Guid.NewGuid():N}",
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-dup-2"
        });

        Assert.Equal(HttpStatusCode.Conflict, r2.StatusCode);
    }

    [Fact]
    public async Task Update_asset_persists_changed_fields()
    {
        // REQ: FR-02 - Edit asset persists updates.
        var create = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Asset To Update",
            permitEuId = $"PERMIT-UPD-{Guid.NewGuid():N}",
            globalUniqueAssetId = $"GUID-{Guid.NewGuid():N}",
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-upd-create"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var createdJson = await create.Content.ReadFromJsonAsync<JsonElement>();
        var assetId = createdJson.GetProperty("assetId").GetInt64();

        var update = await _client.PutAsJsonAsync($"/api/assets/{assetId}", new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Asset Updated Name",
            permitEuId = createdJson.GetProperty("permitEuId").GetString(),
            requiresParentPseudo = false,
            modifiedBy = "tester2",
            correlationId = "corr-upd-1"
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var updatedJson = await update.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Asset Updated Name", updatedJson.GetProperty("assetName").GetString());

        // Verify persisted via GET.
        var get = await _client.GetAsync($"/api/assets/{assetId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var getJson = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Asset Updated Name", getJson.GetProperty("assetName").GetString());
    }

    [Fact]
    public async Task Delete_asset_soft_deletes_and_subsequent_get_is_404()
    {
        // REQ: FR-04 - Delete asset removes it from read operations (soft delete).
        var create = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Asset To Delete",
            permitEuId = $"PERMIT-DEL-{Guid.NewGuid():N}",
            globalUniqueAssetId = $"GUID-{Guid.NewGuid():N}",
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-del-create"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var createdJson = await create.Content.ReadFromJsonAsync<JsonElement>();
        var assetId = createdJson.GetProperty("assetId").GetInt64();

        var del = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/assets/{assetId}")
        {
            Content = JsonContent.Create(new
            {
                modifiedBy = "tester",
                correlationId = "corr-del-1"
            })
        });
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await _client.GetAsync($"/api/assets/{assetId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }
}
