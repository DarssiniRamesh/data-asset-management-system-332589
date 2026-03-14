using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Features.Section4;

/// <summary>
/// Site Profile DTO (BRD §4 "Site Profile Information").
/// Minimal fields only (no BRD-evidenced field list beyond module name).
/// </summary>
public sealed record SiteProfileDto(
    long SiteProfileId,
    string SiteId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateSiteProfileRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateSiteProfileRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// WWTS Process Stream DTO (BRD §4 conditional "Asset / WWTS Process Stream Configuration").
/// </summary>
public sealed record WwtsProcessStreamDto(
    long WwtsProcessStreamId,
    string SiteId,
    string StreamName,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateWwtsProcessStreamRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string StreamName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateWwtsProcessStreamRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string StreamName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Chemical Raw Material DTO (BRD §4 "Chemical Raw Material Configuration and Usage").
/// </summary>
public sealed record ChemicalRawMaterialDto(
    long ChemicalRawMaterialId,
    string SiteId,
    string ChemicalName,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateChemicalRawMaterialRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ChemicalName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateChemicalRawMaterialRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ChemicalName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Chemical SDS DTO (BRD §4 "Chemical SDS Details Configuration").
/// </summary>
public sealed record ChemicalSdsDto(
    long ChemicalSdsId,
    string SiteId,
    string ChemicalName,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateChemicalSdsRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ChemicalName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateChemicalSdsRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ChemicalName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Lab Data Configuration DTO (BRD §4 conditional "Lab Data Configuration").
/// </summary>
public sealed record LabDataConfigurationDto(
    long LabDataConfigurationId,
    string SiteId,
    string ConfigurationName,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateLabDataConfigurationRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ConfigurationName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateLabDataConfigurationRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ConfigurationName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Water Process Configuration DTO (BRD §4 conditional "WWTS / Water Process related screens").
/// </summary>
public sealed record WaterProcessConfigurationDto(
    long WaterProcessConfigurationId,
    string SiteId,
    string ConfigurationName,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

public sealed class CreateWaterProcessConfigurationRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ConfigurationName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

public sealed class UpdateWaterProcessConfigurationRequest
{
    [Required(AllowEmptyStrings = false)]
    public string SiteId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ConfigurationName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}
