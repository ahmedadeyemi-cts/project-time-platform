-- Application rollback compatibility: deploy the previous API/UI, but retain
-- the additive funding schema and authoritative balance views from 060c.
-- The legacy API consumes the same balance columns. Restoring the ledger-only
-- view would release funds already used by automatically funded projects.
-- Keep live approval/edit/delete handling active throughout rollback.
-- Do not drop funding links, views, functions, or audit history.
BEGIN;
DO $$
BEGIN
    IF to_regclass('contract_project_funding') IS NULL
       OR to_regclass('vw_boh_authoritative_time_usage') IS NULL
       OR to_regclass('vw_boh_contract_time_totals') IS NULL THEN
        RAISE EXCEPTION '060c funding schema must remain installed for safe application rollback';
    END IF;
END $$;
COMMIT;
