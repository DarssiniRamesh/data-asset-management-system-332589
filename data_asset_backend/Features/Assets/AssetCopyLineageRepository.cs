using DataAssetBackend.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Repository for CRUD/query operations on Flyway V2 table <c>asset_copy_lineage</c> (BRD §6.13).
/// </summary>
public sealed class AssetCopyLineageRepository
{
    private readonly NpgsqlConnectionFactory _connectionFactory;
    private readonly ILogger<AssetCopyLineageRepository> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="AssetCopyLineageRepository" />.
    /// </summary>
    public AssetCopyLineageRepository(NpgsqlConnectionFactory connectionFactory, ILogger<AssetCopyLineageRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an asset copy lineage record and returns the created DTO.
    /// </summary>
    public async Task<AssetCopyLineageDto> CreateAsync(CreateAssetCopyLineageRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO asset_copy_lineage (
                copy_operation_id,
                source_asset_id,
                target_asset_id,
                copy_timestamp_utc,
                copy_performed_by,
                replication_result_status,
                replication_result_detail,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @copy_operation_id,
                @source_asset_id,
                @target_asset_id,
                @copy_timestamp_utc,
                @copy_performed_by,
                @replication_result_status,
                @replication_result_detail,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                asset_copy_lineage_id,
                copy_operation_id,
                source_asset_id,
                target_asset_id,
                copy_timestamp_utc,
                copy_performed_by,
                replication_result_status,
                replication_result_detail,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "copy_operation_id", request.CopyOperationId);
        AddParam(cmd, "source_asset_id", request.SourceAssetId);
        AddParam(cmd, "target_asset_id", request.TargetAssetId);
        AddParam(cmd, "copy_timestamp_utc", request.CopyTimestampUtc);
        AddParam(cmd, "copy_performed_by", request.CopyPerformedBy);
        AddParam(cmd, "replication_result_status", request.ReplicationResultStatus);
        AddParam(cmd, "replication_result_detail", (object?)request.ReplicationResultDetail ?? DBNull.Value);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create asset_copy_lineage failed: INSERT returned no row.");
        }

        return ReadAssetCopyLineage(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Queries lineage records with optional filters (excluding soft-deleted rows).
    /// Multiple filters are ANDed.
    /// </summary>
    public async Task<IReadOnlyList<AssetCopyLineageDto>> QueryAsync(QueryAssetCopyLineageRequest request, CancellationToken cancellationToken)
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

        var where = new List<string> { "is_deleted = FALSE" };
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(request.CopyOperationId))
        {
            where.Add("copy_operation_id = @copy_operation_id");
            parameters.Add(("copy_operation_id", request.CopyOperationId.Trim()));
        }

        if (request.SourceAssetId.HasValue)
        {
            where.Add("source_asset_id = @source_asset_id");
            parameters.Add(("source_asset_id", request.SourceAssetId.Value));
        }

        if (request.TargetAssetId.HasValue)
        {
            where.Add("target_asset_id = @target_asset_id");
            parameters.Add(("target_asset_id", request.TargetAssetId.Value));
        }

        var whereSql = string.Join(" AND ", where);

        // Deterministic order: newest first.
        var sql = $"""
            SELECT
                asset_copy_lineage_id,
                copy_operation_id,
                source_asset_id,
                target_asset_id,
                copy_timestamp_utc,
                copy_performed_by,
                replication_result_status,
                replication_result_detail,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM asset_copy_lineage
            WHERE {whereSql}
            ORDER BY copy_timestamp_utc DESC, asset_copy_lineage_id DESC
            LIMIT @limit;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters)
        {
            AddParam(cmd, p.Name, p.Value);
        }

        AddParam(cmd, "limit", limit);

        var results = new List<AssetCopyLineageDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAssetCopyLineage(reader));
        }

        _logger.LogDebug("AssetCopyLineageRepository.QueryAsync returned {Count} rows.", results.Count);
        return results;
    }

    private static void AddParam(NpgsqlCommand cmd, string name, object value)
    {
        var p = cmd.Parameters.AddWithValue(name, value);
        _ = p;
    }

    private static AssetCopyLineageDto ReadAssetCopyLineage(NpgsqlDataReader r)
    {
        return new AssetCopyLineageDto(
            AssetCopyLineageId: r.GetInt64(r.GetOrdinal("asset_copy_lineage_id")),
            CopyOperationId: r.GetString(r.GetOrdinal("copy_operation_id")),
            SourceAssetId: r.GetInt64(r.GetOrdinal("source_asset_id")),
            TargetAssetId: r.GetInt64(r.GetOrdinal("target_asset_id")),
            CopyTimestampUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("copy_timestamp_utc")),
            CopyPerformedBy: r.GetString(r.GetOrdinal("copy_performed_by")),
            ReplicationResultStatus: r.GetString(r.GetOrdinal("replication_result_status")),
            ReplicationResultDetail: r.IsDBNull(r.GetOrdinal("replication_result_detail"))
                ? null
                : r.GetString(r.GetOrdinal("replication_result_detail")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }
}
