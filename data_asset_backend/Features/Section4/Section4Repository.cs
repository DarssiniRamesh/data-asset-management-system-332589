using DataAssetBackend.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Features.Section4;

/// <summary>
/// Repository for BRD §4 “Required Pages / Modules”.
/// Uses the minimal persistence schema from Flyway V3, without assuming undocumented fields.
/// </summary>
public sealed class Section4Repository
{
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly ILogger<Section4Repository> _logger;

    public Section4Repository(NpgsqlConnectionFactory connectionFactory, ILogger<Section4Repository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    // -------------------------
    // Site Profile
    // -------------------------

    public async Task<IReadOnlyList<SiteProfileDto>> ListSiteProfilesAsync(string? siteId, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            where.Add("site_id = @site_id");
            parameters.Add(("site_id", siteId.Trim()));
        }

        const string order = "ORDER BY site_profile_id DESC";

        var sql = $"""
            SELECT
                site_profile_id,
                site_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM site_profile
            WHERE {string.Join(" AND ", where)}
            {order}
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) AddParam(cmd, p.Name, p.Value);
        AddParam(cmd, "limit", limit);

        var results = new List<SiteProfileDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadSiteProfile(reader));
        }

        return results;
    }

    public async Task<SiteProfileDto> CreateSiteProfileAsync(CreateSiteProfileRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO site_profile (
                site_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            ) VALUES (
                @site_id,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                site_profile_id,
                site_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "created_by", request.CreatedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Create site profile failed: INSERT returned no row.");

        return ReadSiteProfile(reader);
    }

    public async Task<SiteProfileDto?> UpdateSiteProfileAsync(long siteProfileId, UpdateSiteProfileRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE site_profile
            SET
                site_id = @site_id,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE site_profile_id = @site_profile_id
              AND is_deleted = FALSE
            RETURNING
                site_profile_id,
                site_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_profile_id", siteProfileId);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "modified_by", request.ModifiedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadSiteProfile(reader);
    }

    public async Task<bool> DeleteSiteProfileAsync(long siteProfileId, string modifiedBy, string correlationId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE site_profile
            SET
                is_deleted = TRUE,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE site_profile_id = @site_profile_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_profile_id", siteProfileId);
        AddParam(cmd, "modified_by", modifiedBy.Trim());
        AddParam(cmd, "correlation_id", correlationId.Trim());

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // -------------------------
    // WWTS Process Stream
    // -------------------------

    public async Task<IReadOnlyList<WwtsProcessStreamDto>> ListWwtsProcessStreamsAsync(string? siteId, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            where.Add("site_id = @site_id");
            parameters.Add(("site_id", siteId.Trim()));
        }

        var sql = $"""
            SELECT
                wwts_process_stream_id,
                site_id,
                stream_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM wwts_process_stream
            WHERE {string.Join(" AND ", where)}
            ORDER BY wwts_process_stream_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) AddParam(cmd, p.Name, p.Value);
        AddParam(cmd, "limit", limit);

