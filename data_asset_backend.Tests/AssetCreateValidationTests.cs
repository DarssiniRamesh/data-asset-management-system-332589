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
}
