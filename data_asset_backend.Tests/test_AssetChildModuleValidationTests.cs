using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

public sealed class AssetChildModuleValidationTests : IClassFixture<TestAppFactory>
{
    private readonly HttpClient _client;

    public AssetChildModuleValidationTests(TestAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_asset_property_requires_required_fields_400()
    {
        // Missing required fields should fail model validation before any DB access.
        var resp = await _client.PostAsJsonAsync("/api/assets/1/properties", new
        {
            propertyName = "",
            propertyValue = "",
            // fromDate missing
            notes = "",
            createdBy = "",
            correlationId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_control_device_mapping_requires_required_fields_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets/1/control-device-mappings", new
        {
            controlDeviceId = 0, // invalid (required long, must be non-zero in practice)
            controlDeviceNameOrRef = "",
            // inUseFlag missing
            createdBy = "",
            correlationId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_reporting_attribute_mapping_requires_required_fields_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets/1/reporting-attribute-mappings", new
        {
            attributeName = "",
            attributeValue = "",
            reportingProgramId = 0,
            createdBy = "",
            correlationId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_input_parameter_requires_required_fields_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets/1/input-parameters", new
        {
            inputParameterName = "",
            inputType = "",
            dataEntryFrequency = "",
            // inUseFlag missing
            createdBy = "",
            correlationId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_input_parameter_when_in_use_requires_uom_and_reporting_program_400()
    {
        // This validation is implemented via IValidatableObject on CreateInputParameterRequest.
        // It should run before any DB access.
        var resp = await _client.PostAsJsonAsync("/api/assets/1/input-parameters", new
        {
            inputParameterName = "IP-1",
            inputType = "TypeA",
            dataEntryFrequency = "Monthly",
            inUseFlag = true,
            // uomId missing
            // reportingProgramId missing
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_additional_asset_id_requires_required_fields_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets/1/additional-ids", new
        {
            idType = "",
            idValue = "",
            createdBy = "",
            correlationId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_status_log_requires_required_fields_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets/1/status-logs", new
        {
            operatingStatus = "",
            // statusFromDate missing
            createdBy = "",
            correlationId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_throughput_equation_requires_required_fields_and_supports_aliases_400_when_missing()
    {
        // Validate that missing canonical fields (and aliases) fails at model validation layer.
        // CreateThroughputEquationRequest normalizes aliases, but still requires usable values.
        var resp = await _client.PostAsJsonAsync("/api/assets/1/input-parameters/1/throughput-equations", new
        {
            // masterEquationId missing (and equationMasterId missing)
            // generatedEquation missing (and equationText missing)
            // reportingYear missing (and year missing)
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_throughput_scalar_accepts_alias_scalarName_but_requires_one_of_scalarType_or_scalarName()
    {
        // If both scalarType and scalarName are missing/blank, validation fails before DB access.
        var resp = await _client.PostAsJsonAsync("/api/assets/1/input-parameters/1/throughput-equations/1/throughput-scalars", new
        {
            scalarType = "",
            scalarName = "",
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_data_input_value_requires_reporting_year_and_period_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/assets/1/input-parameters/1/data-input-values", new
        {
            // reportingYear missing
            reportingPeriod = "",
            createdBy = "",
            correlationId = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task List_child_routes_are_mapped_and_not_404()
    {
        // Route mapping regression tests (do not assume DB availability).
        var endpoints = new[]
        {
            "/api/assets/1/status-logs",
            "/api/assets/1/additional-ids",
            "/api/assets/1/properties",
            "/api/assets/1/control-device-mappings",
            "/api/assets/1/reporting-attribute-mappings",
            "/api/assets/1/input-parameters",
            "/api/assets/1/input-parameters/1/parent-input-mappings",
            "/api/assets/1/input-parameters/1/ef-source-mappings",
            "/api/assets/1/input-parameters/1/throughput-equations",
            "/api/assets/1/input-parameters/1/data-input-values"
        };

        foreach (var url in endpoints)
        {
            var resp = await _client.GetAsync(url);
            Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }
}
