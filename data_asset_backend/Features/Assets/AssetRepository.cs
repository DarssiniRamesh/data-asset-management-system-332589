using DataAssetBackend.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Repository for CRUD/query operations on Flyway V2 <c>asset</c>.
/// </summary>
public sealed class AssetRepository
{
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly ILogger<AssetRepository> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AssetRepository" />.
    /// </summary>
    public AssetRepository(NpgsqlConnectionFactory connectionFactory, ILogger<AssetRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <summary>
    /// Creates an asset row and returns the created DTO.
    /// </summary>
    public async Task<AssetDto> CreateAsync(CreateAssetRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Note: modified_* initially mirrors created_* for a new row.
        const string sql = """
            INSERT INTO asset (
                site_id,
                asset_group,
                process_group,
                process_group_other_text,
                asset_name,
                permit_eu_id,
                global_unique_asset_id,
                asset_description,
                stationary_flag,
                parent_pseudo_asset_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @site_id,
                @asset_group,
                @process_group,
                @process_group_other_text,
                @asset_name,
                @permit_eu_id,
                @global_unique_asset_id,
                @asset_description,
                @stationary_flag,
                @parent_pseudo_asset_id,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                asset_id,
                site_id,
                asset_group,
                process_group,
                process_group_other_text,
                asset_name,
                permit_eu_id,
                global_unique_asset_id,
                asset_description,
                stationary_flag,
                parent_pseudo_asset_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);

        AddParam(cmd, "site_id", request.SiteId);
        AddParam(cmd, "asset_group", request.AssetGroup);
        AddParam(cmd, "process_group", request.ProcessGroup);
        AddParam(cmd, "process_group_other_text", (object?)request.ProcessGroupOtherText ?? DBNull.Value);
        AddParam(cmd, "asset_name", request.AssetName);
        AddParam(cmd, "permit_eu_id", request.PermitEuId);
        AddParam(cmd, "global_unique_asset_id", request.GlobalUniqueAssetId);
        AddParam(cmd, "asset_description", (object?)request.AssetDescription ?? DBNull.Value);
        AddParam(cmd, "stationary_flag", request.StationaryFlag.HasValue ? request.StationaryFlag.Value : DBNull.Value);
        AddParam(cmd, "parent_pseudo_asset_id", request.ParentPseudoAssetId.HasValue ? request.ParentPseudoAssetId.Value : DBNull.Value);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create asset failed: INSERT returned no row.");
        }

        return ReadAsset(reader);
    }

    /// <summary>
    /// Gets an asset by ID (excluding soft-deleted rows).
    /// </summary>
    public async Task<AssetDto?> GetByIdAsync(long assetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                asset_id,
                site_id,
                asset_group,
                process_group,
                process_group_other_text,
                asset_name,
                permit_eu_id,
                global_unique_asset_id,
                asset_description,
                stationary_flag,
                parent_pseudo_asset_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM asset
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAsset(reader);
    }

    /// <summary>
    /// Updates an asset and returns the updated DTO (excluding soft-deleted rows).
    /// </summary>
    public async Task<AssetDto?> UpdateAsync(long assetId, UpdateAssetRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // We also fetch the immutable global_unique_asset_id in one round-trip to enforce BRD "immutable once created".
        const string sql = """
            UPDATE asset
            SET
                site_id = @site_id,
                asset_group = @asset_group,
                process_group = @process_group,
                process_group_other_text = @process_group_other_text,
                asset_name = @asset_name,
                permit_eu_id = @permit_eu_id,
                asset_description = @asset_description,
                stationary_flag = @stationary_flag,
                parent_pseudo_asset_id = @parent_pseudo_asset_id,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            RETURNING
                asset_id,
                site_id,
                asset_group,
                process_group,
                process_group_other_text,
                asset_name,
                permit_eu_id,
                global_unique_asset_id,
                asset_description,
                stationary_flag,
                parent_pseudo_asset_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);

        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "site_id", request.SiteId);
        AddParam(cmd, "asset_group", request.AssetGroup);
        AddParam(cmd, "process_group", request.ProcessGroup);
        AddParam(cmd, "process_group_other_text", (object?)request.ProcessGroupOtherText ?? DBNull.Value);
        AddParam(cmd, "asset_name", request.AssetName);
        AddParam(cmd, "permit_eu_id", request.PermitEuId);
        AddParam(cmd, "asset_description", (object?)request.AssetDescription ?? DBNull.Value);
        AddParam(cmd, "stationary_flag", request.StationaryFlag.HasValue ? request.StationaryFlag.Value : DBNull.Value);
        AddParam(cmd, "parent_pseudo_asset_id", request.ParentPseudoAssetId.HasValue ? request.ParentPseudoAssetId.Value : DBNull.Value);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAsset(reader);
    }

    /// <summary>
    /// Queries assets with optional filters (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<AssetDto>> QueryAsync(QueryAssetsRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        var limit = request.Limit ?? 100;
        if (limit <= 0)
        {
            limit = 100;
        }

        if (limit > 500)
        {
            limit = 500;
        }

        // Build a parameterized WHERE clause (no string interpolation of values).
        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(request.SiteId))
        {
            where.Add("site_id = @site_id");
            parameters.Add(("site_id", request.SiteId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.AssetGroup))
        {
            where.Add("asset_group = @asset_group");
            parameters.Add(("asset_group", request.AssetGroup.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.ProcessGroup))
        {
            where.Add("process_group = @process_group");
            parameters.Add(("process_group", request.ProcessGroup.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.PermitEuId))
        {
            where.Add("permit_eu_id = @permit_eu_id");
            parameters.Add(("permit_eu_id", request.PermitEuId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.GlobalUniqueAssetId))
        {
            where.Add("global_unique_asset_id = @global_unique_asset_id");
            parameters.Add(("global_unique_asset_id", request.GlobalUniqueAssetId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(request.AssetNameContains))
        {
            // ILIKE is Postgres-specific and suited for simple contains search.
            where.Add("asset_name ILIKE @asset_name_contains");
            parameters.Add(("asset_name_contains", $"%{request.AssetNameContains.Trim()}%"));
        }

        var whereSql = string.Join(" AND ", where);

        // Stable ordering for deterministic behavior.
        var sql = $"""
            SELECT
                asset_id,
                site_id,
                asset_group,
                process_group,
                process_group_other_text,
                asset_name,
                permit_eu_id,
                global_unique_asset_id,
                asset_description,
                stationary_flag,
                parent_pseudo_asset_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM asset
            WHERE {whereSql}
            ORDER BY asset_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            AddParam(cmd, p.Name, p.Value);
        }

        AddParam(cmd, "limit", limit);

        var results = new List<AssetDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAsset(reader));
        }

        _logger.LogDebug("AssetRepository.QueryAsync returned {Count} rows.", results.Count);
        return results;
    }

    private static void AddParam(NpgsqlCommand cmd, string name, object value)
    {
        var p = cmd.Parameters.AddWithValue(name, value);
        _ = p;
    }

    private static AssetDto ReadAsset(NpgsqlDataReader r)
    {
        return new AssetDto(
            AssetId: r.GetInt64(r.GetOrdinal("asset_id")),
            SiteId: r.GetString(r.GetOrdinal("site_id")),
            AssetGroup: r.GetString(r.GetOrdinal("asset_group")),
            ProcessGroup: r.GetString(r.GetOrdinal("process_group")),
            ProcessGroupOtherText: r.IsDBNull(r.GetOrdinal("process_group_other_text")) ? null : r.GetString(r.GetOrdinal("process_group_other_text")),
            AssetName: r.GetString(r.GetOrdinal("asset_name")),
            PermitEuId: r.GetString(r.GetOrdinal("permit_eu_id")),
            GlobalUniqueAssetId: r.GetString(r.GetOrdinal("global_unique_asset_id")),
            AssetDescription: r.IsDBNull(r.GetOrdinal("asset_description")) ? null : r.GetString(r.GetOrdinal("asset_description")),
            StationaryFlag: r.IsDBNull(r.GetOrdinal("stationary_flag")) ? null : r.GetBoolean(r.GetOrdinal("stationary_flag")),
            ParentPseudoAssetId: r.IsDBNull(r.GetOrdinal("parent_pseudo_asset_id")) ? null : r.GetInt64(r.GetOrdinal("parent_pseudo_asset_id")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }
}
