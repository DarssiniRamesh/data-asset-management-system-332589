using DataAssetBackend.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Repository implementing the BRD FR-03 Copy Asset workflow using the Flyway V2 schema.
/// </summary>
/// <remarks>
/// This repository is intentionally the *single* database implementation for copy+lineage, so the behavior is not duplicated
/// across controllers/flows (Reusable Flow Implementation: flows, not patches).
/// </remarks>
public sealed class AssetCopyRepository
{
    private readonly NpgsqlConnectionFactory _connectionFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="AssetCopyRepository" />.
    /// </summary>
    public AssetCopyRepository(NpgsqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private sealed record ModuleResult(string Name, int CopiedCount, int SkippedCount, string Status, string? Detail);

    // PUBLIC_INTERFACE
    /// <summary>
    /// Copies an asset and BRD-evidenced related entities, and records a lineage row (table: asset_copy_lineage).
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs:
    ///   - sourceAssetId: existing asset_id (non-deleted)
    ///   - request: CopyAssetRequest
    /// - Output: CopyAssetFlowResponse containing target asset, lineage and module results
    /// - Errors:
    ///   - Throws <see cref="AssetRepository.EntityNotFoundException"/> if source asset does not exist (non-deleted)
    ///   - Propagates <see cref="PostgresException"/> for constraint violations (caller maps to 409)
    ///   - Propagates <see cref="InvalidOperationException"/> for DB misconfiguration (existing pattern)
    /// - Side effects:
    ///   - Inserts into asset + child tables + asset_copy_lineage
    /// Observability:
    ///   - Uses provided logger for step logs and partial-skip explanation.
    /// </remarks>
    public async Task<AssetCopyFlows.CopyAssetFlowResponse> CopyAssetAsync(
        long sourceAssetId,
        CopyAssetRequest request,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var conn = await _connectionFactory.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            logger.LogInformation(
                "AssetCopyRepository.CopyAssetAsync started transaction. source_asset_id={SourceAssetId}, copy_operation_id={CopyOperationId}",
                sourceAssetId,
                request.CopyOperationId);

            // Validate source exists (exclude soft-deleted)
            var sourceExists = await ScalarExistsAsync(
                conn,
                tx,
                """
                SELECT 1
                FROM asset
                WHERE asset_id = @asset_id
                  AND is_deleted = FALSE
                LIMIT 1;
                """,
                new Dictionary<string, object> { ["asset_id"] = sourceAssetId },
                cancellationToken);

            if (!sourceExists)
            {
                throw new AssetRepository.EntityNotFoundException("asset", sourceAssetId);
            }

            // 1) Create target asset
            var targetAsset = await CreateTargetAssetAsync(conn, tx, request, cancellationToken);

            // 2) Copy BRD-evidenced asset-scoped child tables (all rows, excluding soft-deleted)
            var moduleResults = new List<ModuleResult>();

            moduleResults.Add(await CopyAssetStatusLogsAsync(conn, tx, sourceAssetId, targetAsset.AssetId, request, cancellationToken));
            moduleResults.Add(await CopyAdditionalAssetIdsAsync(conn, tx, sourceAssetId, targetAsset.AssetId, request, cancellationToken));
            moduleResults.Add(await CopyAssetPropertiesAsync(conn, tx, sourceAssetId, targetAsset.AssetId, request, cancellationToken));
            moduleResults.Add(await CopyControlDeviceMappingsAsync(conn, tx, sourceAssetId, targetAsset.AssetId, request, cancellationToken));
            moduleResults.Add(await CopyReportingAttributeMappingsAsync(conn, tx, sourceAssetId, targetAsset.AssetId, request, cancellationToken));

            // 3) Copy input parameter subtree with ID mapping (input_parameter -> ef_source_mapping, throughput_equation -> throughput_scalar, data_input_value)
            var inputCopy = await CopyInputParameterTreeAsync(conn, tx, sourceAssetId, targetAsset.AssetId, request, logger, cancellationToken);
            moduleResults.AddRange(inputCopy.ModuleResults);

            // 4) Record copy lineage (BRD §6.13)
            var lineage = await CreateLineageAsync(
                conn,
                tx,
                request,
                sourceAssetId,
                targetAsset.AssetId,
                inputCopy.ReplicationResultStatus,
                inputCopy.ReplicationResultDetail,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);

            logger.LogInformation(
                "AssetCopyRepository.CopyAssetAsync committed transaction. source_asset_id={SourceAssetId}, target_asset_id={TargetAssetId}, copy_operation_id={CopyOperationId}",
                sourceAssetId,
                targetAsset.AssetId,
                request.CopyOperationId);

            var response = new CopyAssetResponse(
                TargetAsset: targetAsset,
                Lineage: lineage,
                ReplicationResultStatus: inputCopy.ReplicationResultStatus,
                ReplicationResultDetail: inputCopy.ReplicationResultDetail,
                ModuleResults: moduleResults
                    .Select(m => new CopyAssetModuleReplicationResult(m.Name, m.CopiedCount, m.SkippedCount, m.Status, m.Detail))
                    .ToList());

            return new AssetCopyFlows.CopyAssetFlowResponse(response);
        }
        catch
        {
            try
            {
                await tx.RollbackAsync(cancellationToken);
            }
            catch
            {
                // Do not hide the original exception.
            }

            throw;
        }
    }

