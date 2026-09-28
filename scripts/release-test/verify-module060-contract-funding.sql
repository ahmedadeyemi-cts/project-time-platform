-- Read-only post-migration checks. Never creates customers, time or funding.
DO $$
BEGIN
    IF to_regclass('public.contract_project_funding') IS NULL
       OR to_regclass('public.vw_boh_authoritative_time_usage') IS NULL
       OR to_regclass('public.vw_boh_contract_time_totals') IS NULL THEN
        RAISE EXCEPTION 'Module 060 contract funding schema is incomplete';
    END IF;
    IF contract_time_approval_bucket('submitted', false) <> 'pending'
       OR contract_time_approval_bucket('manager_approved', true) <> 'pending'
       OR contract_time_approval_bucket('manager_approved', false) <> 'approved'
       OR contract_time_approval_bucket('pm_approved', true) <> 'approved'
       OR contract_time_approval_bucket('draft', false) <> 'excluded'
       OR contract_time_approval_bucket('pm_declined', true) <> 'excluded' THEN
        RAISE EXCEPTION 'Module 060 approval semantics are incorrect';
    END IF;
    IF EXISTS (SELECT time_entry_id FROM vw_boh_authoritative_time_usage
               GROUP BY time_entry_id HAVING COUNT(*) > 1) THEN
        RAISE EXCEPTION 'Contract time is being counted more than once';
    END IF;
END $$;
SELECT 'MODULE060_CONTRACT_FUNDING_SCHEMA_AND_APPROVALS=PASS' AS result;
