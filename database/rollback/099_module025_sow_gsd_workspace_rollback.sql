BEGIN;

DROP TRIGGER IF EXISTS trg_module025_protect_sow_gsd_identity ON module025_sow_gsd_engagements;
DROP FUNCTION IF EXISTS module025_protect_sow_gsd_identity();
DROP TABLE IF EXISTS module025_sow_gsd_events;
DROP TABLE IF EXISTS module025_sow_gsd_phases;
DROP TABLE IF EXISTS module025_sow_gsd_engagements;
DROP SEQUENCE IF EXISTS module025_sow_gsd_number_seq;

COMMIT;
