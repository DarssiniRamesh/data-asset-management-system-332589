using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

/// <summary>
/// DB-backed tests for the BRD FR-03 Copy Asset workflow replication semantics:
/// - target is newly created (new IDs)
/// - source remains unchanged
/// - lineage row is created and points source->target
/// - deep replication of the Input Parameter subtree:
///   - ef_source_mapping
///   - throughput_equation + throughput_scalar
///   - data_input_value
/// </summary>
[RequiresDatabase]
public sealed class DbBackedCopyReplicationCorrectnessTests : IClassFixture<DbTestAppFactory>
{
    private readonly DbTestAppFactory _factory;

    public DbBackedCopyReplicationCorrectnessTests(DbTestAppFactory factory)
    {
        _factory = factory;
    }

    [DbFact]
    public async Task Copy_asset_replicates_input_subtree_with_new_ids_and_preserves_source()
    {
        using var client = _factory.CreateAuthedClient();

        // Create required master data so we can create "in use" input parameters and throughput equations.
        var uom = await CreateUomAsync(client, uomKey: "kg", displayLabel: "Kilogram");
        var rp = await CreateReportingProgramAsync(client, programKey: "RP1", displayLabel: "Reporting Program 1");
        var eqMaster = await CreateEquationMasterAsync(client, equationKey: "EQ1", versionLabel: "v1");

        // 1) Create a source asset.
        var sourceAsset = await CreateAssetAsync(
            client,
            siteId: "site-1",
            assetGroup: "group-1",
            processGroup: "process-1",
            assetName: "copy-src",
            permitEuId: "permit-src",
            globalUniqueAssetId: "guaid-src");

        // 2) Create a source input parameter (in-use) under source asset.
        var input = await CreateInputParameterAsync(
            client,
            sourceAsset.assetId,
            inputParameterName: "Input A",
            uomId: uom.uomId,
            reportingProgramId: rp.reportingProgramId,
            inputType: "Manual",
            dataEntryFrequency: "Monthly",
            fuelMapping: "Fuel-X",
            inUseFlag: true);

        // 3) Add EF source mapping under that input parameter.
        var ef = await CreateEfSourceMappingAsync(
            client,
            sourceAsset.assetId,
            input.inputParameterId,
            efSourceSetOrTable: "ef_table_1",
            equationSetup: "setup-1",
            scalarValues: "{\"k\":1}",
            // IMPORTANT: request includes reportingProgramId so it doesn't need to be derived.
            reportingProgramId: rp.reportingProgramId);

        // 4) Add throughput equation + scalar under the same input parameter.
        var te = await CreateThroughputEquationAsync(
            client,
            sourceAsset.assetId,
            input.inputParameterId,
            masterEquationId: eqMaster.equationMasterId,
            generatedEquation: "A*B",
            reportingYear: 2025);

        var ts = await CreateThroughputScalarAsync(
            client,
            sourceAsset.assetId,
            input.inputParameterId,
            te.throughputEquationId,
            scalarType: "Constant",
            scalarTable: "tbl",
            scalarId: "id1",
            scalarValue: "123.45",
            scalarBasis: "basis-1");

        // 5) Add a data input value under the input parameter.
        var div = await CreateDataInputValueAsync(
            client,
            sourceAsset.assetId,
            input.inputParameterId,
            inputParameterValue: "42",
            reportingYear: 2025,
            reportingPeriod: "Q1",
            calculatedThroughputOutput: "calc-1");

        // Snapshot counts on source before copy (source must remain unchanged).
        var sourceEfBefore = await ListEfSourceMappingsAsync(client, sourceAsset.assetId, input.inputParameterId);
        var sourceTeBefore = await ListThroughputEquationsAsync(client, sourceAsset.assetId, input.inputParameterId);
        var sourceTsBefore = await ListThroughputScalarsAsync(client, sourceAsset.assetId, input.inputParameterId, te.throughputEquationId);
        var sourceDivBefore = await ListDataInputValuesAsync(client, sourceAsset.assetId, input.inputParameterId);

        Assert.Single(sourceEfBefore);
        Assert.Single(sourceTeBefore);
        Assert.Single(sourceTsBefore);
        Assert.Single(sourceDivBefore);

        // 6) Copy the asset.
        var copyOperationId = $"op-{Guid.NewGuid():N}";
        var correlationId = $"corr-{Guid.NewGuid():N}";

        var copyResp = await client.PostAsJsonAsync($"/api/assets/{sourceAsset.assetId}/copy", new
        {
            copyOperationId,
            copyPerformedBy = "db-test-user",
            correlationId,
            targetAsset = new
            {
                siteId = "site-1",
                assetGroup = "group-1",
                processGroup = "process-1",
                processGroupOtherText = (string?)null,
                assetName = "copy-target",
                permitEuId = "permit-target",
                globalUniqueAssetId = "guaid-target",
                assetDescription = "target desc",
                stationaryFlag = (bool?)null,
                parentPseudoAssetId = (long?)null
            }
        });

        Assert.Equal(HttpStatusCode.Created, copyResp.StatusCode);

        var copy = await copyResp.Content.ReadFromJsonAsync<CopyAssetResponseDto>();
        Assert.NotNull(copy);

        // --- Create-semantics: target is newly created with a new ID, and source is unchanged.
        Assert.True(copy!.targetAsset.assetId > 0);
        Assert.NotEqual(sourceAsset.assetId, copy.targetAsset.assetId);

        // Lineage: must point from source->target and have the same copyOperationId.
        Assert.Equal(copyOperationId, copy.lineage.copyOperationId);
        Assert.Equal(sourceAsset.assetId, copy.lineage.sourceAssetId);
        Assert.Equal(copy.targetAsset.assetId, copy.lineage.targetAssetId);
        Assert.False(string.IsNullOrWhiteSpace(copy.replicationResultStatus));

        // 7) Find the copied input parameter under the target asset and assert it's a new ID.
        var targetInputs = await ListInputParametersAsync(client, copy.targetAsset.assetId);
        Assert.Single(targetInputs);

        var targetInput = targetInputs.Single();
        Assert.True(targetInput.inputParameterId > 0);
        Assert.NotEqual(input.inputParameterId, targetInput.inputParameterId);
        Assert.Equal("Input A", targetInput.inputParameterName);

        // 8) Assert EF source mapping replicated to target input with new ID, same content.
        var targetEf = await ListEfSourceMappingsAsync(client, copy.targetAsset.assetId, targetInput.inputParameterId);
        Assert.Single(targetEf);

        Assert.NotEqual(ef.efSourceMappingId, targetEf[0].efSourceMappingId);
        Assert.Equal(targetInput.inputParameterId, targetEf[0].inputParameterId);
        Assert.Equal("ef_table_1", targetEf[0].efSourceSetOrTable);
        Assert.Equal("setup-1", targetEf[0].equationSetup);
        Assert.Equal("{\"k\":1}", targetEf[0].scalarValues);
        Assert.Equal(rp.reportingProgramId, targetEf[0].reportingProgramId);

        // 9) Assert throughput equation replicated and scalars follow the new equation id.
        var targetTe = await ListThroughputEquationsAsync(client, copy.targetAsset.assetId, targetInput.inputParameterId);
        Assert.Single(targetTe);

        Assert.NotEqual(te.throughputEquationId, targetTe[0].throughputEquationId);
        Assert.Equal(eqMaster.equationMasterId, targetTe[0].masterEquationId);
        Assert.Equal("A*B", targetTe[0].generatedEquation);
        Assert.Equal(2025, targetTe[0].reportingYear);

        var targetTs = await ListThroughputScalarsAsync(
            client,
            copy.targetAsset.assetId,
            targetInput.inputParameterId,
            targetTe[0].throughputEquationId);

        Assert.Single(targetTs);
        Assert.NotEqual(ts.throughputScalarId, targetTs[0].throughputScalarId);
        Assert.Equal(targetTe[0].throughputEquationId, targetTs[0].throughputEquationId);
        Assert.Equal("Constant", targetTs[0].scalarType);
        Assert.Equal("tbl", targetTs[0].scalarTable);
        Assert.Equal("id1", targetTs[0].scalarId);
        Assert.Equal("123.45", targetTs[0].scalarValue);
        Assert.Equal("basis-1", targetTs[0].scalarBasis);

        // 10) Assert data input values replicated with new ID and same content.
        var targetDiv = await ListDataInputValuesAsync(client, copy.targetAsset.assetId, targetInput.inputParameterId);
        Assert.Single(targetDiv);

        Assert.NotEqual(div.dataInputValueId, targetDiv[0].dataInputValueId);
        Assert.Equal(targetInput.inputParameterId, targetDiv[0].inputParameterId);
        Assert.Equal("42", targetDiv[0].inputParameterValue);
        Assert.Equal(2025, targetDiv[0].reportingYear);
        Assert.Equal("Q1", targetDiv[0].reportingPeriod);
        Assert.Equal("calc-1", targetDiv[0].calculatedThroughputOutput);

        // 11) Re-check source subtree: it must remain unchanged by copy.
        var sourceEfAfter = await ListEfSourceMappingsAsync(client, sourceAsset.assetId, input.inputParameterId);
        var sourceTeAfter = await ListThroughputEquationsAsync(client, sourceAsset.assetId, input.inputParameterId);
        var sourceTsAfter = await ListThroughputScalarsAsync(client, sourceAsset.assetId, input.inputParameterId, te.throughputEquationId);
        var sourceDivAfter = await ListDataInputValuesAsync(client, sourceAsset.assetId, input.inputParameterId);

        Assert.Equal(sourceEfBefore.Length, sourceEfAfter.Length);
        Assert.Equal(sourceTeBefore.Length, sourceTeAfter.Length);
        Assert.Equal(sourceTsBefore.Length, sourceTsAfter.Length);
        Assert.Equal(sourceDivBefore.Length, sourceDivAfter.Length);

        Assert.Equal(sourceEfBefore[0].efSourceMappingId, sourceEfAfter[0].efSourceMappingId);
        Assert.Equal(sourceTeBefore[0].throughputEquationId, sourceTeAfter[0].throughputEquationId);
        Assert.Equal(sourceTsBefore[0].throughputScalarId, sourceTsAfter[0].throughputScalarId);
        Assert.Equal(sourceDivBefore[0].dataInputValueId, sourceDivAfter[0].dataInputValueId);
    }

