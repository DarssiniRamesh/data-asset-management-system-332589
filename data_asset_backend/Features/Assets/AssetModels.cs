using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Features.Assets;

/// <summary>
/// Persisted asset record (maps to Flyway V2 table: <c>asset</c>).
/// </summary>
public sealed record AssetDto(
    long AssetId,
    string SiteId,
    string AssetGroup,
    string ProcessGroup,
    string? ProcessGroupOtherText,
    string AssetName,
    string PermitEuId,
    string GlobalUniqueAssetId,
    string? AssetDescription,
    bool? StationaryFlag,
    long? ParentPseudoAssetId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating an asset.
/// </summary>
public sealed class CreateAssetRequest : IValidatableObject
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

    // BRD §6.11 audit/trace fields (required)
    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // BRD §6.1: Process Group - If "Other", free-text process group must be captured (Conditional).
        if (string.Equals(ProcessGroup?.Trim(), "Other", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(ProcessGroupOtherText))
        {
            yield return new ValidationResult(
                "processGroupOtherText is required when processGroup is 'Other'.",
                new[] { nameof(ProcessGroupOtherText) });
        }
    }
}

/// <summary>
/// Request payload for updating an asset (BRD: Global Unique Asset ID is immutable once created).
/// </summary>
public sealed class UpdateAssetRequest : IValidatableObject
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

    public string? AssetDescription { get; set; }

    public bool? StationaryFlag { get; set; }

    public long? ParentPseudoAssetId { get; set; }

    // BRD §6.11 audit/trace fields (required)
    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // BRD §6.1: Process Group - If "Other", free-text process group must be captured (Conditional).
        if (string.Equals(ProcessGroup?.Trim(), "Other", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(ProcessGroupOtherText))
        {
            yield return new ValidationResult(
                "processGroupOtherText is required when processGroup is 'Other'.",
                new[] { nameof(ProcessGroupOtherText) });
        }
    }
}

/// <summary>
/// Request payload for deleting (soft-deleting) an asset.
/// </summary>
/// <remarks>
/// This uses the existing V2 schema convention of <c>is_deleted</c> soft deletes (BRD §6.11 audit/trace fields).
/// </remarks>
public sealed class DeleteAssetRequest
{
    // BRD §6.11 audit/trace fields (required)
    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Query parameters for searching assets.
/// All filters are optional; when multiple filters are provided, they are ANDed.
/// </summary>
public sealed class QueryAssetsRequest
{
    public string? SiteId { get; set; }
    public string? AssetGroup { get; set; }
    public string? ProcessGroup { get; set; }
    public string? AssetNameContains { get; set; }
    public string? PermitEuId { get; set; }
    public string? GlobalUniqueAssetId { get; set; }

    /// <summary>
    /// Max results to return (default 100, max 500).
    /// </summary>
    public int? Limit { get; set; }
}
