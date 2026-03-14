using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Features.Assets;

//
// NOTE (NO ASSUMPTIONS):
// These models are shaped strictly from the Flyway V2 BRD-evidenced table/columns in:
// - db/migrations/V2__brd_asset_configuration_schema.sql
// - table: asset_copy_lineage (BRD §6.13)
//

// ------------------------------------------------------------
// Asset Copy Lineage (table: asset_copy_lineage)
// ------------------------------------------------------------

/// <summary>
/// Persisted copy lineage record (maps to Flyway V2 table: <c>asset_copy_lineage</c>).
/// </summary>
public sealed record AssetCopyLineageDto(
    long AssetCopyLineageId,
    string CopyOperationId,
    long SourceAssetId,
    long TargetAssetId,
    DateTimeOffset CopyTimestampUtc,
    string CopyPerformedBy,
    string ReplicationResultStatus,
    string? ReplicationResultDetail,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating a copy lineage record (BRD §6.13).
/// </summary>
public sealed class CreateAssetCopyLineageRequest
{
    [Required(AllowEmptyStrings = false)]
    public string CopyOperationId { get; set; } = string.Empty;

    [Required]
    public long SourceAssetId { get; set; }

    [Required]
    public long TargetAssetId { get; set; }

    /// <summary>
    /// UTC timestamp when the copy occurred (BRD §6.13). Stored as TIMESTAMPTZ.
    /// </summary>
    [Required]
    public DateTimeOffset CopyTimestampUtc { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CopyPerformedBy { get; set; } = string.Empty;

    /// <summary>
    /// BRD §6.13: Completed/Partial/Failed (exact coding scheme NOT EVIDENCED; stored as free-form text).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ReplicationResultStatus { get; set; } = string.Empty;

    /// <summary>
    /// Optional detail. BRD mentions reason code / impacted module list, but structure is NOT EVIDENCED.
    /// Persisted as free-form text.
    /// </summary>
    public string? ReplicationResultDetail { get; set; }

    // BRD §6.11 audit/trace fields (required)
    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Query parameters for retrieving lineage records. Filters are ANDed when multiple are provided.
/// </summary>
public sealed class QueryAssetCopyLineageRequest
{
    /// <summary>
    /// Filter by copy operation ID (BRD §6.13).
    /// </summary>
    public string? CopyOperationId { get; set; }

    /// <summary>
    /// Filter by source asset ID (BRD §6.13).
    /// </summary>
    public long? SourceAssetId { get; set; }

    /// <summary>
    /// Filter by target asset ID (BRD §6.13).
    /// </summary>
    public long? TargetAssetId { get; set; }

    /// <summary>
    /// Max results to return (default 100, max 500).
    /// </summary>
    public int? Limit { get; set; }
}
