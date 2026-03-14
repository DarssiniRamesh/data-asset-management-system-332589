using System.ComponentModel.DataAnnotations;

namespace DataAssetBackend.Features.Assets;

//
// NOTE (NO ASSUMPTIONS):
// These models are shaped strictly from the Flyway V2 BRD-evidenced tables/columns in:
// - db/migrations/V2__brd_asset_configuration_schema.sql
// BRD business rules that are not represented as columns are handled (where possible) via
// API-level validation/flow checks without inventing new persisted fields.
//

// ------------------------------------------------------------
// Asset Status Log (table: asset_status_log)
// ------------------------------------------------------------

/// <summary>
/// Persisted asset operating status log row (maps to Flyway V2 table: <c>asset_status_log</c>).
/// </summary>
public sealed record AssetStatusLogDto(
    long AssetStatusLogId,
    long AssetId,
    string OperatingStatus,
    DateOnly StatusFromDate,
    DateOnly? StatusToDate,
    string? Comments,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating an asset status log row.
/// </summary>
public sealed class CreateAssetStatusLogRequest
{
    [Required(AllowEmptyStrings = false)]
    public string OperatingStatus { get; set; } = string.Empty;

    [Required]
    public DateOnly StatusFromDate { get; set; }

    public DateOnly? StatusToDate { get; set; }

    public string? Comments { get; set; }

    // BRD §6.11 audit/trace fields (required)
    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating an asset status log row.
/// </summary>
public sealed class UpdateAssetStatusLogRequest
{
    [Required(AllowEmptyStrings = false)]
    public string OperatingStatus { get; set; } = string.Empty;

    [Required]
    public DateOnly StatusFromDate { get; set; }

    public DateOnly? StatusToDate { get; set; }

    public string? Comments { get; set; }

    // BRD §6.11 audit/trace fields (required)
    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// Additional Asset ID (table: additional_asset_id)
// ------------------------------------------------------------

/// <summary>
/// Persisted additional identifier for an asset (maps to Flyway V2 table: <c>additional_asset_id</c>).
/// </summary>
public sealed record AdditionalAssetIdDto(
    long AdditionalAssetId,
    long AssetId,
    string IdType,
    string IdValue,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating an additional asset id row.
/// </summary>
public sealed class CreateAdditionalAssetIdRequest
{
    [Required(AllowEmptyStrings = false)]
    public string IdType { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string IdValue { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating an additional asset id row.
/// </summary>
public sealed class UpdateAdditionalAssetIdRequest
{
    [Required(AllowEmptyStrings = false)]
    public string IdType { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string IdValue { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// Asset Property (table: asset_property)
// ------------------------------------------------------------

/// <summary>
/// Persisted asset property row (maps to Flyway V2 table: <c>asset_property</c>).
/// </summary>
public sealed record AssetPropertyDto(
    long AssetPropertyId,
    long AssetId,
    string PropertyName,
    string PropertyValue,
    DateOnly FromDate,
    string Notes,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating an asset property row.
/// </summary>
public sealed class CreateAssetPropertyRequest
{
    [Required(AllowEmptyStrings = false)]
    public string PropertyName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string PropertyValue { get; set; } = string.Empty;

    [Required]
    public DateOnly FromDate { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string Notes { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating an asset property row.
/// </summary>
public sealed class UpdateAssetPropertyRequest
{
    [Required(AllowEmptyStrings = false)]
    public string PropertyName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string PropertyValue { get; set; } = string.Empty;

    [Required]
    public DateOnly FromDate { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string Notes { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// Control Device Mapping (table: control_device_mapping)
// ------------------------------------------------------------

/// <summary>
/// Persisted control device mapping row (maps to Flyway V2 table: <c>control_device_mapping</c>).
/// </summary>
public sealed record ControlDeviceMappingDto(
    long ControlDeviceMappingId,
    long AssetId,
    long ControlDeviceId,
    string ControlDeviceNameOrRef,
    bool InUseFlag,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating a control device mapping.
/// </summary>
public sealed class CreateControlDeviceMappingRequest
{
    [Required]
    public long ControlDeviceId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ControlDeviceNameOrRef { get; set; } = string.Empty;

    [Required]
    public bool InUseFlag { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating a control device mapping.
/// </summary>
public sealed class UpdateControlDeviceMappingRequest
{
    [Required]
    public long ControlDeviceId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ControlDeviceNameOrRef { get; set; } = string.Empty;

    [Required]
    public bool InUseFlag { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// Input Parameter (table: input_parameter)
// ------------------------------------------------------------

/// <summary>
/// Persisted input parameter row (maps to Flyway V2 table: <c>input_parameter</c>).
/// </summary>
public sealed record InputParameterDto(
    long InputParameterId,
    long AssetId,
    string InputParameterName,
    long? UomId,
    long? ReportingProgramId,
    string InputType,
    string DataEntryFrequency,
    string? FuelMapping,
    bool InUseFlag,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating an input parameter row.
/// </summary>
public sealed class CreateInputParameterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string InputParameterName { get; set; } = string.Empty;

    public long? UomId { get; set; }

    public long? ReportingProgramId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string InputType { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DataEntryFrequency { get; set; } = string.Empty;

    public string? FuelMapping { get; set; }

    [Required]
    public bool InUseFlag { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating an input parameter row.
/// </summary>
public sealed class UpdateInputParameterRequest
{
    [Required(AllowEmptyStrings = false)]
    public string InputParameterName { get; set; } = string.Empty;

    public long? UomId { get; set; }

    public long? ReportingProgramId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string InputType { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string DataEntryFrequency { get; set; } = string.Empty;

    public string? FuelMapping { get; set; }

    [Required]
    public bool InUseFlag { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// Parent Input Mapping (table: parent_input_mapping)
// ------------------------------------------------------------

/// <summary>
/// Persisted parent input mapping row (maps to Flyway V2 table: <c>parent_input_mapping</c>).
/// </summary>
public sealed record ParentInputMappingDto(
    long ParentInputMappingId,
    long ChildInputParameterId,
    long ParentInputParameterId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating a parent input mapping.
/// </summary>
public sealed class CreateParentInputMappingRequest
{
    [Required]
    public long ParentInputParameterId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating a parent input mapping.
/// </summary>
public sealed class UpdateParentInputMappingRequest
{
    [Required]
    public long ParentInputParameterId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// Reporting Attribute Mapping (table: reporting_attribute_mapping)
// ------------------------------------------------------------

/// <summary>
/// Persisted reporting attribute mapping row (maps to Flyway V2 table: <c>reporting_attribute_mapping</c>).
/// </summary>
public sealed record ReportingAttributeMappingDto(
    long ReportingAttributeMappingId,
    long AssetId,
    string AttributeName,
    string AttributeValue,
    long ReportingProgramId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating a reporting attribute mapping.
/// </summary>
public sealed class CreateReportingAttributeMappingRequest
{
    [Required(AllowEmptyStrings = false)]
    public string AttributeName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AttributeValue { get; set; } = string.Empty;

    [Required]
    public long ReportingProgramId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating a reporting attribute mapping.
/// </summary>
public sealed class UpdateReportingAttributeMappingRequest
{
    [Required(AllowEmptyStrings = false)]
    public string AttributeName { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AttributeValue { get; set; } = string.Empty;

    [Required]
    public long ReportingProgramId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// EF Source Mapping (table: ef_source_mapping)
// ------------------------------------------------------------

/// <summary>
/// Persisted EF source mapping row (maps to Flyway V2 table: <c>ef_source_mapping</c>).
/// </summary>
public sealed record EfSourceMappingDto(
    long EfSourceMappingId,
    long InputParameterId,
    string EfSourceSetOrTable,
    string? EquationSetup,
    string? ScalarValues,
    long ReportingProgramId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating an EF source mapping row.
/// </summary>
public sealed class CreateEfSourceMappingRequest
{
    [Required(AllowEmptyStrings = false)]
    public string EfSourceSetOrTable { get; set; } = string.Empty;

    public string? EquationSetup { get; set; }

    public string? ScalarValues { get; set; }

    [Required]
    public long ReportingProgramId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating an EF source mapping row.
/// </summary>
public sealed class UpdateEfSourceMappingRequest
{
    [Required(AllowEmptyStrings = false)]
    public string EfSourceSetOrTable { get; set; } = string.Empty;

    public string? EquationSetup { get; set; }

    public string? ScalarValues { get; set; }

    [Required]
    public long ReportingProgramId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// Throughput Equation + Scalars (tables: throughput_equation, throughput_scalar)
// ------------------------------------------------------------

/// <summary>
/// Persisted throughput equation row (maps to Flyway V2 table: <c>throughput_equation</c>).
/// </summary>
public sealed record ThroughputEquationDto(
    long ThroughputEquationId,
    long InputParameterId,
    long MasterEquationId,
    string GeneratedEquation,
    int ReportingYear,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating a throughput equation row.
/// </summary>
public sealed class CreateThroughputEquationRequest
{
    [Required]
    public long MasterEquationId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string GeneratedEquation { get; set; } = string.Empty;

    [Required]
    public int ReportingYear { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating a throughput equation row.
/// </summary>
public sealed class UpdateThroughputEquationRequest
{
    [Required]
    public long MasterEquationId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string GeneratedEquation { get; set; } = string.Empty;

    [Required]
    public int ReportingYear { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Persisted throughput scalar row (maps to Flyway V2 table: <c>throughput_scalar</c>).
/// </summary>
public sealed record ThroughputScalarDto(
    long ThroughputScalarId,
    long ThroughputEquationId,
    string ScalarType,
    string? ScalarTable,
    string? ScalarId,
    string? ScalarValue,
    string? ScalarBasis,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating a throughput scalar row.
/// </summary>
public sealed class CreateThroughputScalarRequest
{
    [Required(AllowEmptyStrings = false)]
    public string ScalarType { get; set; } = string.Empty;

    public string? ScalarTable { get; set; }

    public string? ScalarId { get; set; }

    public string? ScalarValue { get; set; }

    public string? ScalarBasis { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating a throughput scalar row.
/// </summary>
public sealed class UpdateThroughputScalarRequest
{
    [Required(AllowEmptyStrings = false)]
    public string ScalarType { get; set; } = string.Empty;

    public string? ScalarTable { get; set; }

    public string? ScalarId { get; set; }

    public string? ScalarValue { get; set; }

    public string? ScalarBasis { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

// ------------------------------------------------------------
// Data Input Value (table: data_input_value)
// ------------------------------------------------------------

/// <summary>
/// Persisted data input value row (maps to Flyway V2 table: <c>data_input_value</c>).
/// </summary>
public sealed record DataInputValueDto(
    long DataInputValueId,
    long InputParameterId,
    string? InputParameterValue,
    int ReportingYear,
    string ReportingPeriod,
    string? CalculatedThroughputOutput,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string ModifiedBy,
    DateTimeOffset ModifiedAt,
    bool IsDeleted,
    string CorrelationId);

/// <summary>
/// Request payload for creating a data input value row.
/// </summary>
public sealed class CreateDataInputValueRequest
{
    public string? InputParameterValue { get; set; }

    [Required]
    public int ReportingYear { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ReportingPeriod { get; set; } = string.Empty;

    public string? CalculatedThroughputOutput { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// Request payload for updating a data input value row.
/// </summary>
public sealed class UpdateDataInputValueRequest
{
    public string? InputParameterValue { get; set; }

    [Required]
    public int ReportingYear { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ReportingPeriod { get; set; } = string.Empty;

    public string? CalculatedThroughputOutput { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;
}
