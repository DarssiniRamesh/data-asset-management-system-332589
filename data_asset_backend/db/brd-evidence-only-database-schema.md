# BRD Evidence-Only Database Schema (NO ASSUMPTIONS)

Source: `attachments/BRD_-_Asset_Configuration_Feature.pdf` (dated 2026-02-27)

This document captures **only** what is evidenced in the BRD regarding persistence entities and data elements.  
Where the BRD does **not** specify technical details (exact SQL types, max lengths, enum values, FK cardinalities, uniqueness scope, etc.), it is explicitly called out as **NOT EVIDENCED**.

---

## 1) Evidenced persistent entities (BRD Section 10.1 “Data Model Deliverables”)

The BRD explicitly requires a normalized entity model for the following:

- Asset
- StatusLog
- AdditionalAssetID
- AssetProperty
- ControlDeviceMapping
- InputParameter
- ParentInputMapping
- ReportingAttributeMapping
- EFSourceMapping
- ThroughputEquation
- ThroughputScalar
- DataInputValue

**Evidence:** BRD §10.1: “Normalized entity model for: Asset, StatusLog, AdditionalAssetID, AssetProperty, ControlDeviceMapping, InputParameter, ParentInputMapping, ReportingAttributeMapping, EFSourceMapping, ThroughputEquation, ThroughputScalar, DataInputValue.”

---

## 2) Evidenced data elements by area (BRD Section 6)

### 2.1 Asset Header / Asset Details Tab (BRD §6.1)

Data elements evidenced:
- Site ID (Required)
- Asset Group (Required)
- Process Group (Required)
  - If "Other", free-text process group must be captured (Conditional)
- Asset Name (Required)
- Permit EU ID (Required) — Uniqueness constraint mentioned, but **scope is NOT EVIDENCED**
  - Implementation note (evidence-safe): uniqueness is enforced conservatively at the API layer across non-deleted assets, returning HTTP 409 on duplicates (no DB UNIQUE constraint is added without a scoped key).
- Global Unique Asset ID (Required) — “immutable once created” (immutability enforcement mechanism is **NOT EVIDENCED** at DB level)
- Asset Description (Optional)
- Stationary Flag (Optional)
- Pseudo/Child Mapping (Conditional) — “Parent pseudo asset required for child mode when options exist”

**Evidence:** BRD §6.1 table.

### 2.2 Status Log (BRD §6.2)

Data elements evidenced:
- Operating Status (Required) — must support Active/Inactive states (domain values beyond that are **NOT EVIDENCED**)
- Status From Date (Required)
- Status To Date (Optional)
- Comments (Optional)
- Status Chronology Rule (Required) — “Latest status from-date must be after previous to-date” (cross-row constraint; DB enforcement strategy **NOT EVIDENCED**)

**Evidence:** BRD §6.2 table.

### 2.3 Asset Properties (BRD §6.3)

Data elements evidenced (in-use row context):
- Property Name (Required when in-use)
- Property Value (Required when in-use)
- From Date (Required when in-use)
- Notes (Required when in-use)

**Evidence:** BRD §6.3 table.

### 2.4 Associated Control Devices (BRD §6.4)

Data elements evidenced:
- Control Device Mapping ID (Required) — “Reference to configured control-device context”
- Control Device Name/Ref (Required)
- In Use Flag (Required)

**Evidence:** BRD §6.4 table.

### 2.5 Associated Input Parameters & Fuel Mapping (BRD §6.5)

Data elements evidenced (in-use row context unless otherwise noted):
- Input Parameter Name (Required when in-use)
- UOM (Required when in-use) — must resolve to approved UOM master
- Reporting Program (Required when in-use) — at least one active reporting program mapping
- Input Type (Required when in-use)
- Data Entry Frequency (Required when in-use)
- Fuel Mapping (Optional/Conditional) — “Required when business type/fuel dependency applies” (condition logic **NOT EVIDENCED**)
- In Use Flag (Required)

