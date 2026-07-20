-- Destructive rollback for ProjectPulse migration 031.
BEGIN;
DROP TABLE IF EXISTS projectpulse_oncall_acknowledgements;
DROP TABLE IF EXISTS projectpulse_oncall_roster_members;
DROP TABLE IF EXISTS projectpulse_oncall_schedule_versions;
DROP TABLE IF EXISTS projectpulse_oneassist_route_revisions;
DROP TABLE IF EXISTS projectpulse_oneassist_routes;
DROP TABLE IF EXISTS projectpulse_module_audit_events;
COMMIT;
