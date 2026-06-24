-- Project Pulse
-- Rollback: 011_remaining_psa_module_foundation_rollback.sql

BEGIN;

DELETE FROM schema_migrations WHERE migration_id = '011_remaining_psa_module_foundation';

DROP TABLE IF EXISTS reporting_snapshots;
DROP TABLE IF EXISTS invoice_line_items;
DROP TABLE IF EXISTS client_invoices;
DROP TABLE IF EXISTS expense_items;
DROP TABLE IF EXISTS expense_reports;
DROP TABLE IF EXISTS resource_capacity_plans;
DROP TABLE IF EXISTS project_risks;
DROP TABLE IF EXISTS project_milestones;
DROP TABLE IF EXISTS project_templates;
DROP TABLE IF EXISTS project_intake_requests;

COMMIT;