    private static async Task<AssetDto> CreateTargetAssetAsync(NpgsqlConnection conn, NpgsqlTransaction tx, CopyAssetRequest request, CancellationToken cancellationToken)
    {
        // Audit semantics (evidence-based, conservative):
        // - created_by/modified_by are required; we use CopyPerformedBy as the actor for the copy.
        // - correlation_id is required; use request.CorrelationId across all copied rows for traceability.
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

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        AddParam(cmd, "site_id", request.TargetAsset.SiteId);
        AddParam(cmd, "asset_group", request.TargetAsset.AssetGroup);
        AddParam(cmd, "process_group", request.TargetAsset.ProcessGroup);
        AddParam(cmd, "process_group_other_text", (object?)request.TargetAsset.ProcessGroupOtherText ?? DBNull.Value);
        AddParam(cmd, "asset_name", request.TargetAsset.AssetName);
        AddParam(cmd, "permit_eu_id", request.TargetAsset.PermitEuId);
        AddParam(cmd, "global_unique_asset_id", request.TargetAsset.GlobalUniqueAssetId);
        AddParam(cmd, "asset_description", (object?)request.TargetAsset.AssetDescription ?? DBNull.Value);
        AddParam(cmd, "stationary_flag", request.TargetAsset.StationaryFlag.HasValue ? request.TargetAsset.StationaryFlag.Value : DBNull.Value);
        AddParam(cmd, "parent_pseudo_asset_id", request.TargetAsset.ParentPseudoAssetId.HasValue ? request.TargetAsset.ParentPseudoAssetId.Value : DBNull.Value);
        AddParam(cmd, "created_by", request.CopyPerformedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Copy asset failed: target asset INSERT returned no row.");
        }

        return ReadAsset(reader);
    }

    private static async Task<ModuleResult> CopyAssetStatusLogsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceAssetId,
        long targetAssetId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
            SELECT
                @target_asset_id,
                operating_status,
                status_from_date,
                status_to_date,
                comments,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            FROM asset_status_log
            WHERE asset_id = @source_asset_id
              AND is_deleted = FALSE;
            """;

        var copied = await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["target_asset_id"] = targetAssetId,
                ["source_asset_id"] = sourceAssetId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);

        return new ModuleResult("asset_status_log", copied, 0, "Completed", null);
    }

    private static async Task<ModuleResult> CopyAdditionalAssetIdsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceAssetId,
        long targetAssetId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
            SELECT
                @target_asset_id,
                id_type,
                id_value,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            FROM additional_asset_id
            WHERE asset_id = @source_asset_id
              AND is_deleted = FALSE;
            """;

