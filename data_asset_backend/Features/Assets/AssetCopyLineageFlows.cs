using Microsoft.Extensions.Logging;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Flows for BRD-evidenced copy lineage capture (BRD §6.13; table: asset_copy_lineage).
/// </summary>
public static class AssetCopyLineageFlows
{
    /// <summary>
    /// Thrown when a query request is missing all supported filters (conservative semantics).
    /// </summary>
    public sealed class MissingFiltersException : Exception
    {
        /// <summary>
        /// Initializes a new instance of <see cref="MissingFiltersException"/>.
        /// </summary>
        public MissingFiltersException()
            : base("At least one filter must be provided: copyOperationId, sourceAssetId, or targetAssetId.")
        {
        }
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates a copy lineage record after validating referenced assets exist (excluding soft-deleted).
    /// </summary>
    /// <remarks>
    /// Validation is conservative and evidence-based:
    /// - source_asset_id and target_asset_id are foreign keys to asset(asset_id) in the V2 schema.
    /// - We check existence via <see cref="AssetRepository.AssetExistsAsync"/> to return a clean 404 (instead of relying on FK error).
    /// </remarks>
    public static async Task<AssetCopyLineageDto> CreateAsync(
        CreateAssetCopyLineageRequest request,
        AssetCopyLineageRepository lineageRepository,
        AssetRepository assetRepository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetCopyLineageFlows.Create starting. copy_operation_id={CopyOperationId}, source_asset_id={SourceAssetId}, target_asset_id={TargetAssetId}",
            request.CopyOperationId,
            request.SourceAssetId,
            request.TargetAssetId);

        if (!await assetRepository.AssetExistsAsync(request.SourceAssetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", request.SourceAssetId);
        }

        if (!await assetRepository.AssetExistsAsync(request.TargetAssetId, cancellationToken))
        {
            throw new AssetRepository.EntityNotFoundException("asset", request.TargetAssetId);
        }

        var created = await lineageRepository.CreateAsync(request, cancellationToken);

        logger.LogInformation(
            "AssetCopyLineageFlows.Create completed. asset_copy_lineage_id={Id}",
            created.AssetCopyLineageId);

        return created;
    }

    // PUBLIC_INTERFACE
    /// <summary>
    /// Queries copy lineage records by supported filters (copyOperationId, sourceAssetId, targetAssetId).
    /// Multiple filters are ANDed. Requires at least one filter to avoid unbounded queries.
    /// </summary>
    public static async Task<IReadOnlyList<AssetCopyLineageDto>> QueryAsync(
        QueryAssetCopyLineageRequest request,
        AssetCopyLineageRepository lineageRepository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var hasAnyFilter =
            !string.IsNullOrWhiteSpace(request.CopyOperationId) ||
            request.SourceAssetId.HasValue ||
            request.TargetAssetId.HasValue;

        if (!hasAnyFilter)
        {
            throw new MissingFiltersException();
        }

        logger.LogInformation(
            "AssetCopyLineageFlows.Query starting. copy_operation_id={CopyOperationId}, source_asset_id={SourceAssetId}, target_asset_id={TargetAssetId}, limit={Limit}",
            request.CopyOperationId,
            request.SourceAssetId,
            request.TargetAssetId,
            request.Limit);

        var results = await lineageRepository.QueryAsync(request, cancellationToken);

        logger.LogInformation("AssetCopyLineageFlows.Query completed. count={Count}", results.Count);
        return results;
    }
}
