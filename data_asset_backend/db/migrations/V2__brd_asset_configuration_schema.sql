-- V2 BRD Asset Configuration schema
-- Source: attachments/BRD_-_Asset_Configuration_Feature.pdf
-- Authoritative plan/spec:
-- - db/brd-evidence-only-database-schema.md
-- - db/brd-flyway-migration-plan.md
--
-- Notes:
-- - BRD does not provide a technical data dictionary (types/lengths/domains). This migration uses
--   conservative Postgres types (TEXT, DATE, TIMESTAMPTZ, BOOLEAN) to avoid over-constraining.
-- - Uniqueness constraints are only applied where BRD indicates global uniqueness (Global Unique Asset ID).
-- - Some cross-row/business rules (e.g., status chronology, no cycles) are not enforced at DB level
--   because the BRD does not specify an enforceable technical rule shape.

------------------------------------------------------------
-- Master / reference tables (BRD §6.14)
------------------------------------------------------------

CREATE TABLE uom_master (
    uom_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    uom_key TEXT NOT NULL,
    display_label TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

CREATE TABLE reporting_program_master (
    reporting_program_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    program_key TEXT NOT NULL,
    display_label TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

CREATE TABLE control_device_master (
    control_device_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    site_id TEXT NOT NULL,
    device_key TEXT NOT NULL,
    display_label TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

CREATE TABLE equation_master (
    equation_master_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    equation_key TEXT NOT NULL,
    version_label TEXT NOT NULL,
    effective_from DATE NULL,
    effective_to DATE NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

CREATE TABLE status_code_master (
    status_code_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    status_code TEXT NOT NULL,
    business_meaning TEXT NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

------------------------------------------------------------
-- Core entities (BRD §10.1) + header fields (BRD §6.1)
------------------------------------------------------------

CREATE TABLE asset (
    asset_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- BRD §6.1 asset header fields
    site_id TEXT NOT NULL,
    asset_group TEXT NOT NULL,
    process_group TEXT NOT NULL,
    process_group_other_text TEXT NULL,
    asset_name TEXT NOT NULL,
    permit_eu_id TEXT NOT NULL,
    global_unique_asset_id TEXT NOT NULL,
    asset_description TEXT NULL,
    stationary_flag BOOLEAN NULL,
    parent_pseudo_asset_id BIGINT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT uq_asset_global_unique_asset_id UNIQUE (global_unique_asset_id),
    CONSTRAINT fk_asset_parent_pseudo_asset
        FOREIGN KEY (parent_pseudo_asset_id) REFERENCES asset(asset_id)
);

------------------------------------------------------------
-- Status log (BRD §6.2)
------------------------------------------------------------

CREATE TABLE asset_status_log (
    asset_status_log_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_id BIGINT NOT NULL,

    operating_status TEXT NOT NULL,
    status_from_date DATE NOT NULL,
    status_to_date DATE NULL,
    comments TEXT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_asset_status_log_asset
        FOREIGN KEY (asset_id) REFERENCES asset(asset_id)
);

------------------------------------------------------------
-- AdditionalAssetID (BRD §10.1; no additional fields enumerated)
------------------------------------------------------------

CREATE TABLE additional_asset_id (
    additional_asset_id_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_id BIGINT NOT NULL,

    -- BRD mentions "Additional Asset IDs" but does not enumerate the fields.
    -- Represent as a key/value pair without assuming specific identifiers.
    id_type TEXT NOT NULL,
    id_value TEXT NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_additional_asset_id_asset
        FOREIGN KEY (asset_id) REFERENCES asset(asset_id)
);

------------------------------------------------------------
-- AssetProperty (BRD §6.3)
------------------------------------------------------------

CREATE TABLE asset_property (
    asset_property_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_id BIGINT NOT NULL,

    property_name TEXT NOT NULL,
    property_value TEXT NOT NULL,
    from_date DATE NOT NULL,
    notes TEXT NOT NULL,

    -- In-use row context is a business rule; persisted explicitly for validation.
    in_use_flag BOOLEAN NOT NULL DEFAULT TRUE,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_asset_property_asset
        FOREIGN KEY (asset_id) REFERENCES asset(asset_id)
);

------------------------------------------------------------
-- ControlDeviceMapping (BRD §6.4)
------------------------------------------------------------

CREATE TABLE control_device_mapping (
    control_device_mapping_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_id BIGINT NOT NULL,

    -- BRD §6.4: mapping id reference + name/ref + in-use flag
    control_device_id BIGINT NULL,
    control_device_name_or_ref TEXT NOT NULL,
    in_use_flag BOOLEAN NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_control_device_mapping_asset
        FOREIGN KEY (asset_id) REFERENCES asset(asset_id),
    CONSTRAINT fk_control_device_mapping_master
        FOREIGN KEY (control_device_id) REFERENCES control_device_master(control_device_id)
);

------------------------------------------------------------
-- InputParameter (BRD §6.5)
------------------------------------------------------------

CREATE TABLE input_parameter (
    input_parameter_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_id BIGINT NOT NULL,

    input_parameter_name TEXT NOT NULL,
    uom_id BIGINT NULL,
    reporting_program_id BIGINT NULL,
    input_type TEXT NOT NULL,
    data_entry_frequency TEXT NOT NULL,
    fuel_mapping TEXT NULL,
    in_use_flag BOOLEAN NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_input_parameter_asset
        FOREIGN KEY (asset_id) REFERENCES asset(asset_id),
    CONSTRAINT fk_input_parameter_uom
        FOREIGN KEY (uom_id) REFERENCES uom_master(uom_id),
    CONSTRAINT fk_input_parameter_reporting_program
        FOREIGN KEY (reporting_program_id) REFERENCES reporting_program_master(reporting_program_id)
);

------------------------------------------------------------
-- ParentInputMapping (BRD §6.6)
------------------------------------------------------------

CREATE TABLE parent_input_mapping (
    parent_input_mapping_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    child_input_parameter_id BIGINT NOT NULL,
    parent_input_parameter_id BIGINT NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_parent_input_mapping_child
        FOREIGN KEY (child_input_parameter_id) REFERENCES input_parameter(input_parameter_id),
    CONSTRAINT fk_parent_input_mapping_parent
        FOREIGN KEY (parent_input_parameter_id) REFERENCES input_parameter(input_parameter_id)
);

------------------------------------------------------------
-- ReportingAttributeMapping (BRD §6.7)
------------------------------------------------------------

CREATE TABLE reporting_attribute_mapping (
    reporting_attribute_mapping_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    asset_id BIGINT NOT NULL,

    attribute_name TEXT NOT NULL,
    attribute_value TEXT NOT NULL,
    reporting_program_id BIGINT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_reporting_attribute_mapping_asset
        FOREIGN KEY (asset_id) REFERENCES asset(asset_id),
    CONSTRAINT fk_reporting_attribute_mapping_reporting_program
        FOREIGN KEY (reporting_program_id) REFERENCES reporting_program_master(reporting_program_id)
);

------------------------------------------------------------
-- EFSourceMapping (BRD §6.8)
------------------------------------------------------------

CREATE TABLE ef_source_mapping (
    ef_source_mapping_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    input_parameter_id BIGINT NOT NULL,

    ef_source_set_or_table TEXT NOT NULL,
    equation_setup TEXT NULL,
    scalar_values TEXT NULL,
    reporting_program_id BIGINT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_ef_source_mapping_input_parameter
        FOREIGN KEY (input_parameter_id) REFERENCES input_parameter(input_parameter_id),
    CONSTRAINT fk_ef_source_mapping_reporting_program
        FOREIGN KEY (reporting_program_id) REFERENCES reporting_program_master(reporting_program_id)
);

------------------------------------------------------------
-- ThroughputEquation (BRD §6.9)
------------------------------------------------------------

CREATE TABLE throughput_equation (
    throughput_equation_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    input_parameter_id BIGINT NOT NULL,

    master_equation_id BIGINT NULL,
    generated_equation TEXT NOT NULL,
    reporting_year INT NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_throughput_equation_input_parameter
        FOREIGN KEY (input_parameter_id) REFERENCES input_parameter(input_parameter_id),
    CONSTRAINT fk_throughput_equation_master
        FOREIGN KEY (master_equation_id) REFERENCES equation_master(equation_master_id)
);

------------------------------------------------------------
-- ThroughputScalar (BRD §6.9)
------------------------------------------------------------

CREATE TABLE throughput_scalar (
    throughput_scalar_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    throughput_equation_id BIGINT NOT NULL,

    scalar_type TEXT NOT NULL,
    scalar_table TEXT NULL,
    scalar_id TEXT NULL,
    scalar_value TEXT NULL,
    scalar_basis TEXT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_throughput_scalar_equation
        FOREIGN KEY (throughput_equation_id) REFERENCES throughput_equation(throughput_equation_id)
);

------------------------------------------------------------
-- DataInputValue (BRD §6.10)
------------------------------------------------------------

CREATE TABLE data_input_value (
    data_input_value_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    input_parameter_id BIGINT NOT NULL,

    input_parameter_value TEXT NULL,
    reporting_year INT NOT NULL,
    reporting_period TEXT NOT NULL,
    calculated_throughput_output TEXT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_data_input_value_input_parameter
        FOREIGN KEY (input_parameter_id) REFERENCES input_parameter(input_parameter_id)
);

------------------------------------------------------------
-- Copy lineage (BRD §6.13)
------------------------------------------------------------

CREATE TABLE asset_copy_lineage (
    asset_copy_lineage_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    copy_operation_id TEXT NOT NULL,
    source_asset_id BIGINT NOT NULL,
    target_asset_id BIGINT NOT NULL,
    copy_timestamp_utc TIMESTAMPTZ NOT NULL,
    copy_performed_by TEXT NOT NULL,
    replication_result_status TEXT NOT NULL,
    replication_result_detail TEXT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL,

    CONSTRAINT fk_asset_copy_lineage_source_asset
        FOREIGN KEY (source_asset_id) REFERENCES asset(asset_id),
    CONSTRAINT fk_asset_copy_lineage_target_asset
        FOREIGN KEY (target_asset_id) REFERENCES asset(asset_id)
);
