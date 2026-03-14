using Microsoft.Extensions.Logging;
using Npgsql;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Flow for asset create/read/update/query operations (single canonical orchestration layer).
/// </summary>
public static class AssetFlows
{
    /// <summary>
    /// Thrown when an attempt is made to create/update an asset with a Permit EU ID that already exists.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Mapped to HTTP 409 Conflict by the unified API exception handler.
    /// - Message is safe to show to clients (no secrets).
    /// </remarks>
    public sealed class DuplicatePermitEuIdException : Exception
    {
        /// <summary>
        /// Initializes a new instance of <see cref="DuplicatePermitEuIdException"/>.
        /// </summary>
        public DuplicatePermitEuIdException(string permitEuId)
            : base($"Permit EU ID must be unique. A non-deleted asset already exists with permitEuId='{permitEuId}'.")
        {
            PermitEuId = permitEuId;
        }

        /// <summary>
        /// The conflicting Permit EU ID (as provided by the caller).
        /// </summary>
        public string PermitEuId { get; }
    }

    /// <summary>
    /// Request for the create asset flow.
    /// </summary>
    public sealed record CreateAssetFlowRequest(CreateAssetRequest Payload);

    /// <summary>
    /// Result for the create asset flow.
    /// </summary>
    public sealed record CreateAssetFlowResult(AssetDto Asset);

    // PUBLIC_INTERFACE
    /// <summary>
    /// Creates an asset using the Flyway V2 schema.
    /// </summary>
    /// <remarks>
    /// Contract:
    /// - Inputs: required asset header fields + audit fields in <see cref="CreateAssetRequest"/>
    /// - Outputs: created <see cref="AssetDto"/> including DB-generated ID and timestamps
    /// - Errors:
    ///   - may throw <see cref="InvalidOperationException"/> when DB is not configured
    ///   - may throw <see cref="PostgresException"/> (e.g., unique constraint violation on global_unique_asset_id)
    /// - Side effects: inserts a row into <c>asset</c>
    /// </remarks>
    public static async Task<CreateAssetFlowResult> CreateAssetAsync(
        CreateAssetFlowRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetFlows.CreateAsset starting. site_id={SiteId}, global_unique_asset_id={GlobalUniqueAssetId}",
            request.Payload.SiteId,
            request.Payload.GlobalUniqueAssetId);

        // Permit EU ID uniqueness (BRD mentions uniqueness but does not evidence scope).
        // Conservative enforcement: disallow duplicates across all non-deleted assets.
        var permitEuIdNormalized = request.Payload.PermitEuId.Trim();
        if (await repository.PermitEuIdExistsAsync(permitEuIdNormalized, excludeAssetId: null, cancellationToken))
        {
            logger.LogWarning(
                "AssetFlows.CreateAsset rejected due to duplicate permit_eu_id. permit_eu_id={PermitEuId}",
                permitEuIdNormalized);

            throw new DuplicatePermitEuIdException(permitEuIdNormalized);
        }

        var created = await repository.CreateAsync(request.Payload, cancellationToken);

        logger.LogInformation(
            "AssetFlows.CreateAsset completed. asset_id={AssetId}",
            created.AssetId);

