-- V1 baseline migration for Data Asset Management System.
-- Intentionally minimal: avoids inventing domain schema before the CRUD model is implemented.
--
-- Flyway will still track this migration in flyway_schema_history.

CREATE TABLE IF NOT EXISTS __app_schema_baseline (
  id INT PRIMARY KEY,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
