\set ON_ERROR_STOP on
BEGIN;
DO $do$ DECLARE q2 uuid:='04200000-0000-4000-8000-000000000201'; july uuid:='04200000-0000-4000-8000-000000000202'; card uuid:='04200000-0000-4000-8000-000000000203'; BEGIN
 IF EXISTS(SELECT 1 FROM billing_invoices WHERE project_id IN(q2,july)) THEN RAISE EXCEPTION 'Cleanup blocked: immutable invoice exists'; END IF;
 DELETE FROM time_entries WHERE project_id IN(q2,july); DELETE FROM project_assignments WHERE project_id IN(q2,july); DELETE FROM project_tasks WHERE project_id IN(q2,july); DELETE FROM project_purchase_orders WHERE project_id IN(q2,july); DELETE FROM project_billing_profiles WHERE project_id IN(q2,july); DELETE FROM projects WHERE project_id IN(q2,july); DELETE FROM work_rate_card_lines WHERE rate_card_id=card; DELETE FROM work_rate_cards WHERE rate_card_id=card;
 DELETE FROM timesheets t WHERE t.user_id='faf19aa4-47b2-45ab-bc6d-0351ea5d72e0' AND t.timesheet_id IN (SELECT (md5('DEMO-CUCM-TIMESHEET-'||d::text))::uuid FROM generate_series('2026-03-30'::date,'2026-07-27'::date,'7 day') d) AND NOT EXISTS(SELECT 1 FROM time_entries e WHERE e.timesheet_id=t.timesheet_id);
END $do$;
COMMIT;
SELECT 'KEVIN_CUCM_DEMO_CLEANUP_COMPLETE' status;
