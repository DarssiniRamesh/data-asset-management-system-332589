# BRD Flyway Migration Plan (Derived from Evidence-Only Schema)

Authoritative inputs:
- `db/brd-evidence-only-database-schema.md`
- `attachments/BRD_-_Asset_Configuration_Feature.pdf`

Goal:
- Implement the BRD-required normalized entity model using Flyway SQL migrations.
- Keep migrations ordered, deterministic, and compatible with Flyway Community (Postgres).

Non-goals:
- Do not invent business rules not evidenced (e.g., exact uniqueness scopes, enum domains beyond “Active/Inactive”, etc.).
- Do not embed application logic constraints that BRD calls out but does not technically specify (e.g., status chronology).

---

## 1) Current state

Existing migration:
- `db/migrations/V1__init.sql` is a minimal baseline.

Plan:
- Keep V1 unchanged.
- Add V2 to create BRD-evidenced tables.

---

## 2) Migration ordering

### V1__init.sql (existing)
- Baseline only.

### V2__brd_asset_configuration_schema.sql (new)
Creates the normalized entity tables evidenced in BRD §10.1 and supporting master/reference tables evidenced in BRD §6.14, plus a copy lineage table per BRD §6.13.

---

## 3) Implementation choices due to missing BRD technical typing (explicit)

The BRD requires a formal data dictionary (§6.12) but does not provide the actual technical types/lengths/domains. To produce a runnable Postgres schema:

- **Primary keys:** use `BIGINT GENERATED ALWAYS AS IDENTITY` as a neutral, widely-supported choice.
- **Foreign keys:** added where relationships are directly implied by the BRD wording (“link”, “reference”, “mapping to asset/input”).
- **Textual business fields:** stored as `TEXT` when BRD does not provide length/format constraints.
- **Dates/timestamps:**
  - “Status From/To Date” stored as `DATE` (BRD uses date semantics).
  - Audit “Created Date/Modified Date” stored as `TIMESTAMPTZ`.
- **Booleans:** flags stored as `BOOLEAN`.
- **Uniqueness constraints:** only added when the BRD clearly asserts global uniqueness (e.g., “Global Unique Asset ID”). Where scope is unclear (e.g., Permit EU ID), uniqueness constraints are **not** enforced at DB level in this migration.
- **Soft delete:** BRD says “Soft Delete Flag Required if logical deletes are used.” We include `is_deleted BOOLEAN NOT NULL DEFAULT FALSE` to support recoverable history as referenced elsewhere in the BRD (§7 delete behavior).
- **Correlation ID:** stored as `TEXT NOT NULL` (format not evidenced).

These choices are made to avoid over-constraining the schema while still enabling persistence and referential integrity.

---

## 4) Tables created in V2 (summary)

Business entities (BRD §10.1):
- `asset`
- `asset_status_log`
- `additional_asset_id`
- `asset_property`
- `control_device_mapping`
- `input_parameter`
- `parent_input_mapping`
- `reporting_attribute_mapping`
- `ef_source_mapping`
- `throughput_equation`
- `throughput_scalar`
- `data_input_value`

Copy lineage (BRD §6.13):
- `asset_copy_lineage`

Reference/master data (BRD §6.14):
- `uom_master`
- `reporting_program_master`
- `control_device_master`
- `equation_master`
- `status_code_master`

---

## 5) Rollback notes

Flyway Community does not support automatic down migrations; rollback strategy is:
- Use forward-fix migrations (V3+) to correct schema changes.
- For local/dev reset only: drop schema and re-run migrations (outside production).
