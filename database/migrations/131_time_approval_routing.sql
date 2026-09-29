-- Apply after 060c. Additive routing shared by approvals and contract balances.
BEGIN;
CREATE OR REPLACE FUNCTION time_requires_project_approval(entry_row JSONB, project_row JSONB, task_row JSONB)
RETURNS BOOLEAN LANGUAGE SQL IMMUTABLE PARALLEL SAFE AS $$
 SELECT COALESCE(
   NULLIF(project_row->>'project_id', '') IS NOT NULL
   AND COALESCE(NULLIF(project_row->>'project_manager_user_id', ''),
                NULLIF(project_row->>'project_coordinator_user_id', '')) IS NOT NULL
   -- The assigned reviewer's own time is completed by their Manager/PTC.
   AND (entry_row->>'user_id') IS DISTINCT FROM COALESCE(
       NULLIF(project_row->>'project_manager_user_id', ''),
       NULLIF(project_row->>'project_coordinator_user_id', ''))
   AND COALESCE(project_row->>'project_code', '') !~* '^(SR|PRES|INT)-'
   AND regexp_replace(lower(COALESCE(project_row->>'work_type', 'Project')), '[^a-z0-9]+', '', 'g')
       NOT IN ('servicerequest', 'servicerequesttask', 'sr', 'presales', 'presale', 'pres', 'presalestask', 'internal', 'internalproject', 'internaltask')
   AND regexp_replace(lower(COALESCE(task_row->>'work_task_category', task_row->>'work_type', 'project_task')), '[^a-z0-9]+', '', 'g')
       NOT IN ('servicerequest', 'servicerequesttask', 'sr', 'presales', 'presale', 'presalestask', 'internal', 'internaltask')
   AND NULLIF(task_row->>'service_request_number', '') IS NULL
   AND NULLIF(entry_row->>'service_request_id', '') IS NULL,
 FALSE);
$$;
CREATE OR REPLACE VIEW vw_boh_authoritative_time_usage AS
WITH mapped AS (
    SELECT e.time_entry_id, e.project_id, e.user_id, e.work_date, e.hours,
        e.status, e.created_at AS entry_created_at,
        COALESCE(l.boh_contract_id, f.boh_contract_id) AS boh_contract_id,
        COALESCE(l.billing_rate, f.drawdown_hourly_rate, 0) AS billing_rate,
        time_requires_project_approval(to_jsonb(e), to_jsonb(p), to_jsonb(task)) AS has_pm
    FROM time_entries e
    LEFT JOIN projects p ON p.project_id = e.project_id
    LEFT JOIN project_tasks task ON task.task_id = e.task_id
    LEFT JOIN boh_usage_ledger l ON l.time_entry_id = e.time_entry_id
        AND l.usage_status NOT IN ('reversed', 'voided')
    LEFT JOIN contract_project_funding f ON f.project_id = e.project_id
    WHERE l.boh_contract_id IS NOT NULL OR f.boh_contract_id IS NOT NULL
)
SELECT m.*, ROUND(m.hours * m.billing_rate, 2)::NUMERIC(14,2) AS amount,
    contract_time_approval_bucket(m.status, m.has_pm) AS approval_bucket
FROM mapped m;

-- Fail closed if a repeat application changes the essential routing contract.
DO $$
BEGIN
 IF NOT time_requires_project_approval('{"user_id":"engineer"}', '{"project_id":"project","project_coordinator_user_id":"coordinator"}', '{}')
 OR time_requires_project_approval('{"user_id":"pm"}', '{"project_id":"project","project_manager_user_id":"pm"}', '{}')
 OR time_requires_project_approval('{"user_id":"engineer"}', '{"project_id":"project","project_manager_user_id":"pm","work_type":"Service Request"}', '{}') THEN
   RAISE EXCEPTION 'Time approval routing verification failed';
 END IF;
END $$;
COMMIT;
