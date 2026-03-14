using DataAssetBackend.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Features.Masters;

/// <summary>
/// Repository for BRD master/reference tables (Flyway V2 schema).
/// </summary>
public sealed class MasterRepository
{
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly ILogger<MasterRepository> _logger;

    public MasterRepository(NpgsqlConnectionFactory connectionFactory, ILogger<MasterRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    // -------------------------
    // UOM MASTER
    // -------------------------

    public async Task<UomMasterDto> CreateUomAsync(CreateUomMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO uom_master (
                uom_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @uom_key,
                @display_label,
                @is_active,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                uom_id,
                uom_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "uom_key", request.UomKey);
        AddParam(cmd, "display_label", request.DisplayLabel);
        AddParam(cmd, "is_active", request.IsActive);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create UOM failed: INSERT returned no row.");
        }

        return ReadUom(reader);
    }

    public async Task<UomMasterDto?> GetUomByIdAsync(long uomId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                uom_id,
                uom_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM uom_master
            WHERE uom_id = @uom_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "uom_id", uomId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadUom(reader);
    }

    public async Task<UomMasterDto?> UpdateUomAsync(long uomId, UpdateUomMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE uom_master
            SET
                uom_key = @uom_key,
                display_label = @display_label,
                is_active = @is_active,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE uom_id = @uom_id
              AND is_deleted = FALSE
            RETURNING
                uom_id,
                uom_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "uom_id", uomId);
        AddParam(cmd, "uom_key", request.UomKey);
        AddParam(cmd, "display_label", request.DisplayLabel);
        AddParam(cmd, "is_active", request.IsActive);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadUom(reader);
    }

    public async Task<IReadOnlyList<UomMasterDto>> QueryUomsAsync(QueryMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var limit = ClampLimit(request.Limit, defaultLimit: 200, max: 1000);

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (request.ActiveOnly == true)
        {
            where.Add("is_active = TRUE");
        }

        var sql = $"""
            SELECT
                uom_id,
                uom_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM uom_master
            WHERE {string.Join(" AND ", where)}
            ORDER BY uom_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            AddParam(cmd, p.Name, p.Value);
        }

        AddParam(cmd, "limit", limit);

        var results = new List<UomMasterDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadUom(reader));
        }

        _logger.LogDebug("MasterRepository.QueryUomsAsync returned {Count} rows.", results.Count);
        return results;
    }

    // -------------------------
    // REPORTING PROGRAM MASTER
    // -------------------------

    public async Task<ReportingProgramMasterDto> CreateReportingProgramAsync(CreateReportingProgramMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO reporting_program_master (
                program_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @program_key,
                @display_label,
                @is_active,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                reporting_program_id,
                program_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "program_key", request.ProgramKey);
        AddParam(cmd, "display_label", request.DisplayLabel);
        AddParam(cmd, "is_active", request.IsActive);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create reporting program failed: INSERT returned no row.");
        }

        return ReadReportingProgram(reader);
    }

    public async Task<ReportingProgramMasterDto?> GetReportingProgramByIdAsync(long reportingProgramId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                reporting_program_id,
                program_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM reporting_program_master
            WHERE reporting_program_id = @reporting_program_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "reporting_program_id", reportingProgramId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadReportingProgram(reader);
    }

    public async Task<ReportingProgramMasterDto?> UpdateReportingProgramAsync(long reportingProgramId, UpdateReportingProgramMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE reporting_program_master
            SET
                program_key = @program_key,
                display_label = @display_label,
                is_active = @is_active,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE reporting_program_id = @reporting_program_id
              AND is_deleted = FALSE
            RETURNING
                reporting_program_id,
                program_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "reporting_program_id", reportingProgramId);
        AddParam(cmd, "program_key", request.ProgramKey);
        AddParam(cmd, "display_label", request.DisplayLabel);
        AddParam(cmd, "is_active", request.IsActive);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadReportingProgram(reader);
    }

    public async Task<IReadOnlyList<ReportingProgramMasterDto>> QueryReportingProgramsAsync(QueryMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var limit = ClampLimit(request.Limit, defaultLimit: 200, max: 1000);

        var where = new List<string> { "is_deleted = FALSE" };
        if (request.ActiveOnly == true)
        {
            where.Add("is_active = TRUE");
        }

        var sql = $"""
            SELECT
                reporting_program_id,
                program_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM reporting_program_master
            WHERE {string.Join(" AND ", where)}
            ORDER BY reporting_program_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "limit", limit);

        var results = new List<ReportingProgramMasterDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadReportingProgram(reader));
        }

        _logger.LogDebug("MasterRepository.QueryReportingProgramsAsync returned {Count} rows.", results.Count);
        return results;
    }

    // -------------------------
    // CONTROL DEVICE MASTER
    // -------------------------

    public async Task<ControlDeviceMasterDto> CreateControlDeviceAsync(CreateControlDeviceMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO control_device_master (
                site_id,
                device_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @site_id,
                @device_key,
                @display_label,
                @is_active,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                control_device_id,
                site_id,
                device_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_id", request.SiteId);
        AddParam(cmd, "device_key", request.DeviceKey);
        AddParam(cmd, "display_label", request.DisplayLabel);
        AddParam(cmd, "is_active", request.IsActive);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create control device failed: INSERT returned no row.");
        }

        return ReadControlDevice(reader);
    }

    public async Task<ControlDeviceMasterDto?> GetControlDeviceByIdAsync(long controlDeviceId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                control_device_id,
                site_id,
                device_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM control_device_master
            WHERE control_device_id = @control_device_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "control_device_id", controlDeviceId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadControlDevice(reader);
    }

    public async Task<ControlDeviceMasterDto?> UpdateControlDeviceAsync(long controlDeviceId, UpdateControlDeviceMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE control_device_master
            SET
                site_id = @site_id,
                device_key = @device_key,
                display_label = @display_label,
                is_active = @is_active,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE control_device_id = @control_device_id
              AND is_deleted = FALSE
            RETURNING
                control_device_id,
                site_id,
                device_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "control_device_id", controlDeviceId);
        AddParam(cmd, "site_id", request.SiteId);
        AddParam(cmd, "device_key", request.DeviceKey);
        AddParam(cmd, "display_label", request.DisplayLabel);
        AddParam(cmd, "is_active", request.IsActive);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadControlDevice(reader);
    }

    public async Task<IReadOnlyList<ControlDeviceMasterDto>> QueryControlDevicesAsync(string? siteId, QueryMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var limit = ClampLimit(request.Limit, defaultLimit: 200, max: 1000);

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (request.ActiveOnly == true)
        {
            where.Add("is_active = TRUE");
        }

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            where.Add("site_id = @site_id");
            parameters.Add(("site_id", siteId.Trim()));
        }

        var sql = $"""
            SELECT
                control_device_id,
                site_id,
                device_key,
                display_label,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM control_device_master
            WHERE {string.Join(" AND ", where)}
            ORDER BY control_device_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            AddParam(cmd, p.Name, p.Value);
        }
        AddParam(cmd, "limit", limit);

        var results = new List<ControlDeviceMasterDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadControlDevice(reader));
        }

        _logger.LogDebug("MasterRepository.QueryControlDevicesAsync returned {Count} rows.", results.Count);
        return results;
    }

    // -------------------------
    // EQUATION MASTER
    // -------------------------

    public async Task<EquationMasterDto> CreateEquationAsync(CreateEquationMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO equation_master (
                equation_key,
                version_label,
                effective_from,
                effective_to,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @equation_key,
                @version_label,
                @effective_from,
                @effective_to,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                equation_master_id,
                equation_key,
                version_label,
                effective_from,
                effective_to,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "equation_key", request.EquationKey);
        AddParam(cmd, "version_label", request.VersionLabel);
        AddParam(cmd, "effective_from", request.EffectiveFrom.HasValue ? request.EffectiveFrom.Value : DBNull.Value);
        AddParam(cmd, "effective_to", request.EffectiveTo.HasValue ? request.EffectiveTo.Value : DBNull.Value);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create equation failed: INSERT returned no row.");
        }

        return ReadEquation(reader);
    }

    public async Task<EquationMasterDto?> GetEquationByIdAsync(long equationMasterId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                equation_master_id,
                equation_key,
                version_label,
                effective_from,
                effective_to,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM equation_master
            WHERE equation_master_id = @equation_master_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "equation_master_id", equationMasterId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadEquation(reader);
    }

    public async Task<EquationMasterDto?> UpdateEquationAsync(long equationMasterId, UpdateEquationMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE equation_master
            SET
                equation_key = @equation_key,
                version_label = @version_label,
                effective_from = @effective_from,
                effective_to = @effective_to,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE equation_master_id = @equation_master_id
              AND is_deleted = FALSE
            RETURNING
                equation_master_id,
                equation_key,
                version_label,
                effective_from,
                effective_to,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "equation_master_id", equationMasterId);
        AddParam(cmd, "equation_key", request.EquationKey);
        AddParam(cmd, "version_label", request.VersionLabel);
        AddParam(cmd, "effective_from", request.EffectiveFrom.HasValue ? request.EffectiveFrom.Value : DBNull.Value);
        AddParam(cmd, "effective_to", request.EffectiveTo.HasValue ? request.EffectiveTo.Value : DBNull.Value);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadEquation(reader);
    }

    public async Task<IReadOnlyList<EquationMasterDto>> QueryEquationsAsync(QueryMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var limit = ClampLimit(request.Limit, defaultLimit: 200, max: 1000);

        const string sql = """
            SELECT
                equation_master_id,
                equation_key,
                version_label,
                effective_from,
                effective_to,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM equation_master
            WHERE is_deleted = FALSE
            ORDER BY equation_master_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "limit", limit);

        var results = new List<EquationMasterDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadEquation(reader));
        }

        _logger.LogDebug("MasterRepository.QueryEquationsAsync returned {Count} rows.", results.Count);
        return results;
    }

    // -------------------------
    // STATUS CODE MASTER
    // -------------------------

    public async Task<StatusCodeMasterDto> CreateStatusCodeAsync(CreateStatusCodeMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO status_code_master (
                status_code,
                business_meaning,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @status_code,
                @business_meaning,
                @is_active,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                status_code_id,
                status_code,
                business_meaning,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "status_code", request.StatusCode);
        AddParam(cmd, "business_meaning", request.BusinessMeaning);
        AddParam(cmd, "is_active", request.IsActive);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create status code failed: INSERT returned no row.");
        }

        return ReadStatusCode(reader);
    }

    public async Task<StatusCodeMasterDto?> GetStatusCodeByIdAsync(long statusCodeId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                status_code_id,
                status_code,
                business_meaning,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM status_code_master
            WHERE status_code_id = @status_code_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "status_code_id", statusCodeId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadStatusCode(reader);
    }

    public async Task<StatusCodeMasterDto?> UpdateStatusCodeAsync(long statusCodeId, UpdateStatusCodeMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE status_code_master
            SET
                status_code = @status_code,
                business_meaning = @business_meaning,
                is_active = @is_active,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE status_code_id = @status_code_id
              AND is_deleted = FALSE
            RETURNING
                status_code_id,
                status_code,
                business_meaning,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "status_code_id", statusCodeId);
        AddParam(cmd, "status_code", request.StatusCode);
        AddParam(cmd, "business_meaning", request.BusinessMeaning);
        AddParam(cmd, "is_active", request.IsActive);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadStatusCode(reader);
    }

    public async Task<IReadOnlyList<StatusCodeMasterDto>> QueryStatusCodesAsync(QueryMasterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var limit = ClampLimit(request.Limit, defaultLimit: 200, max: 1000);

        var where = new List<string> { "is_deleted = FALSE" };
        if (request.ActiveOnly == true)
        {
            where.Add("is_active = TRUE");
        }

        var sql = $"""
            SELECT
                status_code_id,
                status_code,
                business_meaning,
                is_active,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM status_code_master
            WHERE {string.Join(" AND ", where)}
            ORDER BY status_code_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "limit", limit);

        var results = new List<StatusCodeMasterDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStatusCode(reader));
        }

        _logger.LogDebug("MasterRepository.QueryStatusCodesAsync returned {Count} rows.", results.Count);
        return results;
    }

    // -------------------------
    // Helpers
    // -------------------------

    private static void AddParam(NpgsqlCommand cmd, string name, object value)
    {
        var p = cmd.Parameters.AddWithValue(name, value);
        _ = p;
    }

    private static int ClampLimit(int? limit, int defaultLimit, int max)
    {
        var v = limit ?? defaultLimit;
        if (v <= 0) v = defaultLimit;
        if (v > max) v = max;
        return v;
    }

    private static UomMasterDto ReadUom(NpgsqlDataReader r)
    {
        return new UomMasterDto(
            UomId: r.GetInt64(r.GetOrdinal("uom_id")),
            UomKey: r.GetString(r.GetOrdinal("uom_key")),
            DisplayLabel: r.GetString(r.GetOrdinal("display_label")),
            IsActive: r.GetBoolean(r.GetOrdinal("is_active")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ReportingProgramMasterDto ReadReportingProgram(NpgsqlDataReader r)
    {
        return new ReportingProgramMasterDto(
            ReportingProgramId: r.GetInt64(r.GetOrdinal("reporting_program_id")),
            ProgramKey: r.GetString(r.GetOrdinal("program_key")),
            DisplayLabel: r.GetString(r.GetOrdinal("display_label")),
            IsActive: r.GetBoolean(r.GetOrdinal("is_active")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ControlDeviceMasterDto ReadControlDevice(NpgsqlDataReader r)
    {
        return new ControlDeviceMasterDto(
            ControlDeviceId: r.GetInt64(r.GetOrdinal("control_device_id")),
            SiteId: r.GetString(r.GetOrdinal("site_id")),
            DeviceKey: r.GetString(r.GetOrdinal("device_key")),
            DisplayLabel: r.GetString(r.GetOrdinal("display_label")),
            IsActive: r.GetBoolean(r.GetOrdinal("is_active")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static EquationMasterDto ReadEquation(NpgsqlDataReader r)
    {
        DateOnly? ReadDate(string col)
        {
            var idx = r.GetOrdinal(col);
            if (r.IsDBNull(idx)) return null;
            return DateOnly.FromDateTime(r.GetDateTime(idx));
        }

        return new EquationMasterDto(
            EquationMasterId: r.GetInt64(r.GetOrdinal("equation_master_id")),
            EquationKey: r.GetString(r.GetOrdinal("equation_key")),
            VersionLabel: r.GetString(r.GetOrdinal("version_label")),
            EffectiveFrom: ReadDate("effective_from"),
            EffectiveTo: ReadDate("effective_to"),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static StatusCodeMasterDto ReadStatusCode(NpgsqlDataReader r)
    {
        return new StatusCodeMasterDto(
            StatusCodeId: r.GetInt64(r.GetOrdinal("status_code_id")),
            StatusCode: r.GetString(r.GetOrdinal("status_code")),
            BusinessMeaning: r.GetString(r.GetOrdinal("business_meaning")),
            IsActive: r.GetBoolean(r.GetOrdinal("is_active")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }
}
