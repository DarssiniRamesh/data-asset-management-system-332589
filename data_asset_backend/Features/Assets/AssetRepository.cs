using DataAssetBackend.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Repository for CRUD/query operations on Flyway V2 tables related to <c>asset</c>.
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
    /// Thrown when a requested entity is not found during an existence/belongs-to validation.
    /// </summary>
    public sealed class EntityNotFoundException : Exception
    {
        /// <summary>
        /// Initializes a new instance of <see cref="EntityNotFoundException"/>.
        /// </summary>
        public EntityNotFoundException(string entityName, long id)
            : base($"{entityName} not found (id={id}).")
        {
            EntityName = entityName;
            Id = id;
        }

        /// <summary>
        /// Entity type name (table-ish name used in error message).
        /// </summary>
        public string EntityName { get; }

        /// <summary>
        /// The missing entity ID.
        /// </summary>
        public long Id { get; }
    }

    // ---------------------------------------------------------------------
    // Asset (table: asset)
    // ---------------------------------------------------------------------

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

        // Nullable columns: always bind with explicit types to avoid Postgres “could not determine data type of parameter $N”.
        AddParam(
            cmd,
            "process_group_other_text",
            (object?)request.ProcessGroupOtherText ?? DBNull.Value,
            NpgsqlDbType.Text);

        AddParam(cmd, "asset_name", request.AssetName);
        AddParam(cmd, "permit_eu_id", request.PermitEuId);
        AddParam(cmd, "global_unique_asset_id", request.GlobalUniqueAssetId);

        AddParam(
            cmd,
            "asset_description",
            (object?)request.AssetDescription ?? DBNull.Value,
            NpgsqlDbType.Text);

        AddParam(
            cmd,
            "stationary_flag",
            request.StationaryFlag.HasValue ? request.StationaryFlag.Value : DBNull.Value,
            NpgsqlDbType.Boolean);

        AddParam(
            cmd,
            "parent_pseudo_asset_id",
            request.ParentPseudoAssetId.HasValue ? request.ParentPseudoAssetId.Value : DBNull.Value,
            NpgsqlDbType.Bigint);

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

        // Nullable columns: always bind with explicit types to avoid Postgres “could not determine data type of parameter $N”.
        AddParam(
            cmd,
            "process_group_other_text",
            (object?)request.ProcessGroupOtherText ?? DBNull.Value,
            NpgsqlDbType.Text);

        AddParam(cmd, "asset_name", request.AssetName);
        AddParam(cmd, "permit_eu_id", request.PermitEuId);

        AddParam(
            cmd,
            "asset_description",
            (object?)request.AssetDescription ?? DBNull.Value,
            NpgsqlDbType.Text);

        AddParam(
            cmd,
            "stationary_flag",
            request.StationaryFlag.HasValue ? request.StationaryFlag.Value : DBNull.Value,
            NpgsqlDbType.Boolean);

        AddParam(
            cmd,
            "parent_pseudo_asset_id",
            request.ParentPseudoAssetId.HasValue ? request.ParentPseudoAssetId.Value : DBNull.Value,
            NpgsqlDbType.Bigint);

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

    // ---------------------------------------------------------------------
    // Evidence-based existence / belongs-to checks (NO ASSUMPTIONS)
    // ---------------------------------------------------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Checks whether a non-deleted asset already exists with the given Permit EU ID.
    /// </summary>
    /// <remarks>
    /// BRD evidence: BRD §6.1 marks Permit EU ID as required and mentions uniqueness, but does not evidence
    /// the uniqueness scope (e.g., per site vs global).
    ///
    /// Therefore this method implements a conservative, evidence-safe check:
    /// - It checks for duplicates across all non-deleted assets.
    /// - It allows excluding a specific asset id (for updates).
    ///
    /// If a future BRD revision clarifies the scope, this method is the single place to adjust the predicate.
    /// </remarks>
    public async Task<bool> PermitEuIdExistsAsync(
        string permitEuId,
        long? excludeAssetId,
        CancellationToken cancellationToken)
    {
        // Defensive normalization: API models already require non-empty, but keep this safe for internal callers.
        var normalized = (permitEuId ?? string.Empty).Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT 1
            FROM asset
            WHERE permit_eu_id = @permit_eu_id
              AND is_deleted = FALSE
              AND (@exclude_asset_id IS NULL OR asset_id <> @exclude_asset_id)
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "permit_eu_id", normalized);
        AddParam(
            cmd,
            "exclude_asset_id",
            excludeAssetId.HasValue ? excludeAssetId.Value : DBNull.Value,
            NpgsqlDbType.Bigint);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is not null;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Checks whether a non-deleted asset exists.
    /// </summary>
    public async Task<bool> AssetExistsAsync(long assetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT 1
            FROM asset
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Soft-deletes an asset and BRD-evidenced dependent rows using the existing <c>is_deleted</c> flag.
    /// </summary>
    /// <remarks>
    /// Transactional behavior:
    /// - All updates occur in a single DB transaction.
    /// - If the asset does not exist (or is already deleted), returns false and performs no updates.
    /// </remarks>
    public async Task<bool> SoftDeleteAssetGraphAsync(
        long assetId,
        string modifiedBy,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        // 1) Soft-delete the asset row itself first; if nothing updated, treat as not found.
        const string deleteAssetSql = """
            UPDATE asset
            SET
                is_deleted = TRUE,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE;
            """;

        var assetUpdated = await ExecuteNonQueryAsync(
            conn,
            deleteAssetSql,
            new[]
            {
                ("asset_id", (object)assetId),
                ("modified_by", (object)modifiedBy),
                ("correlation_id", (object)correlationId)
            },
            cancellationToken);

        if (assetUpdated == 0)
        {
            await tx.RollbackAsync(cancellationToken);
            return false;
        }

        // 2) Soft-delete BRD-evidenced child tables that reference asset_id directly.
        await ExecuteNonQueryAsync(
            conn,
            """
            UPDATE asset_status_log
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE asset_id = @asset_id AND is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        await ExecuteNonQueryAsync(
            conn,
            """
            UPDATE additional_asset_id
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE asset_id = @asset_id AND is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        await ExecuteNonQueryAsync(
            conn,
            """
            UPDATE asset_property
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE asset_id = @asset_id AND is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        await ExecuteNonQueryAsync(
            conn,
            """
            UPDATE control_device_mapping
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE asset_id = @asset_id AND is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        await ExecuteNonQueryAsync(
            conn,
            """
            UPDATE reporting_attribute_mapping
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE asset_id = @asset_id AND is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        // 3) Soft-delete input_parameter and its nested dependents.
        // We delete deepest children first (throughput_scalar), then parents, to remain safe even if future rules add constraints.
        await ExecuteNonQueryAsync(
            conn,
            """
            WITH inputs AS (
                SELECT input_parameter_id
                FROM input_parameter
                WHERE asset_id = @asset_id AND is_deleted = FALSE
            ),
            equations AS (
                SELECT throughput_equation_id
                FROM throughput_equation
                WHERE input_parameter_id IN (SELECT input_parameter_id FROM inputs)
                  AND is_deleted = FALSE
            )
            UPDATE throughput_scalar ts
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE ts.throughput_equation_id IN (SELECT throughput_equation_id FROM equations)
              AND ts.is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        await ExecuteNonQueryAsync(
            conn,
            """
            WITH inputs AS (
                SELECT input_parameter_id
                FROM input_parameter
                WHERE asset_id = @asset_id AND is_deleted = FALSE
            )
            UPDATE throughput_equation te
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE te.input_parameter_id IN (SELECT input_parameter_id FROM inputs)
              AND te.is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        await ExecuteNonQueryAsync(
            conn,
            """
            WITH inputs AS (
                SELECT input_parameter_id
                FROM input_parameter
                WHERE asset_id = @asset_id AND is_deleted = FALSE
            )
            UPDATE ef_source_mapping esm
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE esm.input_parameter_id IN (SELECT input_parameter_id FROM inputs)
              AND esm.is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        await ExecuteNonQueryAsync(
            conn,
            """
            WITH inputs AS (
                SELECT input_parameter_id
                FROM input_parameter
                WHERE asset_id = @asset_id AND is_deleted = FALSE
            )
            UPDATE data_input_value div
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE div.input_parameter_id IN (SELECT input_parameter_id FROM inputs)
              AND div.is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        // parent_input_mapping references input_parameter (child + parent). We soft-delete any mapping where either side is under the asset.
        await ExecuteNonQueryAsync(
            conn,
            """
            WITH inputs AS (
                SELECT input_parameter_id
                FROM input_parameter
                WHERE asset_id = @asset_id AND is_deleted = FALSE
            )
            UPDATE parent_input_mapping pim
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE pim.is_deleted = FALSE
              AND (
                pim.child_input_parameter_id IN (SELECT input_parameter_id FROM inputs)
                OR pim.parent_input_parameter_id IN (SELECT input_parameter_id FROM inputs)
              );
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        // Finally, soft-delete the inputs themselves.
        await ExecuteNonQueryAsync(
            conn,
            """
            UPDATE input_parameter
            SET is_deleted = TRUE, modified_by = @modified_by, modified_at = now(), correlation_id = @correlation_id
            WHERE asset_id = @asset_id AND is_deleted = FALSE;
            """,
            new[] { ("asset_id", (object)assetId), ("modified_by", (object)modifiedBy), ("correlation_id", (object)correlationId) },
            cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return true;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Checks whether a non-deleted input parameter exists and belongs to the given asset.
    /// </summary>
    public async Task<bool> InputParameterBelongsToAssetAsync(long assetId, long inputParameterId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT 1
            FROM input_parameter
            WHERE input_parameter_id = @input_parameter_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "input_parameter_id", inputParameterId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Checks whether a non-deleted throughput equation exists and belongs to the given input parameter.
    /// </summary>
    public async Task<bool> ThroughputEquationBelongsToInputAsync(long inputParameterId, long throughputEquationId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT 1
            FROM throughput_equation
            WHERE throughput_equation_id = @throughput_equation_id
              AND input_parameter_id = @input_parameter_id
              AND is_deleted = FALSE
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);
        AddParam(cmd, "throughput_equation_id", throughputEquationId);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Checks whether adding (or updating) a parent-input mapping would create a cycle in the parent/child graph.
    /// </summary>
    /// <remarks>
    /// BRD evidence: §6.6 “Hierarchy Validity: No cycles; parent-child consistency required.”
    ///
    /// Contract:
    /// - Inputs:
    ///   - <paramref name="childInputParameterId"/>: the child node for the mapping
    ///   - <paramref name="proposedParentInputParameterId"/>: the proposed parent node for the mapping
    ///   - <paramref name="excludeParentInputMappingId"/>: optional, used when updating an existing mapping so it is not considered in traversal
    /// - Output:
    ///   - true if a cycle would be created (i.e., the child is reachable from the proposed parent following existing parent links)
    /// - Notes:
    ///   - Soft-deleted mappings are ignored.
    /// </remarks>
    public async Task<bool> WouldParentInputMappingCreateCycleAsync(
        long childInputParameterId,
        long proposedParentInputParameterId,
        long? excludeParentInputMappingId,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // If the child is reachable from the proposed parent by traversing parent links upward,
        // then adding proposedParent -> ... -> child would create a cycle.
        const string sql = """
            WITH RECURSIVE ancestors AS (
                SELECT
                    pim.parent_input_parameter_id AS node_id
                FROM parent_input_mapping pim
                WHERE pim.child_input_parameter_id = @start_node
                  AND pim.is_deleted = FALSE
                  AND (@exclude_id IS NULL OR pim.parent_input_mapping_id <> @exclude_id)

                UNION

                SELECT
                    pim2.parent_input_parameter_id AS node_id
                FROM parent_input_mapping pim2
                INNER JOIN ancestors a ON a.node_id = pim2.child_input_parameter_id
                WHERE pim2.is_deleted = FALSE
                  AND (@exclude_id IS NULL OR pim2.parent_input_mapping_id <> @exclude_id)
            )
            SELECT 1
            FROM ancestors
            WHERE node_id = @target_node
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "start_node", proposedParentInputParameterId);
        AddParam(cmd, "target_node", childInputParameterId);
        AddParam(
            cmd,
            "exclude_id",
            excludeParentInputMappingId.HasValue ? excludeParentInputMappingId.Value : DBNull.Value,
            NpgsqlDbType.Bigint);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is not null;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Checks whether the given reporting attribute combination already exists under an asset.
    /// </summary>
    /// <remarks>
    /// BRD evidence: §6.7 “Uniqueness Constraint: Duplicate attribute combinations prevented.”
    ///
    /// Since the BRD does not specify a database-level uniqueness key, we enforce uniqueness at the
    /// API level for the full visible combination captured on the tab:
    /// (asset_id, reporting_program_id, attribute_name, attribute_value) among non-deleted rows.
    /// </remarks>
    public async Task<bool> ReportingAttributeCombinationExistsAsync(
        long assetId,
        long reportingProgramId,
        string attributeName,
        string attributeValue,
        long? excludeReportingAttributeMappingId,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT 1
            FROM reporting_attribute_mapping
            WHERE asset_id = @asset_id
              AND reporting_program_id = @reporting_program_id
              AND attribute_name = @attribute_name
              AND attribute_value = @attribute_value
              AND is_deleted = FALSE
              AND (@exclude_id IS NULL OR reporting_attribute_mapping_id <> @exclude_id)
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "reporting_program_id", reportingProgramId);
        AddParam(cmd, "attribute_name", attributeName);
        AddParam(cmd, "attribute_value", attributeValue);
        AddParam(cmd, "exclude_id", excludeReportingAttributeMappingId.HasValue ? excludeReportingAttributeMappingId.Value : DBNull.Value);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is not null;
    }

    // ---------------------------------------------------------------------
    // Child resources (BRD evidenced tables)
    // ---------------------------------------------------------------------

    // ------------------------
    // asset_status_log
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets the most recent (latest by status_from_date) status_to_date for an asset, excluding soft-deleted rows.
    /// </summary>
    /// <remarks>
    /// BRD §6.2 chronology rule evidence: “Latest status from-date must be after previous to-date”.
    /// This method supports that validation by retrieving the prior row’s <c>status_to_date</c> (if any).
    /// </remarks>
    public async Task<DateOnly?> GetLatestStatusToDateAsync(long assetId, long? excludeAssetStatusLogId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT status_to_date
            FROM asset_status_log
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
              AND (@exclude_id IS NULL OR asset_status_log_id <> @exclude_id)
            ORDER BY status_from_date DESC, asset_status_log_id DESC
            LIMIT 1;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "exclude_id", excludeAssetStatusLogId.HasValue ? excludeAssetStatusLogId.Value : DBNull.Value);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        if (scalar is null || scalar is DBNull)
        {
            return null;
        }

        // Stored as date (mapped to DateTime by Npgsql); normalize to DateOnly.
        return DateOnly.FromDateTime((DateTime)scalar);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a status log row for an asset.
    /// </summary>
    public async Task<AssetStatusLogDto> CreateAssetStatusLogAsync(long assetId, CreateAssetStatusLogRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO asset_status_log (
                asset_id,
                operating_status,
                status_from_date,
                status_to_date,
                comments,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @asset_id,
                @operating_status,
                @status_from_date,
                @status_to_date,
                @comments,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                asset_status_log_id,
                asset_id,
                operating_status,
                status_from_date,
                status_to_date,
                comments,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "operating_status", request.OperatingStatus);
        AddParam(cmd, "status_from_date", request.StatusFromDate.ToDateTime(TimeOnly.MinValue));
        AddParam(cmd, "status_to_date", request.StatusToDate.HasValue ? request.StatusToDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        AddParam(cmd, "comments", (object?)request.Comments ?? DBNull.Value);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create asset_status_log failed: INSERT returned no row.");
        }

        return ReadAssetStatusLog(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists status logs for an asset (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<AssetStatusLogDto>> ListAssetStatusLogsAsync(long assetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                asset_status_log_id,
                asset_id,
                operating_status,
                status_from_date,
                status_to_date,
                comments,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM asset_status_log
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            ORDER BY status_from_date DESC, asset_status_log_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);

        var results = new List<AssetStatusLogDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAssetStatusLog(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets a specific status log row under an asset scope (returns null if not found or not under asset).
    /// </summary>
    public async Task<AssetStatusLogDto?> GetAssetStatusLogByIdAsync(long assetId, long assetStatusLogId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                asset_status_log_id,
                asset_id,
                operating_status,
                status_from_date,
                status_to_date,
                comments,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM asset_status_log
            WHERE asset_status_log_id = @asset_status_log_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "asset_status_log_id", assetStatusLogId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAssetStatusLog(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a status log row under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<AssetStatusLogDto?> UpdateAssetStatusLogAsync(
        long assetId,
        long assetStatusLogId,
        UpdateAssetStatusLogRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE asset_status_log
            SET
                operating_status = @operating_status,
                status_from_date = @status_from_date,
                status_to_date = @status_to_date,
                comments = @comments,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE asset_status_log_id = @asset_status_log_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE
            RETURNING
                asset_status_log_id,
                asset_id,
                operating_status,
                status_from_date,
                status_to_date,
                comments,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "asset_status_log_id", assetStatusLogId);
        AddParam(cmd, "operating_status", request.OperatingStatus);
        AddParam(cmd, "status_from_date", request.StatusFromDate.ToDateTime(TimeOnly.MinValue));
        AddParam(cmd, "status_to_date", request.StatusToDate.HasValue ? request.StatusToDate.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        AddParam(cmd, "comments", (object?)request.Comments ?? DBNull.Value);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAssetStatusLog(reader);
    }

    // ------------------------
    // additional_asset_id
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an additional asset id row under an asset.
    /// </summary>
    public async Task<AdditionalAssetIdDto> CreateAdditionalAssetIdAsync(long assetId, CreateAdditionalAssetIdRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO additional_asset_id (
                asset_id,
                id_type,
                id_value,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @asset_id,
                @id_type,
                @id_value,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                additional_asset_id,
                asset_id,
                id_type,
                id_value,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "id_type", request.IdType);
        AddParam(cmd, "id_value", request.IdValue);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create additional_asset_id failed: INSERT returned no row.");
        }

        return ReadAdditionalAssetId(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists additional asset ids for an asset (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<AdditionalAssetIdDto>> ListAdditionalAssetIdsAsync(long assetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                additional_asset_id,
                asset_id,
                id_type,
                id_value,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM additional_asset_id
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            ORDER BY additional_asset_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);

        var results = new List<AdditionalAssetIdDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAdditionalAssetId(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets a specific additional asset id row under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<AdditionalAssetIdDto?> GetAdditionalAssetIdByIdAsync(long assetId, long additionalAssetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                additional_asset_id,
                asset_id,
                id_type,
                id_value,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM additional_asset_id
            WHERE additional_asset_id = @additional_asset_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "additional_asset_id", additionalAssetId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAdditionalAssetId(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an additional asset id row under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<AdditionalAssetIdDto?> UpdateAdditionalAssetIdAsync(
        long assetId,
        long additionalAssetId,
        UpdateAdditionalAssetIdRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE additional_asset_id
            SET
                id_type = @id_type,
                id_value = @id_value,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE additional_asset_id = @additional_asset_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE
            RETURNING
                additional_asset_id,
                asset_id,
                id_type,
                id_value,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "additional_asset_id", additionalAssetId);
        AddParam(cmd, "id_type", request.IdType);
        AddParam(cmd, "id_value", request.IdValue);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAdditionalAssetId(reader);
    }

    // ------------------------
    // asset_property
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an asset property row under an asset.
    /// </summary>
    public async Task<AssetPropertyDto> CreateAssetPropertyAsync(long assetId, CreateAssetPropertyRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO asset_property (
                asset_id,
                property_name,
                property_value,
                from_date,
                notes,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @asset_id,
                @property_name,
                @property_value,
                @from_date,
                @notes,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                asset_property_id,
                asset_id,
                property_name,
                property_value,
                from_date,
                notes,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "property_name", request.PropertyName);
        AddParam(cmd, "property_value", request.PropertyValue);
        AddParam(cmd, "from_date", request.FromDate.ToDateTime(TimeOnly.MinValue));
        AddParam(cmd, "notes", request.Notes);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create asset_property failed: INSERT returned no row.");
        }

        return ReadAssetProperty(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists asset properties for an asset (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<AssetPropertyDto>> ListAssetPropertiesAsync(long assetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                asset_property_id,
                asset_id,
                property_name,
                property_value,
                from_date,
                notes,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM asset_property
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            ORDER BY from_date DESC, asset_property_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);

        var results = new List<AssetPropertyDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadAssetProperty(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets a specific asset property row under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<AssetPropertyDto?> GetAssetPropertyByIdAsync(long assetId, long assetPropertyId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                asset_property_id,
                asset_id,
                property_name,
                property_value,
                from_date,
                notes,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM asset_property
            WHERE asset_property_id = @asset_property_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "asset_property_id", assetPropertyId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAssetProperty(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an asset property row under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<AssetPropertyDto?> UpdateAssetPropertyAsync(
        long assetId,
        long assetPropertyId,
        UpdateAssetPropertyRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE asset_property
            SET
                property_name = @property_name,
                property_value = @property_value,
                from_date = @from_date,
                notes = @notes,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE asset_property_id = @asset_property_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE
            RETURNING
                asset_property_id,
                asset_id,
                property_name,
                property_value,
                from_date,
                notes,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "asset_property_id", assetPropertyId);
        AddParam(cmd, "property_name", request.PropertyName);
        AddParam(cmd, "property_value", request.PropertyValue);
        AddParam(cmd, "from_date", request.FromDate.ToDateTime(TimeOnly.MinValue));
        AddParam(cmd, "notes", request.Notes);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadAssetProperty(reader);
    }

    // ------------------------
    // control_device_mapping
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a control device mapping row under an asset.
    /// </summary>
    public async Task<ControlDeviceMappingDto> CreateControlDeviceMappingAsync(
        long assetId,
        CreateControlDeviceMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO control_device_mapping (
                asset_id,
                control_device_id,
                control_device_name_or_ref,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @asset_id,
                @control_device_id,
                @control_device_name_or_ref,
                @in_use_flag,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                control_device_mapping_id,
                asset_id,
                control_device_id,
                control_device_name_or_ref,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "control_device_id", request.ControlDeviceId);
        AddParam(cmd, "control_device_name_or_ref", request.ControlDeviceNameOrRef);
        AddParam(cmd, "in_use_flag", request.InUseFlag);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create control_device_mapping failed: INSERT returned no row.");
        }

        return ReadControlDeviceMapping(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists control device mappings under an asset (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<ControlDeviceMappingDto>> ListControlDeviceMappingsAsync(long assetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                control_device_mapping_id,
                asset_id,
                control_device_id,
                control_device_name_or_ref,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM control_device_mapping
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            ORDER BY control_device_mapping_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);

        var results = new List<ControlDeviceMappingDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadControlDeviceMapping(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets a control device mapping row under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<ControlDeviceMappingDto?> GetControlDeviceMappingByIdAsync(long assetId, long controlDeviceMappingId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                control_device_mapping_id,
                asset_id,
                control_device_id,
                control_device_name_or_ref,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM control_device_mapping
            WHERE control_device_mapping_id = @control_device_mapping_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "control_device_mapping_id", controlDeviceMappingId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadControlDeviceMapping(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a control device mapping row under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<ControlDeviceMappingDto?> UpdateControlDeviceMappingAsync(
        long assetId,
        long controlDeviceMappingId,
        UpdateControlDeviceMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE control_device_mapping
            SET
                control_device_id = @control_device_id,
                control_device_name_or_ref = @control_device_name_or_ref,
                in_use_flag = @in_use_flag,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE control_device_mapping_id = @control_device_mapping_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE
            RETURNING
                control_device_mapping_id,
                asset_id,
                control_device_id,
                control_device_name_or_ref,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "control_device_mapping_id", controlDeviceMappingId);
        AddParam(cmd, "control_device_id", request.ControlDeviceId);
        AddParam(cmd, "control_device_name_or_ref", request.ControlDeviceNameOrRef);
        AddParam(cmd, "in_use_flag", request.InUseFlag);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadControlDeviceMapping(reader);
    }

    // ------------------------
    // input_parameter
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an input parameter row under an asset.
    /// </summary>
    public async Task<InputParameterDto> CreateInputParameterAsync(long assetId, CreateInputParameterRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO input_parameter (
                asset_id,
                input_parameter_name,
                uom_id,
                reporting_program_id,
                input_type,
                data_entry_frequency,
                fuel_mapping,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @asset_id,
                @input_parameter_name,
                @uom_id,
                @reporting_program_id,
                @input_type,
                @data_entry_frequency,
                @fuel_mapping,
                @in_use_flag,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                input_parameter_id,
                asset_id,
                input_parameter_name,
                uom_id,
                reporting_program_id,
                input_type,
                data_entry_frequency,
                fuel_mapping,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "input_parameter_name", request.InputParameterName);
        AddParam(cmd, "uom_id", request.UomId.HasValue ? request.UomId.Value : DBNull.Value);
        AddParam(cmd, "reporting_program_id", request.ReportingProgramId.HasValue ? request.ReportingProgramId.Value : DBNull.Value);
        AddParam(cmd, "input_type", request.InputType);
        AddParam(cmd, "data_entry_frequency", request.DataEntryFrequency);
        AddParam(cmd, "fuel_mapping", (object?)request.FuelMapping ?? DBNull.Value);
        AddParam(cmd, "in_use_flag", request.InUseFlag);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create input_parameter failed: INSERT returned no row.");
        }

        return ReadInputParameter(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists input parameters under an asset (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<InputParameterDto>> ListInputParametersAsync(long assetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                input_parameter_id,
                asset_id,
                input_parameter_name,
                uom_id,
                reporting_program_id,
                input_type,
                data_entry_frequency,
                fuel_mapping,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM input_parameter
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            ORDER BY input_parameter_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);

        var results = new List<InputParameterDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadInputParameter(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets an input parameter under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<InputParameterDto?> GetInputParameterByIdAsync(long assetId, long inputParameterId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                input_parameter_id,
                asset_id,
                input_parameter_name,
                uom_id,
                reporting_program_id,
                input_type,
                data_entry_frequency,
                fuel_mapping,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM input_parameter
            WHERE input_parameter_id = @input_parameter_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "input_parameter_id", inputParameterId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadInputParameter(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an input parameter under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<InputParameterDto?> UpdateInputParameterAsync(
        long assetId,
        long inputParameterId,
        UpdateInputParameterRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE input_parameter
            SET
                input_parameter_name = @input_parameter_name,
                uom_id = @uom_id,
                reporting_program_id = @reporting_program_id,
                input_type = @input_type,
                data_entry_frequency = @data_entry_frequency,
                fuel_mapping = @fuel_mapping,
                in_use_flag = @in_use_flag,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE input_parameter_id = @input_parameter_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE
            RETURNING
                input_parameter_id,
                asset_id,
                input_parameter_name,
                uom_id,
                reporting_program_id,
                input_type,
                data_entry_frequency,
                fuel_mapping,
                in_use_flag,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "input_parameter_id", inputParameterId);
        AddParam(cmd, "input_parameter_name", request.InputParameterName);
        AddParam(cmd, "uom_id", request.UomId.HasValue ? request.UomId.Value : DBNull.Value);
        AddParam(cmd, "reporting_program_id", request.ReportingProgramId.HasValue ? request.ReportingProgramId.Value : DBNull.Value);
        AddParam(cmd, "input_type", request.InputType);
        AddParam(cmd, "data_entry_frequency", request.DataEntryFrequency);
        AddParam(cmd, "fuel_mapping", (object?)request.FuelMapping ?? DBNull.Value);
        AddParam(cmd, "in_use_flag", request.InUseFlag);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadInputParameter(reader);
    }

    // ------------------------
    // parent_input_mapping (scoped under child input parameter)
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a parent input mapping for a given child input parameter id.
    /// </summary>
    public async Task<ParentInputMappingDto> CreateParentInputMappingAsync(
        long childInputParameterId,
        CreateParentInputMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO parent_input_mapping (
                child_input_parameter_id,
                parent_input_parameter_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @child_input_parameter_id,
                @parent_input_parameter_id,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                parent_input_mapping_id,
                child_input_parameter_id,
                parent_input_parameter_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "child_input_parameter_id", childInputParameterId);
        AddParam(cmd, "parent_input_parameter_id", request.ParentInputParameterId);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create parent_input_mapping failed: INSERT returned no row.");
        }

        return ReadParentInputMapping(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists parent input mappings for a given child input parameter id (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<ParentInputMappingDto>> ListParentInputMappingsAsync(long childInputParameterId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                parent_input_mapping_id,
                child_input_parameter_id,
                parent_input_parameter_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM parent_input_mapping
            WHERE child_input_parameter_id = @child_input_parameter_id
              AND is_deleted = FALSE
            ORDER BY parent_input_mapping_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "child_input_parameter_id", childInputParameterId);

        var results = new List<ParentInputMappingDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadParentInputMapping(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a parent input mapping under a child input parameter scope (returns null if not found/under child).
    /// </summary>
    public async Task<ParentInputMappingDto?> UpdateParentInputMappingAsync(
        long childInputParameterId,
        long parentInputMappingId,
        UpdateParentInputMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE parent_input_mapping
            SET
                parent_input_parameter_id = @parent_input_parameter_id,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE parent_input_mapping_id = @parent_input_mapping_id
              AND child_input_parameter_id = @child_input_parameter_id
              AND is_deleted = FALSE
            RETURNING
                parent_input_mapping_id,
                child_input_parameter_id,
                parent_input_parameter_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "child_input_parameter_id", childInputParameterId);
        AddParam(cmd, "parent_input_mapping_id", parentInputMappingId);
        AddParam(cmd, "parent_input_parameter_id", request.ParentInputParameterId);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadParentInputMapping(reader);
    }

    // ------------------------
    // reporting_attribute_mapping
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a reporting attribute mapping under an asset.
    /// </summary>
    public async Task<ReportingAttributeMappingDto> CreateReportingAttributeMappingAsync(
        long assetId,
        CreateReportingAttributeMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO reporting_attribute_mapping (
                asset_id,
                attribute_name,
                attribute_value,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @asset_id,
                @attribute_name,
                @attribute_value,
                @reporting_program_id,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                reporting_attribute_mapping_id,
                asset_id,
                attribute_name,
                attribute_value,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "attribute_name", request.AttributeName);
        AddParam(cmd, "attribute_value", request.AttributeValue);
        AddParam(cmd, "reporting_program_id", request.ReportingProgramId);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create reporting_attribute_mapping failed: INSERT returned no row.");
        }

        return ReadReportingAttributeMapping(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists reporting attribute mappings for an asset (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<ReportingAttributeMappingDto>> ListReportingAttributeMappingsAsync(long assetId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                reporting_attribute_mapping_id,
                asset_id,
                attribute_name,
                attribute_value,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM reporting_attribute_mapping
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            ORDER BY reporting_attribute_mapping_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);

        var results = new List<ReportingAttributeMappingDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadReportingAttributeMapping(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Gets a reporting attribute mapping under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<ReportingAttributeMappingDto?> GetReportingAttributeMappingByIdAsync(long assetId, long reportingAttributeMappingId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                reporting_attribute_mapping_id,
                asset_id,
                attribute_name,
                attribute_value,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM reporting_attribute_mapping
            WHERE reporting_attribute_mapping_id = @reporting_attribute_mapping_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "reporting_attribute_mapping_id", reportingAttributeMappingId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadReportingAttributeMapping(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a reporting attribute mapping under an asset scope (returns null if not found/under asset).
    /// </summary>
    public async Task<ReportingAttributeMappingDto?> UpdateReportingAttributeMappingAsync(
        long assetId,
        long reportingAttributeMappingId,
        UpdateReportingAttributeMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE reporting_attribute_mapping
            SET
                attribute_name = @attribute_name,
                attribute_value = @attribute_value,
                reporting_program_id = @reporting_program_id,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE reporting_attribute_mapping_id = @reporting_attribute_mapping_id
              AND asset_id = @asset_id
              AND is_deleted = FALSE
            RETURNING
                reporting_attribute_mapping_id,
                asset_id,
                attribute_name,
                attribute_value,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "asset_id", assetId);
        AddParam(cmd, "reporting_attribute_mapping_id", reportingAttributeMappingId);
        AddParam(cmd, "attribute_name", request.AttributeName);
        AddParam(cmd, "attribute_value", request.AttributeValue);
        AddParam(cmd, "reporting_program_id", request.ReportingProgramId);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadReportingAttributeMapping(reader);
    }

    // ------------------------
    // ef_source_mapping (scoped under input parameter)
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an EF source mapping under an input parameter.
    /// </summary>
    public async Task<EfSourceMappingDto> CreateEfSourceMappingAsync(long inputParameterId, CreateEfSourceMappingRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // BRD: ef_source_mapping has a required FK to reporting_program_master.
        // The UI may not always send reportingProgramId (older clients / partial-tab implementations).
        // To prevent FK violations, we derive reporting_program_id from the parent input_parameter row when missing/invalid.
        //
        // IMPORTANT: If neither the request nor the parent input_parameter has a reporting_program_id, this is a
        // client/workflow validation issue, NOT "DB not configured". We therefore return a 400 via RequestValidationException.
        static long ResolveReportingProgramId(long? candidate)
        {
            // Treat null/<=0 as "not provided".
            return candidate.HasValue && candidate.Value > 0 ? candidate.Value : 0;
        }

        var resolvedReportingProgramId = ResolveReportingProgramId(request.ReportingProgramId);
        if (resolvedReportingProgramId <= 0)
        {
            const string rpSql = """
                SELECT reporting_program_id
                FROM input_parameter
                WHERE input_parameter_id = @input_parameter_id
                  AND is_deleted = FALSE;
                """;

            await using var rpCmd = new NpgsqlCommand(rpSql, conn);
            AddParam(rpCmd, "input_parameter_id", inputParameterId);

            var scalar = await rpCmd.ExecuteScalarAsync(cancellationToken);
            if (scalar is null || scalar is DBNull)
            {
                throw new DataAssetBackend.Infrastructure.Api.RequestValidationException(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["reportingProgramId"] = new[]
                        {
                            "reportingProgramId is required (either provide it on the EF Source Mapping request, or set it on the Input Parameter).",
                            "Missing: reportingProgramId was not provided and could not be derived from the parent input parameter."
                        }
                    });
            }

            resolvedReportingProgramId = Convert.ToInt64(scalar);
            if (resolvedReportingProgramId <= 0)
            {
                throw new DataAssetBackend.Infrastructure.Api.RequestValidationException(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["reportingProgramId"] = new[]
                        {
                            "reportingProgramId is required (either provide it on the EF Source Mapping request, or set it on the Input Parameter).",
                            "Invalid: derived reportingProgramId from the parent input parameter was <= 0."
                        }
                    });
            }
        }

        const string sql = """
            INSERT INTO ef_source_mapping (
                input_parameter_id,
                ef_source_set_or_table,
                equation_setup,
                scalar_values,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @input_parameter_id,
                @ef_source_set_or_table,
                @equation_setup,
                @scalar_values,
                @reporting_program_id,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                ef_source_mapping_id,
                input_parameter_id,
                ef_source_set_or_table,
                equation_setup,
                scalar_values,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);
        AddParam(cmd, "ef_source_set_or_table", request.EfSourceSetOrTable);
        AddParam(cmd, "equation_setup", (object?)request.EquationSetup ?? DBNull.Value);
        AddParam(cmd, "scalar_values", (object?)request.ScalarValues ?? DBNull.Value);
        AddParam(cmd, "reporting_program_id", resolvedReportingProgramId);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create ef_source_mapping failed: INSERT returned no row.");
        }

        return ReadEfSourceMapping(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists EF source mappings under an input parameter (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<EfSourceMappingDto>> ListEfSourceMappingsAsync(long inputParameterId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                ef_source_mapping_id,
                input_parameter_id,
                ef_source_set_or_table,
                equation_setup,
                scalar_values,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM ef_source_mapping
            WHERE input_parameter_id = @input_parameter_id
              AND is_deleted = FALSE
            ORDER BY ef_source_mapping_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);

        var results = new List<EfSourceMappingDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadEfSourceMapping(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an EF source mapping under an input parameter scope (returns null if not found/under input).
    /// </summary>
    public async Task<EfSourceMappingDto?> UpdateEfSourceMappingAsync(
        long inputParameterId,
        long efSourceMappingId,
        UpdateEfSourceMappingRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // See CreateEfSourceMappingAsync for rationale: protect against FK violations by deriving reporting_program_id
        // from the parent input_parameter when client omits/sends invalid id.
        static long ResolveReportingProgramId(long? candidate) => candidate.HasValue && candidate.Value > 0 ? candidate.Value : 0;

        var resolvedReportingProgramId = ResolveReportingProgramId(request.ReportingProgramId);
        if (resolvedReportingProgramId <= 0)
        {
            const string rpSql = """
                SELECT reporting_program_id
                FROM input_parameter
                WHERE input_parameter_id = @input_parameter_id
                  AND is_deleted = FALSE;
                """;

            await using var rpCmd = new NpgsqlCommand(rpSql, conn);
            AddParam(rpCmd, "input_parameter_id", inputParameterId);

            var scalar = await rpCmd.ExecuteScalarAsync(cancellationToken);
            if (scalar is null || scalar is DBNull)
            {
                throw new DataAssetBackend.Infrastructure.Api.RequestValidationException(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["reportingProgramId"] = new[]
                        {
                            "reportingProgramId is required (either provide it on the EF Source Mapping request, or set it on the Input Parameter).",
                            "Missing: reportingProgramId was not provided and could not be derived from the parent input parameter."
                        }
                    });
            }

            resolvedReportingProgramId = Convert.ToInt64(scalar);
            if (resolvedReportingProgramId <= 0)
            {
                throw new DataAssetBackend.Infrastructure.Api.RequestValidationException(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["reportingProgramId"] = new[]
                        {
                            "reportingProgramId is required (either provide it on the EF Source Mapping request, or set it on the Input Parameter).",
                            "Invalid: derived reportingProgramId from the parent input parameter was <= 0."
                        }
                    });
            }
        }

        const string sql = """
            UPDATE ef_source_mapping
            SET
                ef_source_set_or_table = @ef_source_set_or_table,
                equation_setup = @equation_setup,
                scalar_values = @scalar_values,
                reporting_program_id = @reporting_program_id,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE ef_source_mapping_id = @ef_source_mapping_id
              AND input_parameter_id = @input_parameter_id
              AND is_deleted = FALSE
            RETURNING
                ef_source_mapping_id,
                input_parameter_id,
                ef_source_set_or_table,
                equation_setup,
                scalar_values,
                reporting_program_id,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);
        AddParam(cmd, "ef_source_mapping_id", efSourceMappingId);
        AddParam(cmd, "ef_source_set_or_table", request.EfSourceSetOrTable);
        AddParam(cmd, "equation_setup", (object?)request.EquationSetup ?? DBNull.Value);
        AddParam(cmd, "scalar_values", (object?)request.ScalarValues ?? DBNull.Value);
        AddParam(cmd, "reporting_program_id", resolvedReportingProgramId);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadEfSourceMapping(reader);
    }

    // ------------------------
    // throughput_equation (scoped under input parameter)
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a throughput equation under an input parameter.
    /// </summary>
    public async Task<ThroughputEquationDto> CreateThroughputEquationAsync(long inputParameterId, CreateThroughputEquationRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Normalize compatibility fields (equationMasterId/equationText/year) into canonical DB columns.
        // This prevents FK violations like fk_throughput_equation_master and ensures DB NOT NULL constraints are met.
        request.Normalize();

        const string sql = """
            INSERT INTO throughput_equation (
                input_parameter_id,
                master_equation_id,
                generated_equation,
                reporting_year,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @input_parameter_id,
                @master_equation_id,
                @generated_equation,
                @reporting_year,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                throughput_equation_id,
                input_parameter_id,
                master_equation_id,
                generated_equation,
                reporting_year,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);
        AddParam(cmd, "master_equation_id", request.MasterEquationId);
        AddParam(cmd, "generated_equation", request.GeneratedEquation);
        AddParam(cmd, "reporting_year", request.ReportingYear);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create throughput_equation failed: INSERT returned no row.");
        }

        return ReadThroughputEquation(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists throughput equations under an input parameter (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<ThroughputEquationDto>> ListThroughputEquationsAsync(long inputParameterId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                throughput_equation_id,
                input_parameter_id,
                master_equation_id,
                generated_equation,
                reporting_year,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM throughput_equation
            WHERE input_parameter_id = @input_parameter_id
              AND is_deleted = FALSE
            ORDER BY reporting_year DESC, throughput_equation_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);

        var results = new List<ThroughputEquationDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadThroughputEquation(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a throughput equation under an input parameter scope (returns null if not found/under input).
    /// </summary>
    public async Task<ThroughputEquationDto?> UpdateThroughputEquationAsync(
        long inputParameterId,
        long throughputEquationId,
        UpdateThroughputEquationRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        // Normalize compatibility fields (equationMasterId/equationText/year) into canonical DB columns.
        request.Normalize();

        const string sql = """
            UPDATE throughput_equation
            SET
                master_equation_id = @master_equation_id,
                generated_equation = @generated_equation,
                reporting_year = @reporting_year,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE throughput_equation_id = @throughput_equation_id
              AND input_parameter_id = @input_parameter_id
              AND is_deleted = FALSE
            RETURNING
                throughput_equation_id,
                input_parameter_id,
                master_equation_id,
                generated_equation,
                reporting_year,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);
        AddParam(cmd, "throughput_equation_id", throughputEquationId);
        AddParam(cmd, "master_equation_id", request.MasterEquationId);
        AddParam(cmd, "generated_equation", request.GeneratedEquation);
        AddParam(cmd, "reporting_year", request.ReportingYear);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadThroughputEquation(reader);
    }

    // ------------------------
    // throughput_scalar (scoped under throughput equation)
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a throughput scalar under a throughput equation.
    /// </summary>
    public async Task<ThroughputScalarDto> CreateThroughputScalarAsync(long throughputEquationId, CreateThroughputScalarRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        request.Normalize();

        const string sql = """
            INSERT INTO throughput_scalar (
                throughput_equation_id,
                scalar_type,
                scalar_table,
                scalar_id,
                scalar_value,
                scalar_basis,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @throughput_equation_id,
                @scalar_type,
                @scalar_table,
                @scalar_id,
                @scalar_value,
                @scalar_basis,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                throughput_scalar_id,
                throughput_equation_id,
                scalar_type,
                scalar_table,
                scalar_id,
                scalar_value,
                scalar_basis,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "throughput_equation_id", throughputEquationId);
        AddParam(cmd, "scalar_type", request.ScalarType);
        AddParam(cmd, "scalar_table", (object?)request.ScalarTable ?? DBNull.Value);
        AddParam(cmd, "scalar_id", (object?)request.ScalarId ?? DBNull.Value);
        AddParam(cmd, "scalar_value", (object?)request.ScalarValue ?? DBNull.Value);
        AddParam(cmd, "scalar_basis", (object?)request.ScalarBasis ?? DBNull.Value);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create throughput_scalar failed: INSERT returned no row.");
        }

        return ReadThroughputScalar(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists throughput scalars under a throughput equation (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<ThroughputScalarDto>> ListThroughputScalarsAsync(long throughputEquationId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                throughput_scalar_id,
                throughput_equation_id,
                scalar_type,
                scalar_table,
                scalar_id,
                scalar_value,
                scalar_basis,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM throughput_scalar
            WHERE throughput_equation_id = @throughput_equation_id
              AND is_deleted = FALSE
            ORDER BY throughput_scalar_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "throughput_equation_id", throughputEquationId);

        var results = new List<ThroughputScalarDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadThroughputScalar(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a throughput scalar under a throughput equation scope (returns null if not found/under equation).
    /// </summary>
    public async Task<ThroughputScalarDto?> UpdateThroughputScalarAsync(
        long throughputEquationId,
        long throughputScalarId,
        UpdateThroughputScalarRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        request.Normalize();

        const string sql = """
            UPDATE throughput_scalar
            SET
                scalar_type = @scalar_type,
                scalar_table = @scalar_table,
                scalar_id = @scalar_id,
                scalar_value = @scalar_value,
                scalar_basis = @scalar_basis,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE throughput_scalar_id = @throughput_scalar_id
              AND throughput_equation_id = @throughput_equation_id
              AND is_deleted = FALSE
            RETURNING
                throughput_scalar_id,
                throughput_equation_id,
                scalar_type,
                scalar_table,
                scalar_id,
                scalar_value,
                scalar_basis,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "throughput_equation_id", throughputEquationId);
        AddParam(cmd, "throughput_scalar_id", throughputScalarId);
        AddParam(cmd, "scalar_type", request.ScalarType);
        AddParam(cmd, "scalar_table", (object?)request.ScalarTable ?? DBNull.Value);
        AddParam(cmd, "scalar_id", (object?)request.ScalarId ?? DBNull.Value);
        AddParam(cmd, "scalar_value", (object?)request.ScalarValue ?? DBNull.Value);
        AddParam(cmd, "scalar_basis", (object?)request.ScalarBasis ?? DBNull.Value);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadThroughputScalar(reader);
    }

    // ------------------------
    // data_input_value (scoped under input parameter)
    // ------------------------

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a data input value under an input parameter.
    /// </summary>
    public async Task<DataInputValueDto> CreateDataInputValueAsync(long inputParameterId, CreateDataInputValueRequest request, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO data_input_value (
                input_parameter_id,
                input_parameter_value,
                reporting_year,
                reporting_period,
                calculated_throughput_output,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            )
            VALUES (
                @input_parameter_id,
                @input_parameter_value,
                @reporting_year,
                @reporting_period,
                @calculated_throughput_output,
                @created_by,
                now(),
                @created_by,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING
                data_input_value_id,
                input_parameter_id,
                input_parameter_value,
                reporting_year,
                reporting_period,
                calculated_throughput_output,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);
        AddParam(cmd, "input_parameter_value", (object?)request.InputParameterValue ?? DBNull.Value);
        AddParam(cmd, "reporting_year", request.ReportingYear);
        AddParam(cmd, "reporting_period", request.ReportingPeriod);
        AddParam(cmd, "calculated_throughput_output", (object?)request.CalculatedThroughputOutput ?? DBNull.Value);
        AddParam(cmd, "created_by", request.CreatedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create data_input_value failed: INSERT returned no row.");
        }

        return ReadDataInputValue(reader);
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Lists data input values under an input parameter (excluding soft-deleted rows).
    /// </summary>
    public async Task<IReadOnlyList<DataInputValueDto>> ListDataInputValuesAsync(long inputParameterId, CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                data_input_value_id,
                input_parameter_id,
                input_parameter_value,
                reporting_year,
                reporting_period,
                calculated_throughput_output,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id
            FROM data_input_value
            WHERE input_parameter_id = @input_parameter_id
              AND is_deleted = FALSE
            ORDER BY reporting_year DESC, data_input_value_id DESC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);

        var results = new List<DataInputValueDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadDataInputValue(reader));
        }

        return results;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates a data input value under an input parameter scope (returns null if not found/under input).
    /// </summary>
    public async Task<DataInputValueDto?> UpdateDataInputValueAsync(
        long inputParameterId,
        long dataInputValueId,
        UpdateDataInputValueRequest request,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE data_input_value
            SET
                input_parameter_value = @input_parameter_value,
                reporting_year = @reporting_year,
                reporting_period = @reporting_period,
                calculated_throughput_output = @calculated_throughput_output,
                modified_by = @modified_by,
                modified_at = now(),
                correlation_id = @correlation_id
            WHERE data_input_value_id = @data_input_value_id
              AND input_parameter_id = @input_parameter_id
              AND is_deleted = FALSE
            RETURNING
                data_input_value_id,
                input_parameter_id,
                input_parameter_value,
                reporting_year,
                reporting_period,
                calculated_throughput_output,
                created_by,
                created_at,
                modified_by,
                modified_at,
                is_deleted,
                correlation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        AddParam(cmd, "input_parameter_id", inputParameterId);
        AddParam(cmd, "data_input_value_id", dataInputValueId);
        AddParam(cmd, "input_parameter_value", (object?)request.InputParameterValue ?? DBNull.Value);
        AddParam(cmd, "reporting_year", request.ReportingYear);
        AddParam(cmd, "reporting_period", request.ReportingPeriod);
        AddParam(cmd, "calculated_throughput_output", (object?)request.CalculatedThroughputOutput ?? DBNull.Value);
        AddParam(cmd, "modified_by", request.ModifiedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadDataInputValue(reader);
    }

    // ---------------------------------------------------------------------
    // Readers
    // ---------------------------------------------------------------------

    private static void AddParam(NpgsqlCommand cmd, string name, object value, NpgsqlDbType? dbType = null)
    {
        // Npgsql cannot infer the SQL type from DBNull.Value. If we pass DBNull without specifying a type,
        // PostgreSQL may fail with: “could not determine data type of parameter $N”.
        if (dbType.HasValue)
        {
            var p = cmd.Parameters.Add(name, dbType.Value);
            p.Value = value;
            return;
        }

        // For non-null values we keep the original behavior (infer type from CLR value).
        var inferred = cmd.Parameters.AddWithValue(name, value);
        _ = inferred;
    }

    private static async Task<int> ExecuteNonQueryAsync(
        NpgsqlConnection conn,
        string sql,
        IEnumerable<(string Name, object Value)> parameters,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
        {
            AddParam(cmd, name, value);
        }

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
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

    private static AssetStatusLogDto ReadAssetStatusLog(NpgsqlDataReader r)
    {
        var fromDate = DateOnly.FromDateTime(r.GetFieldValue<DateTime>(r.GetOrdinal("status_from_date")));
        DateOnly? toDate = r.IsDBNull(r.GetOrdinal("status_to_date"))
            ? null
            : DateOnly.FromDateTime(r.GetFieldValue<DateTime>(r.GetOrdinal("status_to_date")));

        return new AssetStatusLogDto(
            AssetStatusLogId: r.GetInt64(r.GetOrdinal("asset_status_log_id")),
            AssetId: r.GetInt64(r.GetOrdinal("asset_id")),
            OperatingStatus: r.GetString(r.GetOrdinal("operating_status")),
            StatusFromDate: fromDate,
            StatusToDate: toDate,
            Comments: r.IsDBNull(r.GetOrdinal("comments")) ? null : r.GetString(r.GetOrdinal("comments")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static AdditionalAssetIdDto ReadAdditionalAssetId(NpgsqlDataReader r)
    {
        return new AdditionalAssetIdDto(
            AdditionalAssetId: r.GetInt64(r.GetOrdinal("additional_asset_id")),
            AssetId: r.GetInt64(r.GetOrdinal("asset_id")),
            IdType: r.GetString(r.GetOrdinal("id_type")),
            IdValue: r.GetString(r.GetOrdinal("id_value")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static AssetPropertyDto ReadAssetProperty(NpgsqlDataReader r)
    {
        var fromDate = DateOnly.FromDateTime(r.GetFieldValue<DateTime>(r.GetOrdinal("from_date")));

        return new AssetPropertyDto(
            AssetPropertyId: r.GetInt64(r.GetOrdinal("asset_property_id")),
            AssetId: r.GetInt64(r.GetOrdinal("asset_id")),
            PropertyName: r.GetString(r.GetOrdinal("property_name")),
            PropertyValue: r.GetString(r.GetOrdinal("property_value")),
            FromDate: fromDate,
            Notes: r.GetString(r.GetOrdinal("notes")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ControlDeviceMappingDto ReadControlDeviceMapping(NpgsqlDataReader r)
    {
        return new ControlDeviceMappingDto(
            ControlDeviceMappingId: r.GetInt64(r.GetOrdinal("control_device_mapping_id")),
            AssetId: r.GetInt64(r.GetOrdinal("asset_id")),
            ControlDeviceId: r.GetInt64(r.GetOrdinal("control_device_id")),
            ControlDeviceNameOrRef: r.GetString(r.GetOrdinal("control_device_name_or_ref")),
            InUseFlag: r.GetBoolean(r.GetOrdinal("in_use_flag")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static InputParameterDto ReadInputParameter(NpgsqlDataReader r)
    {
        return new InputParameterDto(
            InputParameterId: r.GetInt64(r.GetOrdinal("input_parameter_id")),
            AssetId: r.GetInt64(r.GetOrdinal("asset_id")),
            InputParameterName: r.GetString(r.GetOrdinal("input_parameter_name")),
            UomId: r.IsDBNull(r.GetOrdinal("uom_id")) ? null : r.GetInt64(r.GetOrdinal("uom_id")),
            ReportingProgramId: r.IsDBNull(r.GetOrdinal("reporting_program_id")) ? null : r.GetInt64(r.GetOrdinal("reporting_program_id")),
            InputType: r.GetString(r.GetOrdinal("input_type")),
            DataEntryFrequency: r.GetString(r.GetOrdinal("data_entry_frequency")),
            FuelMapping: r.IsDBNull(r.GetOrdinal("fuel_mapping")) ? null : r.GetString(r.GetOrdinal("fuel_mapping")),
            InUseFlag: r.GetBoolean(r.GetOrdinal("in_use_flag")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ParentInputMappingDto ReadParentInputMapping(NpgsqlDataReader r)
    {
        return new ParentInputMappingDto(
            ParentInputMappingId: r.GetInt64(r.GetOrdinal("parent_input_mapping_id")),
            ChildInputParameterId: r.GetInt64(r.GetOrdinal("child_input_parameter_id")),
            ParentInputParameterId: r.GetInt64(r.GetOrdinal("parent_input_parameter_id")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ReportingAttributeMappingDto ReadReportingAttributeMapping(NpgsqlDataReader r)
    {
        return new ReportingAttributeMappingDto(
            ReportingAttributeMappingId: r.GetInt64(r.GetOrdinal("reporting_attribute_mapping_id")),
            AssetId: r.GetInt64(r.GetOrdinal("asset_id")),
            AttributeName: r.GetString(r.GetOrdinal("attribute_name")),
            AttributeValue: r.GetString(r.GetOrdinal("attribute_value")),
            ReportingProgramId: r.GetInt64(r.GetOrdinal("reporting_program_id")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static EfSourceMappingDto ReadEfSourceMapping(NpgsqlDataReader r)
    {
        return new EfSourceMappingDto(
            EfSourceMappingId: r.GetInt64(r.GetOrdinal("ef_source_mapping_id")),
            InputParameterId: r.GetInt64(r.GetOrdinal("input_parameter_id")),
            EfSourceSetOrTable: r.GetString(r.GetOrdinal("ef_source_set_or_table")),
            EquationSetup: r.IsDBNull(r.GetOrdinal("equation_setup")) ? null : r.GetString(r.GetOrdinal("equation_setup")),
            ScalarValues: r.IsDBNull(r.GetOrdinal("scalar_values")) ? null : r.GetString(r.GetOrdinal("scalar_values")),
            ReportingProgramId: r.GetInt64(r.GetOrdinal("reporting_program_id")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ThroughputEquationDto ReadThroughputEquation(NpgsqlDataReader r)
    {
        return new ThroughputEquationDto(
            ThroughputEquationId: r.GetInt64(r.GetOrdinal("throughput_equation_id")),
            InputParameterId: r.GetInt64(r.GetOrdinal("input_parameter_id")),
            MasterEquationId: r.GetInt64(r.GetOrdinal("master_equation_id")),
            GeneratedEquation: r.GetString(r.GetOrdinal("generated_equation")),
            ReportingYear: r.GetInt32(r.GetOrdinal("reporting_year")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static ThroughputScalarDto ReadThroughputScalar(NpgsqlDataReader r)
    {
        return new ThroughputScalarDto(
            ThroughputScalarId: r.GetInt64(r.GetOrdinal("throughput_scalar_id")),
            ThroughputEquationId: r.GetInt64(r.GetOrdinal("throughput_equation_id")),
            ScalarType: r.GetString(r.GetOrdinal("scalar_type")),
            ScalarTable: r.IsDBNull(r.GetOrdinal("scalar_table")) ? null : r.GetString(r.GetOrdinal("scalar_table")),
            ScalarId: r.IsDBNull(r.GetOrdinal("scalar_id")) ? null : r.GetString(r.GetOrdinal("scalar_id")),
            ScalarValue: r.IsDBNull(r.GetOrdinal("scalar_value")) ? null : r.GetString(r.GetOrdinal("scalar_value")),
            ScalarBasis: r.IsDBNull(r.GetOrdinal("scalar_basis")) ? null : r.GetString(r.GetOrdinal("scalar_basis")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }

    private static DataInputValueDto ReadDataInputValue(NpgsqlDataReader r)
    {
        return new DataInputValueDto(
            DataInputValueId: r.GetInt64(r.GetOrdinal("data_input_value_id")),
            InputParameterId: r.GetInt64(r.GetOrdinal("input_parameter_id")),
            InputParameterValue: r.IsDBNull(r.GetOrdinal("input_parameter_value")) ? null : r.GetString(r.GetOrdinal("input_parameter_value")),
            ReportingYear: r.GetInt32(r.GetOrdinal("reporting_year")),
            ReportingPeriod: r.GetString(r.GetOrdinal("reporting_period")),
            CalculatedThroughputOutput: r.IsDBNull(r.GetOrdinal("calculated_throughput_output"))
                ? null
                : r.GetString(r.GetOrdinal("calculated_throughput_output")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }
}
