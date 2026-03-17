using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Legacy/simplified copy request payload used by earlier clients.
/// </summary>
/// <remarks>
/// PUBLIC_INTERFACE
/// This request shape exists for backward compatibility with older frontend/test flows that
/// only provided a minimal copy intent (target name/site + idempotency key).
///
/// IMPORTANT:
/// The current BRD-backed copy flow requires a new <c>global_unique_asset_id</c> for the target asset.
/// This legacy request does not provide it, so the server must generate a fresh unique value to avoid
/// violating <c>uq_asset_global_unique_asset_id</c>.
/// </remarks>
public sealed class LegacyCopyAssetRequest
{
    /// <summary>
    /// Target site for the copied asset.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string TargetSiteId { get; set; } = string.Empty;

    /// <summary>
    /// Target asset name for the copied asset.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string TargetAssetName { get; set; } = string.Empty;

    /// <summary>
    /// Optional idempotency key (legacy field). If provided, callers should also set the header
    /// <c>X-Idempotency-Key</c>; this field is retained for compatibility only.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Utilities for mapping legacy copy requests into the BRD V2 CopyAssetRequest.
/// </summary>
public static class LegacyCopyAssetMapper
{
    // PUBLIC_INTERFACE
    /// <summary>
    /// Maps a legacy request into the canonical <see cref="CopyAssetRequest"/> by pulling source fields as needed
    /// and generating a new globally unique asset ID for the target.
    /// </summary>
    /// <param name="legacy">Legacy request.</param>
    /// <param name="source">Source asset (used as a template for header fields not provided by legacy request).</param>
    /// <param name="actor">User performing copy (audit).</param>
    /// <param name="correlationId">Correlation id (traceability).</param>
    /// <returns>Canonical copy request suitable for the AssetCopyFlows pipeline.</returns>
    public static CopyAssetRequest ToCanonical(
        LegacyCopyAssetRequest legacy,
        AssetDto source,
        string actor,
        string correlationId)
    {
        // Ensure uniqueness: generate a new global id for every copy operation.
        // Using GUID avoids collisions without needing a DB roundtrip.
        var newGlobalId = $"guaid-{Guid.NewGuid():N}";

        return new CopyAssetRequest
        {
            CopyOperationId = $"op-{Guid.NewGuid():N}",
            CopyPerformedBy = actor,
            CorrelationId = correlationId,
            TargetAsset = new CopyAssetTargetAsset
            {
                SiteId = legacy.TargetSiteId,
                AssetGroup = source.AssetGroup,
                ProcessGroup = source.ProcessGroup,
                ProcessGroupOtherText = source.ProcessGroupOtherText,
                AssetName = legacy.TargetAssetName,
                PermitEuId = source.PermitEuId,
                GlobalUniqueAssetId = newGlobalId,
                AssetDescription = source.AssetDescription,
                StationaryFlag = source.StationaryFlag,
                ParentPseudoAssetId = source.ParentPseudoAssetId
            }
        };
    }
}
