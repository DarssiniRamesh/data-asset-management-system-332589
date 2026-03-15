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
public sealed class CreateInputParameterRequest : IValidatableObject
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // BRD §6.5 (tab-level rule): When an input parameter row is "in-use", UOM and Reporting Program are required.
        // Domain resolution/activeness checks are NOT evidenced here; we only enforce presence.
        if (InUseFlag)
        {
            if (!UomId.HasValue)
            {
                yield return new ValidationResult(
                    "uomId is required when inUseFlag is true.",
                    new[] { nameof(UomId) });
            }

            if (!ReportingProgramId.HasValue)
            {
                yield return new ValidationResult(
                    "reportingProgramId is required when inUseFlag is true.",
                    new[] { nameof(ReportingProgramId) });
            }
        }
    }
}

/// <summary>
/// Request payload for updating an input parameter row.
/// </summary>
public sealed class UpdateInputParameterRequest : IValidatableObject
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // BRD §6.5 (tab-level rule): When an input parameter row is "in-use", UOM and Reporting Program are required.
        // Domain resolution/activeness checks are NOT evidenced here; we only enforce presence.
        if (InUseFlag)
        {
            if (!UomId.HasValue)
            {
                yield return new ValidationResult(
                    "uomId is required when inUseFlag is true.",
                    new[] { nameof(UomId) });
            }

            if (!ReportingProgramId.HasValue)
            {
                yield return new ValidationResult(
                    "reportingProgramId is required when inUseFlag is true.",
                    new[] { nameof(ReportingProgramId) });
            }
        }
    }
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

    /// <summary>
    /// Optional in request payload.
    /// If not provided (or provided as &lt;= 0), the backend derives it from the parent input_parameter row.
    /// </summary>
    public long? ReportingProgramId { get; set; }

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

    /// <summary>
    /// Optional in request payload.
    /// If not provided (or provided as &lt;= 0), the backend derives it from the parent input_parameter row.
    /// </summary>
    public long? ReportingProgramId { get; set; }

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
public sealed class CreateThroughputEquationRequest : IValidatableObject
{
    /// <summary>
    /// Canonical BRD/DB FK to equation_master (table column: throughput_equation.master_equation_id).
    /// </summary>
    [Required]
    public long MasterEquationId { get; set; }

    /// <summary>
    /// Alias for <see cref="MasterEquationId"/> used by some frontend payloads.
    /// Keep this for compatibility so UI can send equationMasterId without triggering FK violations.
    /// </summary>
    public long? EquationMasterId { get; set; }

    /// <summary>
    /// Canonical equation text stored on throughput_equation.generated_equation.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string GeneratedEquation { get; set; } = string.Empty;

    /// <summary>
    /// Alias for <see cref="GeneratedEquation"/> used by some frontend payloads.
    /// </summary>
    public string? EquationText { get; set; }

    /// <summary>
    /// Canonical reporting year (table column: throughput_equation.reporting_year).
    /// </summary>
    [Required]
    public int ReportingYear { get; set; }

    /// <summary>
    /// Optional alias. If provided and <see cref="ReportingYear"/> is not set, backend will default ReportingYear.
    /// (DB requires reporting_year, so we still validate.)
    /// </summary>
    public int? Year { get; set; }

    // UI-only flag; not persisted in this table. Accept for compatibility and ignore.
    public bool? IsActive { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Normalize legacy/alternate payload shapes into the canonical DB fields.
    /// This is used by repository flows to avoid FK/NOT NULL violations.
    /// </summary>
    public void Normalize()
    {
        if (MasterEquationId <= 0 && EquationMasterId.HasValue)
        {
            MasterEquationId = EquationMasterId.Value;
        }

        if (string.IsNullOrWhiteSpace(GeneratedEquation) && !string.IsNullOrWhiteSpace(EquationText))
        {
            GeneratedEquation = EquationText.Trim();
        }

        if (ReportingYear <= 0 && Year.HasValue)
        {
            ReportingYear = Year.Value;
        }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Ensure normalization has occurred if model binder populated only alias fields.
        Normalize();

        if (MasterEquationId <= 0)
        {
            yield return new ValidationResult(
                "masterEquationId is required and must be > 0 (FK to equation_master).",
                new[] { nameof(MasterEquationId), nameof(EquationMasterId) });
        }

        if (string.IsNullOrWhiteSpace(GeneratedEquation))
        {
            yield return new ValidationResult(
                "generatedEquation is required (or provide equationText).",
                new[] { nameof(GeneratedEquation), nameof(EquationText) });
        }

        if (ReportingYear <= 0)
        {
            yield return new ValidationResult(
                "reportingYear is required and must be > 0.",
                new[] { nameof(ReportingYear), nameof(Year) });
        }
    }
}

/// <summary>
/// Request payload for updating a throughput equation row.
/// </summary>
public sealed class UpdateThroughputEquationRequest : IValidatableObject
{
    [Required]
    public long MasterEquationId { get; set; }

