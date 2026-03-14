-- V3 BRD Section 4 modules schema
-- Source of truth: attachments/BRD_-_Asset_Configuration_Feature.pdf (dated 2026-02-27)
--
-- IMPORTANT (NO ASSUMPTIONS):
-- The BRD names required pages/modules in §4 but does NOT evidence detailed data-capture fields
-- for these modules in §6 (fields there focus on Asset Configuration modules).
-- Therefore this migration adds ONLY:
-- - a minimal identity
-- - a display name/label (where evidenced by module naming)
-- - standard BRD §6.11 audit/trace fields
-- - soft delete flag (is_deleted) as used elsewhere in V2 schema
--
-- If future BRD revisions evidence additional fields/workflows, they must be added in later migrations.

------------------------------------------------------------
-- 4.1 Site Profile Information
------------------------------------------------------------
CREATE TABLE site_profile (
    site_profile_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Minimal business identifier. BRD does not specify additional fields.
    site_id TEXT NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

------------------------------------------------------------
-- 4.2 Control Device Configuration
------------------------------------------------------------
-- NOTE: BRD §6.14 already defines "control_device_master" as reference/master.
-- Section 4 calls out the configuration screen; we model it as CRUD against
-- control_device_master, not a separate table. (No new table here.)

------------------------------------------------------------
-- 4.3 Asset / WWTS Process Stream Configuration (conditional)
------------------------------------------------------------
CREATE TABLE wwts_process_stream (
    wwts_process_stream_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- No specific fields evidenced; store site context + a label.
    site_id TEXT NOT NULL,
    stream_name TEXT NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

------------------------------------------------------------
-- 4.4 Chemical Raw Material Configuration and Usage
------------------------------------------------------------
CREATE TABLE chemical_raw_material (
    chemical_raw_material_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- No BRD-evidenced field list; keep site context + name.
    site_id TEXT NOT NULL,
    chemical_name TEXT NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

------------------------------------------------------------
-- 4.5 Chemical SDS Details Configuration
------------------------------------------------------------
CREATE TABLE chemical_sds (
    chemical_sds_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    site_id TEXT NOT NULL,
    chemical_name TEXT NOT NULL,

    -- BRD does not evidence SDS-specific fields (URL/file id/revision/etc.). Not added.

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

------------------------------------------------------------
-- 4.6 Lab Data Configuration (conditional)
------------------------------------------------------------
CREATE TABLE lab_data_configuration (
    lab_data_configuration_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    site_id TEXT NOT NULL,
    configuration_name TEXT NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);

------------------------------------------------------------
-- 4.7 WWTS / Water Process related screens (conditional)
------------------------------------------------------------
CREATE TABLE water_process_configuration (
    water_process_configuration_id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    site_id TEXT NOT NULL,
    configuration_name TEXT NOT NULL,

    created_by TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    modified_by TEXT NOT NULL,
    modified_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    is_deleted BOOLEAN NOT NULL DEFAULT FALSE,
    correlation_id TEXT NOT NULL
);
