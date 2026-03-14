using Microsoft.Extensions.Logging;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Flow/orchestration for BRD FR-03 "Copy Asset" end-to-end workflow.
/// </summary>
public static class AssetCopyFlows
{
    /// <summary>
    /// Flow request wrapper for Copy Asset.
/// </summary>
    public sealed record CopyAssetFlowRequest(long SourceAssetId, CopyAssetRequest Request);

    /// <summary>
    /// Flow response wrapper for Copy Asset.
/// </summary>
    public sealed record CopyAssetFlowResponse(CopyAssetResponse Response);

    // PUBLIC_INTERFACE
    /// <summary>
    /// Copies an asset and BRD-evidenced related records into a new target asset, recording lineage in <c>asset_copy_lineage</c>.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs:
    ///   - sourceAssetId: existing non-deleted asset ID
    ///   - request: CopyAssetRequest (BRD §6.13 + audit/trace fields)
    /// - Output:
    ///   - CopyAssetResponse (target asset + lineage + per-module results)
    /// - Errors:
    ///   - AssetRepository.EntityNotFoundException -> caller maps to 404
    ///   - InvalidOperationException -> caller maps to 503 when DB not configured (existing pattern)
    ///   - Npgsql.PostgresException -> caller maps to 409 conflict (existing pattern)
    /// - Side effects:
    ///   - Inserts new asset
    ///   - Copies related BRD-evidenced records
    ///   - Writes lineage record
    /// Observability:
    ///   - Logs start/end with source/target IDs and copy operation ID.
    /// </remarks>
    public static async Task<CopyAssetFlowResponse> CopyAssetAsync(
        CopyAssetFlowRequest flowRequest,
        AssetCopyRepository copyRepository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetCopyFlows.CopyAsset starting. source_asset_id={SourceAssetId}, copy_operation_id={CopyOperationId}",
            flowRequest.SourceAssetId,
            flowRequest.Request.CopyOperationId);

        var result = await copyRepository.CopyAssetAsync(
            flowRequest.SourceAssetId,
            flowRequest.Request,
            logger,
            cancellationToken);

        logger.LogInformation(
            "AssetCopyFlows.CopyAsset completed. source_asset_id={SourceAssetId}, target_asset_id={TargetAssetId}, copy_operation_id={CopyOperationId}, replication_result_status={ReplicationResultStatus}",
            flowRequest.SourceAssetId,
            result.Response.TargetAsset.AssetId,
            flowRequest.Request.CopyOperationId,
            result.Response.ReplicationResultStatus);

        return new CopyAssetFlowResponse(result.Response);
    }
}