        return new CreateAssetFlowResult(created);
    }

    /// <summary>
    /// Result for get-by-id.
    /// </summary>
    public sealed record GetAssetFlowResult(AssetDto? Asset);

    // PUBLIC_INTERFACE
    /// <summary>
    /// Fetches an asset by ID (excluding soft-deleted).
    /// </summary>
    public static async Task<GetAssetFlowResult> GetAssetAsync(
        long assetId,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetFlows.GetAsset starting. asset_id={AssetId}", assetId);

        var asset = await repository.GetByIdAsync(assetId, cancellationToken);

        logger.LogInformation(
            "AssetFlows.GetAsset completed. asset_id={AssetId}, found={Found}",
            assetId,
            asset is not null);

        return new GetAssetFlowResult(asset);
    }

    /// <summary>
    /// Request for update asset.
    /// </summary>
    public sealed record UpdateAssetFlowRequest(long AssetId, UpdateAssetRequest Payload);

    /// <summary>
    /// Result for update asset.
    /// </summary>
    public sealed record UpdateAssetFlowResult(AssetDto? Asset);

    // PUBLIC_INTERFACE
    /// <summary>
    /// Updates an asset by ID (excluding soft-deleted).
    /// </summary>
    /// <remarks>
    /// Invariant:
    /// - BRD §6.1: Global Unique Asset ID is immutable once created. This API does not accept changing it.
    /// </remarks>
    public static async Task<UpdateAssetFlowResult> UpdateAssetAsync(
        UpdateAssetFlowRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetFlows.UpdateAsset starting. asset_id={AssetId}", request.AssetId);

        // Permit EU ID uniqueness (BRD mentions uniqueness but does not evidence scope).
        // Conservative enforcement: disallow duplicates across all non-deleted assets, excluding this asset id.
        var permitEuIdNormalized = request.Payload.PermitEuId.Trim();
        if (await repository.PermitEuIdExistsAsync(permitEuIdNormalized, excludeAssetId: request.AssetId, cancellationToken))
        {
            logger.LogWarning(
                "AssetFlows.UpdateAsset rejected due to duplicate permit_eu_id. asset_id={AssetId}, permit_eu_id={PermitEuId}",
                request.AssetId,
                permitEuIdNormalized);

            throw new DuplicatePermitEuIdException(permitEuIdNormalized);
        }

        var updated = await repository.UpdateAsync(request.AssetId, request.Payload, cancellationToken);

        logger.LogInformation(
            "AssetFlows.UpdateAsset completed. asset_id={AssetId}, found={Found}",
            request.AssetId,
            updated is not null);

        return new UpdateAssetFlowResult(updated);
    }

    /// <summary>
    /// Result for query assets.
    /// </summary>
    public sealed record QueryAssetsFlowResult(IReadOnlyList<AssetDto> Assets);

    // PUBLIC_INTERFACE
    /// <summary>
    /// Queries assets with optional filters.
    /// </summary>
    public static async Task<QueryAssetsFlowResult> QueryAssetsAsync(
        QueryAssetsRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "AssetFlows.QueryAssets starting. site_id={SiteId}, asset_group={AssetGroup}, process_group={ProcessGroup}, limit={Limit}",
            request.SiteId,
            request.AssetGroup,
            request.ProcessGroup,
            request.Limit);

        var assets = await repository.QueryAsync(request, cancellationToken);

        logger.LogInformation("AssetFlows.QueryAssets completed. count={Count}", assets.Count);

        return new QueryAssetsFlowResult(assets);
    }

    /// <summary>
    /// Request for delete asset.
    /// </summary>
    public sealed record DeleteAssetFlowRequest(long AssetId, DeleteAssetRequest Payload);

    /// <summary>
    /// Result for delete asset.
    /// </summary>
    public sealed record DeleteAssetFlowResult(bool Deleted);

    // PUBLIC_INTERFACE
    /// <summary>
    /// Soft-deletes an asset by ID using the existing <c>is_deleted</c> flag (FR-04).
    /// </summary>
    /// <remarks>
    /// Dependency-safety (BRD-aligned, evidence-based):
    /// - The V2 schema models asset dependencies via foreign keys to BRD-evidenced child tables.
    /// - This flow performs a soft-delete of the asset and soft-deletes BRD-evidenced dependent rows
    ///   in a single transaction to avoid leaving active dependent records pointing at a deleted asset.
    /// - No hard-delete is performed.
    /// </remarks>
    public static async Task<DeleteAssetFlowResult> DeleteAssetAsync(
        DeleteAssetFlowRequest request,
        AssetRepository repository,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("AssetFlows.DeleteAsset starting. asset_id={AssetId}", request.AssetId);

        var deleted = await repository.SoftDeleteAssetGraphAsync(
            request.AssetId,
            request.Payload.ModifiedBy,
            request.Payload.CorrelationId,
            cancellationToken);

        logger.LogInformation(
            "AssetFlows.DeleteAsset completed. asset_id={AssetId}, deleted={Deleted}",
            request.AssetId,
            deleted);

        return new DeleteAssetFlowResult(deleted);
    }
}