        var results = new List<WwtsProcessStreamDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadWwtsProcessStream(reader));
        }

        return results;
    }

    public async Task<WwtsProcessStreamDto> CreateWwtsProcessStreamAsync(CreateWwtsProcessStreamRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO wwts_process_stream (
                site_id,
                stream_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            ) VALUES (
                @site_id,
                @stream_name,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                wwts_process_stream_id,
                site_id,
                stream_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "stream_name", request.StreamName.Trim());
        AddParam(cmd, "created_by", request.CreatedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Create WWTS process stream failed: INSERT returned no row.");

        return ReadWwtsProcessStream(reader);
    }

    public async Task<WwtsProcessStreamDto?> UpdateWwtsProcessStreamAsync(long wwtsProcessStreamId, UpdateWwtsProcessStreamRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE wwts_process_stream
            SET
                site_id = @site_id,
                stream_name = @stream_name,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE wwts_process_stream_id = @wwts_process_stream_id
              AND is_deleted = FALSE
            RETURNING
                wwts_process_stream_id,
                site_id,
                stream_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "wwts_process_stream_id", wwtsProcessStreamId);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "stream_name", request.StreamName.Trim());
        AddParam(cmd, "modified_by", request.ModifiedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadWwtsProcessStream(reader);
    }

    public async Task<bool> DeleteWwtsProcessStreamAsync(long wwtsProcessStreamId, string modifiedBy, string correlationId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE wwts_process_stream
            SET
                is_deleted = TRUE,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE wwts_process_stream_id = @wwts_process_stream_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "wwts_process_stream_id", wwtsProcessStreamId);
        AddParam(cmd, "modified_by", modifiedBy.Trim());
        AddParam(cmd, "correlation_id", correlationId.Trim());

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // -------------------------
    // Chemical Raw Material
    // -------------------------

    public async Task<IReadOnlyList<ChemicalRawMaterialDto>> ListChemicalRawMaterialsAsync(string? siteId, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            where.Add("site_id = @site_id");
            parameters.Add(("site_id", siteId.Trim()));
        }

        var sql = $"""
            SELECT
                chemical_raw_material_id,
                site_id,
                chemical_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM chemical_raw_material
            WHERE {string.Join(" AND ", where)}
            ORDER BY chemical_raw_material_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) AddParam(cmd, p.Name, p.Value);
        AddParam(cmd, "limit", limit);

        var results = new List<ChemicalRawMaterialDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadChemicalRawMaterial(reader));
        }

        return results;
    }

    public async Task<ChemicalRawMaterialDto> CreateChemicalRawMaterialAsync(CreateChemicalRawMaterialRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO chemical_raw_material (
                site_id,
                chemical_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            ) VALUES (
                @site_id,
                @chemical_name,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                chemical_raw_material_id,
                site_id,
                chemical_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "chemical_name", request.ChemicalName.Trim());
        AddParam(cmd, "created_by", request.CreatedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Create chemical raw material failed: INSERT returned no row.");

        return ReadChemicalRawMaterial(reader);
    }

    public async Task<ChemicalRawMaterialDto?> UpdateChemicalRawMaterialAsync(long chemicalRawMaterialId, UpdateChemicalRawMaterialRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE chemical_raw_material
            SET
                site_id = @site_id,
                chemical_name = @chemical_name,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE chemical_raw_material_id = @chemical_raw_material_id
              AND is_deleted = FALSE
            RETURNING
                chemical_raw_material_id,
                site_id,
                chemical_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "chemical_raw_material_id", chemicalRawMaterialId);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "chemical_name", request.ChemicalName.Trim());
        AddParam(cmd, "modified_by", request.ModifiedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadChemicalRawMaterial(reader);
    }

    public async Task<bool> DeleteChemicalRawMaterialAsync(long chemicalRawMaterialId, string modifiedBy, string correlationId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE chemical_raw_material
            SET
                is_deleted = TRUE,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE chemical_raw_material_id = @chemical_raw_material_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "chemical_raw_material_id", chemicalRawMaterialId);
        AddParam(cmd, "modified_by", modifiedBy.Trim());
        AddParam(cmd, "correlation_id", correlationId.Trim());

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // -------------------------
    // Chemical SDS
    // -------------------------

    public async Task<IReadOnlyList<ChemicalSdsDto>> ListChemicalSdsAsync(string? siteId, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            where.Add("site_id = @site_id");
            parameters.Add(("site_id", siteId.Trim()));
        }

        var sql = $"""
            SELECT
                chemical_sds_id,
                site_id,
                chemical_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM chemical_sds
            WHERE {string.Join(" AND ", where)}
            ORDER BY chemical_sds_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) AddParam(cmd, p.Name, p.Value);
        AddParam(cmd, "limit", limit);

        var results = new List<ChemicalSdsDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadChemicalSds(reader));
        }

        return results;
    }

    public async Task<ChemicalSdsDto> CreateChemicalSdsAsync(CreateChemicalSdsRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO chemical_sds (
                site_id,
                chemical_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            ) VALUES (
                @site_id,
                @chemical_name,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                chemical_sds_id,
                site_id,
                chemical_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "chemical_name", request.ChemicalName.Trim());
        AddParam(cmd, "created_by", request.CreatedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Create chemical SDS failed: INSERT returned no row.");

        return ReadChemicalSds(reader);
    }

    public async Task<ChemicalSdsDto?> UpdateChemicalSdsAsync(long chemicalSdsId, UpdateChemicalSdsRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE chemical_sds
            SET
                site_id = @site_id,
                chemical_name = @chemical_name,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE chemical_sds_id = @chemical_sds_id
              AND is_deleted = FALSE
            RETURNING
                chemical_sds_id,
                site_id,
                chemical_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "chemical_sds_id", chemicalSdsId);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "chemical_name", request.ChemicalName.Trim());
        AddParam(cmd, "modified_by", request.ModifiedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadChemicalSds(reader);
    }

    public async Task<bool> DeleteChemicalSdsAsync(long chemicalSdsId, string modifiedBy, string correlationId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE chemical_sds
            SET
                is_deleted = TRUE,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE chemical_sds_id = @chemical_sds_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "chemical_sds_id", chemicalSdsId);
        AddParam(cmd, "modified_by", modifiedBy.Trim());
        AddParam(cmd, "correlation_id", correlationId.Trim());

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // -------------------------
    // Lab Data Configuration
    // -------------------------

    public async Task<IReadOnlyList<LabDataConfigurationDto>> ListLabDataConfigurationsAsync(string? siteId, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            where.Add("site_id = @site_id");
            parameters.Add(("site_id", siteId.Trim()));
        }

        var sql = $"""
            SELECT
                lab_data_configuration_id,
                site_id,
                configuration_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM lab_data_configuration
            WHERE {string.Join(" AND ", where)}
            ORDER BY lab_data_configuration_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) AddParam(cmd, p.Name, p.Value);
        AddParam(cmd, "limit", limit);

        var results = new List<LabDataConfigurationDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadLabDataConfiguration(reader));
        }

        return results;
    }

    public async Task<LabDataConfigurationDto> CreateLabDataConfigurationAsync(CreateLabDataConfigurationRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO lab_data_configuration (
                site_id,
                configuration_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            ) VALUES (
                @site_id,
                @configuration_name,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                lab_data_configuration_id,
                site_id,
                configuration_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "configuration_name", request.ConfigurationName.Trim());
        AddParam(cmd, "created_by", request.CreatedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Create lab data configuration failed: INSERT returned no row.");

        return ReadLabDataConfiguration(reader);
    }

    public async Task<LabDataConfigurationDto?> UpdateLabDataConfigurationAsync(long labDataConfigurationId, UpdateLabDataConfigurationRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE lab_data_configuration
            SET
                site_id = @site_id,
                configuration_name = @configuration_name,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE lab_data_configuration_id = @lab_data_configuration_id
              AND is_deleted = FALSE
            RETURNING
                lab_data_configuration_id,
                site_id,
                configuration_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "lab_data_configuration_id", labDataConfigurationId);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "configuration_name", request.ConfigurationName.Trim());
        AddParam(cmd, "modified_by", request.ModifiedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadLabDataConfiguration(reader);
    }

    public async Task<bool> DeleteLabDataConfigurationAsync(long labDataConfigurationId, string modifiedBy, string correlationId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE lab_data_configuration
            SET
                is_deleted = TRUE,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE lab_data_configuration_id = @lab_data_configuration_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "lab_data_configuration_id", labDataConfigurationId);
        AddParam(cmd, "modified_by", modifiedBy.Trim());
        AddParam(cmd, "correlation_id", correlationId.Trim());

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // -------------------------
    // Water Process Configuration
    // -------------------------

    public async Task<IReadOnlyList<WaterProcessConfigurationDto>> ListWaterProcessConfigurationsAsync(string? siteId, int limit, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(siteId))
        {
            where.Add("site_id = @site_id");
            parameters.Add(("site_id", siteId.Trim()));
        }

        var sql = $"""
            SELECT
                water_process_configuration_id,
                site_id,
                configuration_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM water_process_configuration
            WHERE {string.Join(" AND ", where)}
            ORDER BY water_process_configuration_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) AddParam(cmd, p.Name, p.Value);
        AddParam(cmd, "limit", limit);

        var results = new List<WaterProcessConfigurationDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadWaterProcessConfiguration(reader));
        }

        return results;
    }

    public async Task<WaterProcessConfigurationDto> CreateWaterProcessConfigurationAsync(CreateWaterProcessConfigurationRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO water_process_configuration (
                site_id,
                configuration_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            ) VALUES (
                @site_id,
                @configuration_name,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                water_process_configuration_id,
                site_id,
                configuration_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "configuration_name", request.ConfigurationName.Trim());
        AddParam(cmd, "created_by", request.CreatedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Create water process configuration failed: INSERT returned no row.");

        return ReadWaterProcessConfiguration(reader);
    }

    public async Task<WaterProcessConfigurationDto?> UpdateWaterProcessConfigurationAsync(long waterProcessConfigurationId, UpdateWaterProcessConfigurationRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE water_process_configuration
            SET
                site_id = @site_id,
                configuration_name = @configuration_name,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE water_process_configuration_id = @water_process_configuration_id
              AND is_deleted = FALSE
            RETURNING
                water_process_configuration_id,
                site_id,
                configuration_name,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "water_process_configuration_id", waterProcessConfigurationId);
        AddParam(cmd, "site_id", request.SiteId.Trim());
        AddParam(cmd, "configuration_name", request.ConfigurationName.Trim());
        AddParam(cmd, "modified_by", request.ModifiedBy.Trim());
        AddParam(cmd, "correlation_id", request.CorrelationId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadWaterProcessConfiguration(reader);
    }

    public async Task<bool> DeleteWaterProcessConfigurationAsync(long waterProcessConfigurationId, string modifiedBy, string correlationId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE water_process_configuration
            SET
                is_deleted = TRUE,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE water_process_configuration_id = @water_process_configuration_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "water_process_configuration_id", waterProcessConfigurationId);
        AddParam(cmd, "modified_by", modifiedBy.Trim());
        AddParam(cmd, "correlation_id", correlationId.Trim());

        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    // -------------------------
    // Helpers
    // -------------------------

    private static int ClampLimit(int? limit, int defaultLimit, int max)
    {
        var v = limit ?? defaultLimit;
        if (v <= 0) v = defaultLimit;
        if (v > max) v = max;
        return v;
    }

    private static void AddParam(NpgsqlCommand cmd, string name, object value)
    {
        _ = cmd.Parameters.AddWithValue(name, value);
    }

    private static SiteProfileDto ReadSiteProfile(NpgsqlDataReader r)
    {
        return new SiteProfileDto(
            SiteProfileId: r.GetInt64(r.GetOrdinal("site_profile_id")),
            SiteId: r.GetString(r.GetOrdinal("site_id")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static WwtsProcessStreamDto ReadWwtsProcessStream(NpgsqlDataReader r)
    {
        return new WwtsProcessStreamDto(
            WwtsProcessStreamId: r.GetInt64(r.GetOrdinal("wwts_process_stream_id")),
            SiteId: r.GetString(r.GetOrdinal("site_id")),
            StreamName: r.GetString(r.GetOrdinal("stream_name")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ChemicalRawMaterialDto ReadChemicalRawMaterial(NpgsqlDataReader r)
    {
        return new ChemicalRawMaterialDto(
            ChemicalRawMaterialId: r.GetInt64(r.GetOrdinal("chemical_raw_material_id")),
            SiteId: r.GetString(r.GetOrdinal("site_id")),
            ChemicalName: r.GetString(r.GetOrdinal("chemical_name")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ChemicalSdsDto ReadChemicalSds(NpgsqlDataReader r)
    {
        return new ChemicalSdsDto(
            ChemicalSdsId: r.GetInt64(r.GetOrdinal("chemical_sds_id")),
            SiteId: r.GetString(r.GetOrdinal("site_id")),
            ChemicalName: r.GetString(r.GetOrdinal("chemical_name")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static LabDataConfigurationDto ReadLabDataConfiguration(NpgsqlDataReader r)
    {
        return new LabDataConfigurationDto(
            LabDataConfigurationId: r.GetInt64(r.GetOrdinal("lab_data_configuration_id")),
            SiteId: r.GetString(r.GetOrdinal("site_id")),
            ConfigurationName: r.GetString(r.GetOrdinal("configuration_name")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static WaterProcessConfigurationDto ReadWaterProcessConfiguration(NpgsqlDataReader r)
    {
        return new WaterProcessConfigurationDto(
            WaterProcessConfigurationId: r.GetInt64(r.GetOrdinal("water_process_configuration_id")),
            SiteId: r.GetString(r.GetOrdinal("site_id")),
            ConfigurationName: r.GetString(r.GetOrdinal("configuration_name")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    // Public helper for consistent list limits across endpoints.
    public static int NormalizeLimit(int? limit) => ClampLimit(limit, defaultLimit: 200, max: 1000);
}