    public long? EquationMasterId { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string GeneratedEquation { get; set; } = string.Empty;

    public string? EquationText { get; set; }

    [Required]
    public int ReportingYear { get; set; }

    public int? Year { get; set; }

    public bool? IsActive { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;

    public void Normalize()
    {
        if (MasterEquationId <= 0 && EquationMasterId.HasValue)
        {
            MasterEquationId = EquationMasterId.Value;
        }

        if (string.IsNullOrWhiteSpace(GeneratedEquation) && !string.IsNullOrWhiteSpace(EquationText))
        {
            GeneratedEquation = EquationText.Trim();
        }

        if (ReportingYear <= 0 && Year.HasValue)
        {
            ReportingYear = Year.Value;
        }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        Normalize();

        if (MasterEquationId <= 0)
        {
            yield return new ValidationResult(
                "masterEquationId is required and must be > 0 (FK to equation_master).",
                new[] { nameof(MasterEquationId), nameof(EquationMasterId) });
        }

        if (string.IsNullOrWhiteSpace(GeneratedEquation))
        {
            yield return new ValidationResult(
                "generatedEquation is required (or provide equationText).",
                new[] { nameof(GeneratedEquation), nameof(EquationText) });
        }

        if (ReportingYear <= 0)
        {
            yield return new ValidationResult(
                "reportingYear is required and must be > 0.",
                new[] { nameof(ReportingYear), nameof(Year) });
        }
    }
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
public sealed class CreateThroughputScalarRequest : IValidatableObject
{
    /// <summary>
    /// Canonical scalar type (required by DB).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ScalarType { get; set; } = string.Empty;

    public string? ScalarTable { get; set; }

    public string? ScalarId { get; set; }

    public string? ScalarValue { get; set; }

    public string? ScalarBasis { get; set; }

    // ---------- Compatibility aliases (UI payload shape) ----------
    public string? ScalarName { get; set; }
    public decimal? ScalarNumberValue { get; set; }
    public long? UomId { get; set; }
    public bool? IsActive { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;

    public void Normalize()
    {
        // If UI sends scalarName but not scalarType, treat scalarName as the type label.
        if (string.IsNullOrWhiteSpace(ScalarType) && !string.IsNullOrWhiteSpace(ScalarName))
        {
            ScalarType = ScalarName.Trim();
        }

        // If UI sends numeric scalar value, store as string (DB column is text in this schema).
        if (ScalarNumberValue.HasValue && string.IsNullOrWhiteSpace(ScalarValue))
        {
            ScalarValue = ScalarNumberValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // If UI sends uomId, store in scalar_basis as a tagged value (schema has no uom FK here).
        // This keeps data round-trippable without inventing new columns.
        if (UomId.HasValue && string.IsNullOrWhiteSpace(ScalarBasis))
        {
            ScalarBasis = $"uomId:{UomId.Value}";
        }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        Normalize();

        if (string.IsNullOrWhiteSpace(ScalarType))
        {
            yield return new ValidationResult(
                "scalarType is required (or provide scalarName).",
                new[] { nameof(ScalarType), nameof(ScalarName) });
        }
    }
}

/// <summary>
/// Request payload for updating a throughput scalar row.
/// </summary>
public sealed class UpdateThroughputScalarRequest : IValidatableObject
{
    [Required(AllowEmptyStrings = false)]
    public string ScalarType { get; set; } = string.Empty;

    public string? ScalarTable { get; set; }

    public string? ScalarId { get; set; }

    public string? ScalarValue { get; set; }

    public string? ScalarBasis { get; set; }

    // Compatibility aliases (UI payload shape)
    public string? ScalarName { get; set; }
    public decimal? ScalarNumberValue { get; set; }
    public long? UomId { get; set; }
    public bool? IsActive { get; set; }

    [Required(AllowEmptyStrings = false)]
    public string ModifiedBy { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string CorrelationId { get; set; } = string.Empty;

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(ScalarType) && !string.IsNullOrWhiteSpace(ScalarName))
        {
            ScalarType = ScalarName.Trim();
        }

        if (ScalarNumberValue.HasValue && string.IsNullOrWhiteSpace(ScalarValue))
        {
            ScalarValue = ScalarNumberValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (UomId.HasValue && string.IsNullOrWhiteSpace(ScalarBasis))
        {
            ScalarBasis = $"uomId:{UomId.Value}";
        }
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        Normalize();

        if (string.IsNullOrWhiteSpace(ScalarType))
        {
            yield return new ValidationResult(
                "scalarType is required (or provide scalarName).",
                new[] { nameof(ScalarType), nameof(ScalarName) });
        }
    }
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
