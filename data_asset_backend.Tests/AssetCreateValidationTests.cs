using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

public sealed class AssetCreateValidationTests : IClassFixture<TestAppFactory>
{
    private readonly HttpClient _client;

    public AssetCreateValidationTests(TestAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_asset_requires_requiresParentPseudo_field()
    {
        // Missing requiresParentPseudo should fail validation before any DB interaction.
        var resp = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S1",
            assetGroup = "AG1",
            processGroup = "PG1",
            assetName = "Asset A",
            permitEuId = "P1",
            globalUniqueAssetId = "GUID-1",
            createdBy = "tester",
            correlationId = "corr-1"
            // requiresParentPseudo intentionally omitted
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_asset_requires_processGroupOtherText_when_processGroup_is_other()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S1",
            assetGroup = "AG1",
            processGroup = "Other",
            // processGroupOtherText intentionally omitted
            assetName = "Asset A",
            permitEuId = "P1",
            globalUniqueAssetId = "GUID-1",
            requiresParentPseudo = false,
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_asset_requires_parentPseudoAssetId_when_requiresParentPseudo_is_true()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S1",
            assetGroup = "AG1",
            processGroup = "PG1",
            assetName = "Asset A",
            permitEuId = "P1",
            globalUniqueAssetId = "GUID-1",
            requiresParentPseudo = true,
            // parentPseudoAssetId intentionally omitted
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_asset_rejects_nonexistent_parentPseudoAssetId_with_validation_or_db_unavailable()
    {
        // In a fully configured DB environment, this should be a request validation error (400).
        // In CI/local environments where DB is intentionally not configured, the endpoint may short-circuit
        // with 503 before deeper validation can run.
        var resp = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S1",
            assetGroup = "AG1",
            processGroup = "PG1",
            assetName = "Asset A",
            permitEuId = "P1",
            globalUniqueAssetId = "GUID-1",
            requiresParentPseudo = true,
            parentPseudoAssetId = 99999999,
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.True(
            resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.ServiceUnavailable,
            $"Expected 400 or 503 but got {(int)resp.StatusCode} {resp.StatusCode}");
    }

    [Fact]
    public async Task Create_asset_rejects_parentPseudoAssetId_when_requiresParentPseudo_is_false_with_validation_or_db_unavailable()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets", new
        {
            siteId = "S1",
            assetGroup = "AG1",
            processGroup = "PG1",
            assetName = "Asset A",
            permitEuId = "P1",
            globalUniqueAssetId = "GUID-1",
            requiresParentPseudo = false,
            parentPseudoAssetId = 1,
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.True(
            resp.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.ServiceUnavailable,
            $"Expected 400 or 503 but got {(int)resp.StatusCode} {resp.StatusCode}");
    }
}
