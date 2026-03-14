using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Features.Masters;

/// <summary>
/// UOM Master record (maps to Flyway V2 table: <c>uom_master</c>).
/// </summary>
public sealed record UomMasterDto(
    long UomId,
    string UomKey,
    string DisplayLabel,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating a UOM master row.
/// </summary>
public sealed class CreateUomMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string UomKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DisplayLabel { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // BRD §6.11 audit/trace fields (required)
    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating a UOM master row.
/// </summary>
public sealed class UpdateUomMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string UomKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DisplayLabel { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // BRD §6.11 audit/trace fields (required)
    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Reporting Program Master record (maps to Flyway V2 table: <c>reporting_program_master</c>).
/// </summary>
public sealed record ReportingProgramMasterDto(
    long ReportingProgramId,
    string ProgramKey,
    string DisplayLabel,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateReportingProgramMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string ProgramKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DisplayLabel { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateReportingProgramMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string ProgramKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DisplayLabel { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Control Device Master record (maps to Flyway V2 table: <c>control_device_master</c>).
/// </summary>
public sealed record ControlDeviceMasterDto(
    long ControlDeviceId,
    string SiteId,
    string DeviceKey,
    string DisplayLabel,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateControlDeviceMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DeviceKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DisplayLabel { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateControlDeviceMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DeviceKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DisplayLabel { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Equation Master record (maps to Flyway V2 table: <c>equation_master</c>).
/// </summary>
public sealed record EquationMasterDto(
    long EquationMasterId,
    string EquationKey,
    string VersionLabel,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateEquationMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string EquationKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string VersionLabel { get; set; } = string.Empty;

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateEquationMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string EquationKey { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string VersionLabel { get; set; } = string.Empty;

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Status Code Master record (maps to Flyway V2 table: <c>status_code_master</c>).
/// </summary>
public sealed record StatusCodeMasterDto(
    long StatusCodeId,
    string StatusCode,
    string BusinessMeaning,
    bool IsActive,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateStatusCodeMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string StatusCode { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string BusinessMeaning { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateStatusCodeMasterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string StatusCode { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string BusinessMeaning { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Common query parameters for master/reference lists.
/// </summary>
public sealed class QueryMasterRequest
{
    /// <summary>
    /// When true, filters to active rows only (is_active = true).
    /// </summary>
    public bool? ActiveOnly { get; set; }

    /// <summary>
    /// Max results to return (default 200, max 1000).
    /// </summary>
    public int? Limit { get; set; }
}
