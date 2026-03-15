using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DataAssetBackend.Tests;

/// <summary>
/// Additional validation regression tests derived from the BRD Asset Configuration feature.
/// These tests focus on validations that should be enforceable without a database connection
/// (i.e., model validation / IValidatableObject rules).
/// </summary>
public sealed class BrdValidationGapsTests : IClassFixture<TestAppFactory>
{
    private readonly HttpClient _client;

    public BrdValidationGapsTests(TestAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Create_status_log_rejects_end_date_before_start_date_400()
    {
        // BRD 6.2: Status To Date must be greater than Status From Date (if present).
        var resp = await _client.PostAsJsonAsync("/api/assets/1/status-logs", new
        {
            operatingStatus = "Active",
            statusFromDate = "2026-01-10",
            statusToDate = "2026-01-01",
            createdBy = "tester",
            correlationId = "corr-1"
        });

        // Must be validation error (should not require DB).
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_data_input_value_rejects_blank_period_400()
    {
        // BRD 6.10: Reporting Year/Period required.
        var resp = await _client.PostAsJsonAsync("/api/assets/1/input-parameters/1/data-input-values", new
        {
            reportingYear = 2026,
            reportingPeriod = " ",
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_parent_input_mapping_requires_parent_input_id_400()
    {
        // BRD 6.6: Child Input ID + Parent Input ID required.
        var resp = await _client.PostAsJsonAsync("/api/assets/1/input-parameters/1/parent-input-mappings", new
        {
            // parentInputParameterId missing
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_ef_source_mapping_requires_reporting_program_400()
    {
        // BRD 6.8: Reporting Program Mapping required.
        var resp = await _client.PostAsJsonAsync("/api/assets/1/input-parameters/1/ef-source-mappings", new
        {
            efSourceSetOrTable = "TABLE_A",
            // reportingProgramId missing
            createdBy = "tester",
            correlationId = "corr-1"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