**Evidence:** BRD §6.5 table.

### 2.6 Parent Input Parameter Mapping (BRD §6.6)

Data elements evidenced:
- Child Input ID (Required)
- Parent Input ID (Required)
- Hierarchy Validity (Required) — no cycles; parent-child consistency (DB enforcement strategy **NOT EVIDENCED**)

**Evidence:** BRD §6.6 table.

### 2.7 Reporting Attributes Mapping (BRD §6.7)

Data elements evidenced:
- Attribute Name (Required) — attribute master key/reference
- Attribute Value (Required)
- Reporting Program Link (Required)
- Uniqueness Constraint (Required) — “Duplicate attribute combinations prevented” (exact uniqueness key **NOT EVIDENCED**)

**Evidence:** BRD §6.7 table.

### 2.8 EF Source Mapping Data (BRD §6.8)

Data elements evidenced:
- Input Parameter Link (Required)
- EF Source Set/Table (Required)
- Equation Setup (Optional/Conditional) — required when EF path uses equation behavior (condition logic **NOT EVIDENCED**)
- Scalar Values (Optional/Conditional) — required when equation requires scalar operands (condition logic **NOT EVIDENCED**)
- Reporting Program Mapping (Required)

**Evidence:** BRD §6.8 table.

### 2.9 Calculated Throughput Setup Data (BRD §6.9)

Data elements evidenced:
- Input Parameter Link (Required)
- Master Equation ID (Required)
- Generated Equation (Required)
- Equation Scalars (Required if equation uses scalars) — “Scalar type/table/id/value and basis”
- Reporting Year (Required)

**Evidence:** BRD §6.9 table.

### 2.10 Data Input Values (BRD §6.10)

Data elements evidenced:
- Input Parameter Value (Required by frequency/reporting scope) — “Captured per frequency and period”
- Reporting Year/Period (Required)
- Calculated Throughput Output (System-generated)

**Evidence:** BRD §6.10 table.

### 2.11 Audit and Trace Data (All Entities) (BRD §6.11)

Data elements evidenced:
- Created By / Created Date (Required)
- Modified By / Modified Date (Required)
- Soft Delete Flag (Required) — “If logical deletes are used” (whether logical deletes are used is **NOT EVIDENCED**, but field is required if used)
- Correlation ID (Required)

**Evidence:** BRD §6.11 table.

### 2.12 Copy and Lineage Capture Requirements (BRD §6.13)

Data elements evidenced (copy action):
- Copy Operation ID (Required)
- Source Asset ID (Required)
- Target Asset ID (Required)
- Copy Timestamp (Required) — UTC timestamp
- Copy Performed By (Required)
- Replication Result Status (Required) — Completed/Partial/Failed with reason code and impacted module list (exact coding scheme **NOT EVIDENCED**)

**Evidence:** BRD §6.13 table.

### 2.13 Reference and Master Data Capture Requirements (BRD §6.14)

Master reference domains evidenced:
- UOM Master (Required) — master key and display label; invalid/retired values blocked (blocking rule enforcement **NOT EVIDENCED**)
- Reporting Program Master (Required) — program IDs and active status required
- Control Device Master (Required) — key, site association, active flag
- Equation Master (Required) — equation template version and effective period
- Status Code Master (Required) — status code + business meaning

**Evidence:** BRD §6.14 table.

---

## 3) Technical specifics NOT evidenced (explicitly)

The BRD does **not** provide:
- Exact SQL data types, sizes/precision, or indexing requirements per field (it requires a data dictionary in §6.12 but does not include it)
- Exact uniqueness scopes/keys (e.g., Permit EU ID scope, reporting attribute uniqueness composite key)
- Explicit PK format (UUID vs numeric identity) and any required generator
- Full relationship cardinalities and cascade/ownership rules for every entity

Therefore, migrations must choose implementable SQL representations; such choices are recorded in the Flyway plan as “Implementation Choices due to missing BRD technical typing”.