        var copied = await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["target_asset_id"] = targetAssetId,
                ["source_asset_id"] = sourceAssetId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);

        return new ModuleResult("additional_asset_id", copied, 0, "Completed", null);
    }

    private static async Task<ModuleResult> CopyAssetPropertiesAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceAssetId,
        long targetAssetId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
            SELECT
                @target_asset_id,
                property_name,
                property_value,
                from_date,
                notes,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            FROM asset_property
            WHERE asset_id = @source_asset_id
              AND is_deleted = FALSE;
            """;

        var copied = await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["target_asset_id"] = targetAssetId,
                ["source_asset_id"] = sourceAssetId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);

        return new ModuleResult("asset_property", copied, 0, "Completed", null);
    }

    private static async Task<ModuleResult> CopyControlDeviceMappingsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceAssetId,
        long targetAssetId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
            SELECT
                @target_asset_id,
                control_device_id,
                control_device_name_or_ref,
                in_use_flag,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            FROM control_device_mapping
            WHERE asset_id = @source_asset_id
              AND is_deleted = FALSE;
            """;

        var copied = await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["target_asset_id"] = targetAssetId,
                ["source_asset_id"] = sourceAssetId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);

        return new ModuleResult("control_device_mapping", copied, 0, "Completed", null);
    }

    private static async Task<ModuleResult> CopyReportingAttributeMappingsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceAssetId,
        long targetAssetId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
            SELECT
                @target_asset_id,
                attribute_name,
                attribute_value,
                reporting_program_id,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            FROM reporting_attribute_mapping
            WHERE asset_id = @source_asset_id
              AND is_deleted = FALSE;
            """;

        var copied = await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["target_asset_id"] = targetAssetId,
                ["source_asset_id"] = sourceAssetId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);

        return new ModuleResult("reporting_attribute_mapping", copied, 0, "Completed", null);
    }

    private sealed record InputTreeCopyResult(
        IReadOnlyList<ModuleResult> ModuleResults,
        string ReplicationResultStatus,
        string? ReplicationResultDetail);

    private static async Task<InputTreeCopyResult> CopyInputParameterTreeAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceAssetId,
        long targetAssetId,
        CopyAssetRequest request,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var results = new List<ModuleResult>();

        // Read source input parameters (exclude soft-deleted)
        var sourceInputs = await ReadInputParametersAsync(conn, tx, sourceAssetId, cancellationToken);

        // Insert target input parameters and build mapping old->new
        var inputIdMap = new Dictionary<long, long>();
        foreach (var src in sourceInputs)
        {
            var newId = await InsertInputParameterAsync(conn, tx, targetAssetId, src, request, cancellationToken);
            inputIdMap[src.InputParameterId] = newId;
        }

        results.Add(new ModuleResult("input_parameter", sourceInputs.Count, 0, "Completed", null));

        // Parent input mappings:
        // - Evidence does not specify whether parent/child can span multiple assets.
        // - We conservatively copy only mappings where both parent and child belong to the source asset (so mapping is resolvable).
        // - Any mapping referencing an input parameter outside the source asset is skipped and recorded as partial.
        var parentMap = await ReadParentInputMappingsForAssetAsync(conn, tx, sourceAssetId, cancellationToken);

        var parentCopied = 0;
        var parentSkipped = 0;

        foreach (var m in parentMap)
        {
            if (!inputIdMap.TryGetValue(m.ChildInputParameterId, out var newChild))
            {
                parentSkipped++;
                continue;
            }

            if (!inputIdMap.TryGetValue(m.ParentInputParameterId, out var newParent))
            {
                parentSkipped++;
                continue;
            }

            await InsertParentInputMappingAsync(conn, tx, newChild, newParent, request, cancellationToken);
            parentCopied++;
        }

        results.Add(new ModuleResult(
            "parent_input_mapping",
            parentCopied,
            parentSkipped,
            parentSkipped > 0 ? "Partial" : "Completed",
            parentSkipped > 0
                ? "Some parent_input_mapping rows were skipped because they referenced input parameters that could not be mapped within the source asset scope."
                : null));

        // EF source mappings
        var efCopied = 0;
        foreach (var src in sourceInputs)
        {
            var newInputId = inputIdMap[src.InputParameterId];
            efCopied += await CopyEfSourceMappingsAsync(conn, tx, src.InputParameterId, newInputId, request, cancellationToken);
        }

        results.Add(new ModuleResult("ef_source_mapping", efCopied, 0, "Completed", null));

        // Throughput equations + scalars
        var eqCopied = 0;
        var scalarCopied = 0;

        foreach (var src in sourceInputs)
        {
            var newInputId = inputIdMap[src.InputParameterId];
            var eqs = await ReadThroughputEquationsAsync(conn, tx, src.InputParameterId, cancellationToken);

            foreach (var eq in eqs)
            {
                var newEqId = await InsertThroughputEquationAsync(conn, tx, newInputId, eq, request, cancellationToken);
                eqCopied++;

                scalarCopied += await CopyThroughputScalarsAsync(conn, tx, eq.ThroughputEquationId, newEqId, request, cancellationToken);
            }
        }

        results.Add(new ModuleResult("throughput_equation", eqCopied, 0, "Completed", null));
        results.Add(new ModuleResult("throughput_scalar", scalarCopied, 0, "Completed", null));

        // Data input values
        var valCopied = 0;
        foreach (var src in sourceInputs)
        {
            var newInputId = inputIdMap[src.InputParameterId];
            valCopied += await CopyDataInputValuesAsync(conn, tx, src.InputParameterId, newInputId, request, cancellationToken);
        }

        results.Add(new ModuleResult("data_input_value", valCopied, 0, "Completed", null));

        // Replication result status/detail (BRD §6.13)
        // - Coding scheme is not evidenced, so we use conservative free-form:
        //   - Completed if all modules completed with zero skips
        //   - Partial if any module recorded skipped rows
        var anySkipped = results.Any(r => r.SkippedCount > 0);
        var status = anySkipped ? "Partial" : "Completed";

        string? detail = null;
        if (anySkipped)
        {
            var skippedSummaries = results
                .Where(r => r.SkippedCount > 0)
                .Select(r => $"{r.Name}: skipped={r.SkippedCount}")
                .ToList();

            detail = $"One or more modules had skipped rows: {string.Join("; ", skippedSummaries)}";
            logger.LogWarning(
                "Asset copy resulted in Partial replication. source_asset_id={SourceAssetId}, target_asset_id={TargetAssetId}. detail={Detail}",
                sourceAssetId,
                targetAssetId,
                detail);
        }

        return new InputTreeCopyResult(results, status, detail);
    }

    private static async Task<AssetCopyLineageDto> CreateLineageAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        CopyAssetRequest request,
        long sourceAssetId,
        long targetAssetId,
        string replicationResultStatus,
        string? replicationResultDetail,
        CancellationToken cancellationToken)
    {
        // copy_timestamp_utc must be UTC; DateTimeOffset.UtcNow is used.
        var copyTimestampUtc = DateTimeOffset.UtcNow;

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
                @actor,
                now(),
                @actor,
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

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        AddParam(cmd, "copy_operation_id", request.CopyOperationId);
        AddParam(cmd, "source_asset_id", sourceAssetId);
        AddParam(cmd, "target_asset_id", targetAssetId);
        AddParam(cmd, "copy_timestamp_utc", copyTimestampUtc);
        AddParam(cmd, "copy_performed_by", request.CopyPerformedBy);
        AddParam(cmd, "replication_result_status", replicationResultStatus);
        AddParam(cmd, "replication_result_detail", (object?)replicationResultDetail ?? DBNull.Value);
        AddParam(cmd, "actor", request.CopyPerformedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Create asset_copy_lineage failed: INSERT returned no row.");
        }

        return ReadLineage(reader);
    }

    private sealed record SourceInputParameter(
        long InputParameterId,
        string InputParameterName,
        long? UomId,
        long? ReportingProgramId,
        string InputType,
        string DataEntryFrequency,
        string? FuelMapping,
        bool InUseFlag);

    private sealed record SourceParentInputMapping(long ChildInputParameterId, long ParentInputParameterId);

    private sealed record SourceThroughputEquation(
        long ThroughputEquationId,
        long MasterEquationId,
        string GeneratedEquation,
        int ReportingYear);

    private static async Task<List<SourceInputParameter>> ReadInputParametersAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long assetId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                input_parameter_id,
                input_parameter_name,
                uom_id,
                reporting_program_id,
                input_type,
                data_entry_frequency,
                fuel_mapping,
                in_use_flag
            FROM input_parameter
            WHERE asset_id = @asset_id
              AND is_deleted = FALSE
            ORDER BY input_parameter_id ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        AddParam(cmd, "asset_id", assetId);

        var list = new List<SourceInputParameter>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SourceInputParameter(
                InputParameterId: reader.GetInt64(reader.GetOrdinal("input_parameter_id")),
                InputParameterName: reader.GetString(reader.GetOrdinal("input_parameter_name")),
                UomId: reader.IsDBNull(reader.GetOrdinal("uom_id")) ? null : reader.GetInt64(reader.GetOrdinal("uom_id")),
                ReportingProgramId: reader.IsDBNull(reader.GetOrdinal("reporting_program_id")) ? null : reader.GetInt64(reader.GetOrdinal("reporting_program_id")),
                InputType: reader.GetString(reader.GetOrdinal("input_type")),
                DataEntryFrequency: reader.GetString(reader.GetOrdinal("data_entry_frequency")),
                FuelMapping: reader.IsDBNull(reader.GetOrdinal("fuel_mapping")) ? null : reader.GetString(reader.GetOrdinal("fuel_mapping")),
                InUseFlag: reader.GetBoolean(reader.GetOrdinal("in_use_flag"))));
        }

        return list;
    }

    private static async Task<long> InsertInputParameterAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long targetAssetId,
        SourceInputParameter src,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING input_parameter_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        AddParam(cmd, "asset_id", targetAssetId);
        AddParam(cmd, "input_parameter_name", src.InputParameterName);
        AddParam(cmd, "uom_id", src.UomId.HasValue ? src.UomId.Value : DBNull.Value);
        AddParam(cmd, "reporting_program_id", src.ReportingProgramId.HasValue ? src.ReportingProgramId.Value : DBNull.Value);
        AddParam(cmd, "input_type", src.InputType);
        AddParam(cmd, "data_entry_frequency", src.DataEntryFrequency);
        AddParam(cmd, "fuel_mapping", (object?)src.FuelMapping ?? DBNull.Value);
        AddParam(cmd, "in_use_flag", src.InUseFlag);
        AddParam(cmd, "actor", request.CopyPerformedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        if (scalar is null)
        {
            throw new InvalidOperationException("Copy input_parameter failed: INSERT returned no id.");
        }

        return Convert.ToInt64(scalar);
    }

    private static async Task<List<SourceParentInputMapping>> ReadParentInputMappingsForAssetAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceAssetId,
        CancellationToken cancellationToken)
    {
        // Only mappings where both child and parent belong to the asset scope.
        const string sql = """
            SELECT
                pim.child_input_parameter_id,
                pim.parent_input_parameter_id
            FROM parent_input_mapping pim
            INNER JOIN input_parameter c
                ON c.input_parameter_id = pim.child_input_parameter_id
               AND c.asset_id = @asset_id
               AND c.is_deleted = FALSE
            INNER JOIN input_parameter p
                ON p.input_parameter_id = pim.parent_input_parameter_id
               AND p.asset_id = @asset_id
               AND p.is_deleted = FALSE
            WHERE pim.is_deleted = FALSE
            ORDER BY pim.parent_input_mapping_id ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        AddParam(cmd, "asset_id", sourceAssetId);

        var list = new List<SourceParentInputMapping>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SourceParentInputMapping(
                ChildInputParameterId: reader.GetInt64(reader.GetOrdinal("child_input_parameter_id")),
                ParentInputParameterId: reader.GetInt64(reader.GetOrdinal("parent_input_parameter_id"))));
        }

        return list;
    }

    private static async Task InsertParentInputMappingAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long newChildInputParameterId,
        long newParentInputParameterId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
                @child_id,
                @parent_id,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            );
            """;

        await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["child_id"] = newChildInputParameterId,
                ["parent_id"] = newParentInputParameterId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);
    }

    private static async Task<int> CopyEfSourceMappingsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceInputParameterId,
        long targetInputParameterId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
            SELECT
                @target_input_parameter_id,
                ef_source_set_or_table,
                equation_setup,
                scalar_values,
                reporting_program_id,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            FROM ef_source_mapping
            WHERE input_parameter_id = @source_input_parameter_id
              AND is_deleted = FALSE;
            """;

        return await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["target_input_parameter_id"] = targetInputParameterId,
                ["source_input_parameter_id"] = sourceInputParameterId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);
    }

    private static async Task<List<SourceThroughputEquation>> ReadThroughputEquationsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long inputParameterId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                throughput_equation_id,
                master_equation_id,
                generated_equation,
                reporting_year
            FROM throughput_equation
            WHERE input_parameter_id = @input_parameter_id
              AND is_deleted = FALSE
            ORDER BY throughput_equation_id ASC;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        AddParam(cmd, "input_parameter_id", inputParameterId);

        var list = new List<SourceThroughputEquation>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SourceThroughputEquation(
                ThroughputEquationId: reader.GetInt64(reader.GetOrdinal("throughput_equation_id")),
                MasterEquationId: reader.GetInt64(reader.GetOrdinal("master_equation_id")),
                GeneratedEquation: reader.GetString(reader.GetOrdinal("generated_equation")),
                ReportingYear: reader.GetInt32(reader.GetOrdinal("reporting_year"))));
        }

        return list;
    }

    private static async Task<long> InsertThroughputEquationAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long targetInputParameterId,
        SourceThroughputEquation src,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            )
            RETURNING throughput_equation_id;
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        AddParam(cmd, "input_parameter_id", targetInputParameterId);
        AddParam(cmd, "master_equation_id", src.MasterEquationId);
        AddParam(cmd, "generated_equation", src.GeneratedEquation);
        AddParam(cmd, "reporting_year", src.ReportingYear);
        AddParam(cmd, "actor", request.CopyPerformedBy);
        AddParam(cmd, "correlation_id", request.CorrelationId);

        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        if (scalar is null)
        {
            throw new InvalidOperationException("Copy throughput_equation failed: INSERT returned no id.");
        }

        return Convert.ToInt64(scalar);
    }

    private static async Task<int> CopyThroughputScalarsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceThroughputEquationId,
        long targetThroughputEquationId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
            SELECT
                @target_throughput_equation_id,
                scalar_type,
                scalar_table,
                scalar_id,
                scalar_value,
                scalar_basis,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            FROM throughput_scalar
            WHERE throughput_equation_id = @source_throughput_equation_id
              AND is_deleted = FALSE;
            """;

        return await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["target_throughput_equation_id"] = targetThroughputEquationId,
                ["source_throughput_equation_id"] = sourceThroughputEquationId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);
    }

    private static async Task<int> CopyDataInputValuesAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        long sourceInputParameterId,
        long targetInputParameterId,
        CopyAssetRequest request,
        CancellationToken cancellationToken)
    {
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
            SELECT
                @target_input_parameter_id,
                input_parameter_value,
                reporting_year,
                reporting_period,
                calculated_throughput_output,
                @actor,
                now(),
                @actor,
                now(),
                FALSE,
                @correlation_id
            FROM data_input_value
            WHERE input_parameter_id = @source_input_parameter_id
              AND is_deleted = FALSE;
            """;

        return await ExecuteNonQueryAsync(
            conn,
            tx,
            sql,
            new Dictionary<string, object>
            {
                ["target_input_parameter_id"] = targetInputParameterId,
                ["source_input_parameter_id"] = sourceInputParameterId,
                ["actor"] = request.CopyPerformedBy,
                ["correlation_id"] = request.CorrelationId,
            },
            cancellationToken);
    }

    private static async Task<int> ExecuteNonQueryAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string sql,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var p in parameters)
        {
            AddParam(cmd, p.Key, p.Value);
        }

        return await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ScalarExistsAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string sql,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var p in parameters)
        {
            AddParam(cmd, p.Key, p.Value);
        }

        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value is not null;
    }

    private static void AddParam(NpgsqlCommand cmd, string name, object value)
    {
        _ = cmd.Parameters.AddWithValue(name, value);
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

    private static AssetCopyLineageDto ReadLineage(NpgsqlDataReader r)
    {
        return new AssetCopyLineageDto(
            AssetCopyLineageId: r.GetInt64(r.GetOrdinal("asset_copy_lineage_id")),
            CopyOperationId: r.GetString(r.GetOrdinal("copy_operation_id")),
            SourceAssetId: r.GetInt64(r.GetOrdinal("source_asset_id")),
            TargetAssetId: r.GetInt64(r.GetOrdinal("target_asset_id")),
            CopyTimestampUtc: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("copy_timestamp_utc")),
            CopyPerformedBy: r.GetString(r.GetOrdinal("copy_performed_by")),
            ReplicationResultStatus: r.GetString(r.GetOrdinal("replication_result_status")),
            ReplicationResultDetail: r.IsDBNull(r.GetOrdinal("replication_result_detail")) ? null : r.GetString(r.GetOrdinal("replication_result_detail")),
            CreatedBy: r.GetString(r.GetOrdinal("created_by")),
            CreatedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("created_at")),
            ModifiedBy: r.GetString(r.GetOrdinal("modified_by")),
            ModifiedAt: r.GetFieldValue<DateTimeOffset>(r.GetOrdinal("modified_at")),
            IsDeleted: r.GetBoolean(r.GetOrdinal("is_deleted")),
            CorrelationId: r.GetString(r.GetOrdinal("correlation_id")));
    }
}
