using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

/// <summary>
/// DB-backed integration tests for BRD dependency-safe delete behavior (Gap 6).
/// These tests validate that deleting an asset with child rows does not leave active children behind.
/// </summary>
public sealed class DbBackedDependencySafeDeleteTests : IClassFixture<DbTestAppFactory>
{
    private readonly HttpClient _client;

    public DbBackedDependencySafeDeleteTests(DbTestAppFactory factory)
    {
        _client = factory.CreateAuthedClient();
    }

    [Fact]
    public async Task Delete_asset_with_properties_soft_deletes_children_and_asset()
    {
        // REQ: FR-04 - Dependency-safe delete should handle child records.
        var create = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S-DB",
            assetGroup = "AG",
            processGroup = "PG",
            assetName = "Asset With Property",
            permitEuId = $"PERMIT-CHILD-{Guid.NewGuid():N}",
            globalUniqueAssetId = $"GUID-{Guid.NewGuid():N}",
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-child-create"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var assetId = created.GetProperty("assetId").GetInt64();

        var prop = await _client.PostAsJsonAsync($"/api/assets/{assetId}/properties", new
        {
            propertyName = "p1",
            propertyValue = "v1",
            fromDate = "2026-01-01",
            notes = "n",
            createdBy = "tester",
            correlationId = "corr-child-prop"
        });

        Assert.Equal(HttpStatusCode.Created, prop.StatusCode);

        var listBefore = await _client.GetAsync($"/api/assets/{assetId}/properties");
        Assert.Equal(HttpStatusCode.OK, listBefore.StatusCode);

        var beforeJson = await listBefore.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, beforeJson.ValueKind);
        Assert.True(beforeJson.GetArrayLength() >= 1);

        var del = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/assets/{assetId}")
        {
            Content = JsonContent.Create(new
            {
                modifiedBy = "tester",
                correlationId = "corr-child-del"
            })
        });
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // Asset should be gone.
        var get = await _client.GetAsync($"/api/assets/{assetId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        // Children should not be returned (either 404 because asset is gone, or 200 with empty list depending on API contract).
        var listAfter = await _client.GetAsync($"/api/assets/{assetId}/properties");
        Assert.True(
            listAfter.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.OK,
            $"Expected 404 or 200 but got {(int)listAfter.StatusCode} {listAfter.StatusCode}");

        if (listAfter.StatusCode == HttpStatusCode.OK)
        {
            var afterJson = await listAfter.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Array, afterJson.ValueKind);
            Assert.Equal(0, afterJson.GetArrayLength());
        }
    }
}
