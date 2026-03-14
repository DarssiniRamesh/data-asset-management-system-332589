using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Request payload for the BRD FR-03 "Copy Asset" workflow (BRD §6.13 Copy and Lineage Capture Requirements).
/// </summary>
/// <remarks>
/// Contract (evidence-based; no non-BRD assumptions):
/// - Inputs:
///   - copyOperationId: required (BRD §6.13)
///   - copyPerformedBy: required (BRD §6.13)
///   - correlationId: required audit/trace field (BRD §6.11)
///   - targetAsset: required. The BRD requires Global Unique Asset ID to be immutable/unique. We therefore require the
///     full set of BRD §6.1 header fields for the new asset, including a new Global Unique Asset ID.
/// - Outputs:
///   - targetAsset: created asset row
///   - lineage: the created asset_copy_lineage row
///   - replicationResultStatus/detail: free-form strings persisted to asset_copy_lineage per V2 schema notes.
/// - Errors:
///   - 404 when source asset not found
///   - 409 on DB constraint conflict (e.g., duplicate global_unique_asset_id)
///   - 503 when DB not configured
/// - Side effects:
///   - Inserts a new asset row
///   - Copies BRD-evidenced related records to the new asset (V2 schema tables)
///   - Inserts a lineage row into asset_copy_lineage
/// </remarks>
public sealed class CopyAssetRequest
{
    /// <summary>
    /// Copy Operation ID (BRD §6.13).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string CopyOperationId { get; set; } = string.Empty;

    /// <summary>
    /// Actor who performed the copy (BRD §6.13).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string CopyPerformedBy { get; set; } = string.Empty;

    /// <summary>
    /// Correlation ID for traceability (BRD §6.11).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Target asset header fields (BRD §6.1). This is the "new" asset to be created as a copy target.
    /// </summary>
    [Required]
    public CopyAssetTargetAsset TargetAsset { get; set; } = new();
}

/// <summary>
/// Target asset header fields required to create the copy target (BRD §6.1).
/// </summary>
public sealed class CopyAssetTargetAsset
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AssetGroup { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ProcessGroup { get; set; } = string.Empty;

    public string? ProcessGroupOtherText { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string AssetName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string PermitEuId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string GlobalUniqueAssetId { get; set; } = string.Empty;

    public string? AssetDescription { get; set; }

    public bool? StationaryFlag { get; set; }

    public long? ParentPseudoAssetId { get; set; }
}

/// <summary>
/// Response payload for the copy asset workflow.
/// </summary>
public sealed record CopyAssetResponse(
    AssetDto TargetAsset,
    AssetCopyLineageDto Lineage,
    string ReplicationResultStatus,
    string? ReplicationResultDetail,
    IReadOnlyList<CopyAssetModuleReplicationResult> ModuleResults);

/// <summary>
/// Per-module replication outcome (supports BRD §6.13 "impacted module list" requirement).
/// </summary>
public sealed record CopyAssetModuleReplicationResult(
    string ModuleName,
    int CopiedCount,
    int SkippedCount,
    string Status,
    string? Detail);