    // -----------------------
    // Helpers (HTTP-level)
    // -----------------------

    private static async Task<AssetDto> CreateAssetAsync(
        HttpClient client,
        string siteId,
        string assetGroup,
        string processGroup,
        string assetName,
        string permitEuId,
        string globalUniqueAssetId)
    {
        var resp = await client.PostAsJsonAsync("/api/assets", new
        {
            siteId,
            assetGroup,
            processGroup,
            assetName,
            permitEuId,
            globalUniqueAssetId
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<AssetDto>();
        Assert.NotNull(dto);
        Assert.True(dto!.assetId > 0);

        return dto;
    }

    private static async Task<UomMasterDto> CreateUomAsync(HttpClient client, string uomKey, string displayLabel)
    {
        var resp = await client.PostAsJsonAsync("/api/masters/uoms", new
        {
            uomKey,
            displayLabel,
            isActive = true,
            createdBy = "db-test-user",
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<UomMasterDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private static async Task<ReportingProgramMasterDto> CreateReportingProgramAsync(HttpClient client, string programKey, string displayLabel)
    {
        var resp = await client.PostAsJsonAsync("/api/masters/reporting-programs", new
        {
            programKey,
            displayLabel,
            isActive = true,
            createdBy = "db-test-user",
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<ReportingProgramMasterDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private static async Task<EquationMasterDto> CreateEquationMasterAsync(HttpClient client, string equationKey, string versionLabel)
    {
        var resp = await client.PostAsJsonAsync("/api/masters/equations", new
        {
            equationKey,
            versionLabel,
            effectiveFrom = (string?)null,
            effectiveTo = (string?)null,
            createdBy = "db-test-user",
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<EquationMasterDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private static async Task<InputParameterDto> CreateInputParameterAsync(
        HttpClient client,
        long assetId,
        string inputParameterName,
        long uomId,
        long reportingProgramId,
        string inputType,
        string dataEntryFrequency,
        string? fuelMapping,
        bool inUseFlag)
    {
        var resp = await client.PostAsJsonAsync($"/api/assets/{assetId}/input-parameters", new
        {
            inputParameterName,
            uomId,
            reportingProgramId,
            inputType,
            dataEntryFrequency,
            fuelMapping,
            inUseFlag,
            createdBy = "db-test-user",
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<InputParameterDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private static async Task<EfSourceMappingDto> CreateEfSourceMappingAsync(
        HttpClient client,
        long assetId,
        long inputParameterId,
        string efSourceSetOrTable,
        string? equationSetup,
        string? scalarValues,
        long reportingProgramId)
    {
        var resp = await client.PostAsJsonAsync($"/api/assets/{assetId}/input-parameters/{inputParameterId}/ef-source-mappings", new
        {
            efSourceSetOrTable,
            equationSetup,
            scalarValues,
            reportingProgramId,
            createdBy = "db-test-user",
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<EfSourceMappingDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private static async Task<ThroughputEquationDto> CreateThroughputEquationAsync(
        HttpClient client,
        long assetId,
        long inputParameterId,
        long masterEquationId,
        string generatedEquation,
        int reportingYear)
    {
        var resp = await client.PostAsJsonAsync($"/api/assets/{assetId}/input-parameters/{inputParameterId}/throughput-equations", new
        {
            masterEquationId,
            generatedEquation,
            reportingYear,
            createdBy = "db-test-user",
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<ThroughputEquationDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private static async Task<ThroughputScalarDto> CreateThroughputScalarAsync(
        HttpClient client,
        long assetId,
        long inputParameterId,
        long throughputEquationId,
        string scalarType,
        string? scalarTable,
        string? scalarId,
        string? scalarValue,
        string? scalarBasis)
    {
        var resp = await client.PostAsJsonAsync(
            $"/api/assets/{assetId}/input-parameters/{inputParameterId}/throughput-equations/{throughputEquationId}/throughput-scalars",
            new
            {
                scalarType,
                scalarTable,
                scalarId,
                scalarValue,
                scalarBasis,
                createdBy = "db-test-user",
                correlationId = $"corr-{Guid.NewGuid():N}"
            });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<ThroughputScalarDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private static async Task<DataInputValueDto> CreateDataInputValueAsync(
        HttpClient client,
        long assetId,
        long inputParameterId,
        string? inputParameterValue,
        int reportingYear,
        string reportingPeriod,
        string? calculatedThroughputOutput)
    {
        var resp = await client.PostAsJsonAsync($"/api/assets/{assetId}/input-parameters/{inputParameterId}/data-input-values", new
        {
            inputParameterValue,
            reportingYear,
            reportingPeriod,
            calculatedThroughputOutput,
            createdBy = "db-test-user",
            correlationId = $"corr-{Guid.NewGuid():N}"
        });

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<DataInputValueDto>();
        Assert.NotNull(dto);
        return dto!;
    }

    private static async Task<InputParameterDto[]> ListInputParametersAsync(HttpClient client, long assetId)
    {
        var resp = await client.GetAsync($"/api/assets/{assetId}/input-parameters");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dtos = await resp.Content.ReadFromJsonAsync<InputParameterDto[]>();
        Assert.NotNull(dtos);
        return dtos!;
    }

    private static async Task<EfSourceMappingDto[]> ListEfSourceMappingsAsync(HttpClient client, long assetId, long inputParameterId)
    {
        var resp = await client.GetAsync($"/api/assets/{assetId}/input-parameters/{inputParameterId}/ef-source-mappings");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dtos = await resp.Content.ReadFromJsonAsync<EfSourceMappingDto[]>();
        Assert.NotNull(dtos);
        return dtos!;
    }

    private static async Task<ThroughputEquationDto[]> ListThroughputEquationsAsync(HttpClient client, long assetId, long inputParameterId)
    {
        var resp = await client.GetAsync($"/api/assets/{assetId}/input-parameters/{inputParameterId}/throughput-equations");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dtos = await resp.Content.ReadFromJsonAsync<ThroughputEquationDto[]>();
        Assert.NotNull(dtos);
        return dtos!;
    }

    private static async Task<ThroughputScalarDto[]> ListThroughputScalarsAsync(
        HttpClient client,
        long assetId,
        long inputParameterId,
        long throughputEquationId)
    {
        var resp = await client.GetAsync(
            $"/api/assets/{assetId}/input-parameters/{inputParameterId}/throughput-equations/{throughputEquationId}/throughput-scalars");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dtos = await resp.Content.ReadFromJsonAsync<ThroughputScalarDto[]>();
        Assert.NotNull(dtos);
        return dtos!;
    }

    private static async Task<DataInputValueDto[]> ListDataInputValuesAsync(HttpClient client, long assetId, long inputParameterId)
    {
        var resp = await client.GetAsync($"/api/assets/{assetId}/input-parameters/{inputParameterId}/data-input-values");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dtos = await resp.Content.ReadFromJsonAsync<DataInputValueDto[]>();
        Assert.NotNull(dtos);
        return dtos!;
    }

    // -----------------------
    // Minimal DTOs (tests)
    // -----------------------

    private sealed record AssetDto(long assetId);

    private sealed record CopyAssetResponseDto(
        AssetDto targetAsset,
        AssetCopyLineageDto lineage,
        string replicationResultStatus,
        string? replicationResultDetail);

    private sealed record AssetCopyLineageDto(
        string copyOperationId,
        long sourceAssetId,
        long targetAssetId);

    private sealed record UomMasterDto(long uomId);

    private sealed record ReportingProgramMasterDto(long reportingProgramId);

    private sealed record EquationMasterDto(long equationMasterId);

    private sealed record InputParameterDto(long inputParameterId, string inputParameterName);

    private sealed record EfSourceMappingDto(
        long efSourceMappingId,
        long inputParameterId,
        string efSourceSetOrTable,
        string? equationSetup,
        string? scalarValues,
        long reportingProgramId);

    private sealed record ThroughputEquationDto(
        long throughputEquationId,
        long masterEquationId,
        string generatedEquation,
        int reportingYear);

    private sealed record ThroughputScalarDto(
        long throughputScalarId,
        long throughputEquationId,
        string scalarType,
        string? scalarTable,
        string? scalarId,
        string? scalarValue,
        string? scalarBasis);

    private sealed record DataInputValueDto(
        long dataInputValueId,
        long inputParameterId,
        string? inputParameterValue,
        int reportingYear,
        string reportingPeriod,
        string? calculatedThroughputOutput);
}
