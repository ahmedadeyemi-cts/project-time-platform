-- Accounting records are Pulse-owned and require no AI or external connector.
BEGIN;
ALTER TABLE clients ADD COLUMN IF NOT EXISTS salesforce_account_id text NOT NULL DEFAULT '';
CREATE TABLE IF NOT EXISTS accounting_engagement_profiles (
 project_id uuid PRIMARY KEY REFERENCES projects(project_id),
 contract_number text NOT NULL CHECK(length(contract_number) BETWEEN 1 AND 200),
 original_contract_amount numeric(14,2) NOT NULL CHECK(original_contract_amount >= 0),
 currency text NOT NULL DEFAULT 'USD' CHECK(currency='USD'),
 updated_by uuid NOT NULL REFERENCES app_users(user_id),
 updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS accounting_entries (
 entry_id uuid PRIMARY KEY,
 project_id uuid NOT NULL REFERENCES projects(project_id),
 kind text NOT NULL CHECK(kind IN ('contract_change','prepaid_funding','prepaid_usage','revenue')),
 amount numeric(14,2) NOT NULL CHECK(amount<>0),
 effective_date date NOT NULL,
 reference text NOT NULL CHECK(length(reference) BETWEEN 2 AND 500),
 reason text NOT NULL CHECK(length(reason) BETWEEN 5 AND 2000),
 actor_user_id uuid NOT NULL REFERENCES app_users(user_id),
 created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_accounting_entries_project ON accounting_entries(project_id,effective_date);
CREATE TABLE IF NOT EXISTS accounting_milestones (
 milestone_id uuid PRIMARY KEY,
 project_id uuid NOT NULL REFERENCES projects(project_id),
 name text NOT NULL CHECK(length(name) BETWEEN 2 AND 200),
 amount numeric(14,2) NOT NULL CHECK(amount>0),
 scheduled_date date NOT NULL,
 accepted_date date,
 acceptance_reference text NOT NULL DEFAULT '',
 accepted_by uuid REFERENCES app_users(user_id),
 billing_invoice_id uuid UNIQUE REFERENCES billing_invoices(billing_invoice_id),
 created_by uuid NOT NULL REFERENCES app_users(user_id),
 created_at timestamptz NOT NULL DEFAULT now(),
 CHECK((accepted_date IS NULL AND accepted_by IS NULL) OR
       (accepted_date IS NOT NULL AND accepted_by IS NOT NULL AND length(acceptance_reference) BETWEEN 2 AND 500))
);
CREATE INDEX IF NOT EXISTS ix_accounting_milestones_project ON accounting_milestones(project_id,scheduled_date);
CREATE TABLE IF NOT EXISTS accounting_profile_audit (
 audit_id uuid PRIMARY KEY, project_id uuid NOT NULL REFERENCES projects(project_id),
 actor_user_id uuid NOT NULL REFERENCES app_users(user_id),
 prior_state jsonb NOT NULL, new_state jsonb NOT NULL, recorded_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS accounting_operations (
 operation_id uuid PRIMARY KEY,project_id uuid NOT NULL REFERENCES projects(project_id),
 actor_user_id uuid NOT NULL REFERENCES app_users(user_id),request_hash text NOT NULL,
 request_json jsonb NOT NULL,created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS accounting_time_rates (
 rate_snapshot_id uuid PRIMARY KEY,project_id uuid NOT NULL REFERENCES projects(project_id),
 time_entry_id uuid NOT NULL REFERENCES time_entries(time_entry_id),unit_rate numeric(14,2) NOT NULL CHECK(unit_rate>0),
 work_date date NOT NULL,hours numeric(10,2) NOT NULL,reference text NOT NULL,reason text NOT NULL,
 actor_user_id uuid NOT NULL REFERENCES app_users(user_id),created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_accounting_time_rates_entry ON accounting_time_rates(time_entry_id,created_at DESC);
CREATE OR REPLACE VIEW accounting_engagement_report AS
SELECT p.project_id, p.project_code AS "engagementId", p.project_name AS "engagement",
 c.client_name AS "customer", c.salesforce_account_id AS "salesforceAccountId",
 to_jsonb(p)->>'salesforce_id_number' AS "salesforceOpportunityId",
 p.status AS "engagementStatus", to_jsonb(p)->>'contract_type' AS "contractType",
 p.start_date AS "startDate", p.end_date AS "endDate", f.contract_number AS "contractNumber",
 'USD'::text AS currency,md5(COALESCE(to_jsonb(f)::text,'') || COALESCE(c.salesforce_account_id,'')) AS "accountingVersion", f.original_contract_amount AS "originalContractAmount",
 COALESCE(e.changes,0) AS "approvedChanges",
 f.original_contract_amount+COALESCE(e.changes,0) AS "totalContractAmount",
 COALESCE(i.net,0) AS "pulseInvoiceNet", COALESCE(i.tax,0) AS "invoiceTax",
 COALESCE(i.gross,0) AS "pulseInvoiceTotal", COALESCE(i.external,0) AS "externalInvoiceTotal",
 COALESCE(i.gross,0)+COALESCE(i.external,0) AS "totalInvoiceAmount",
 f.original_contract_amount+COALESCE(e.changes,0)-COALESCE(i.net,0)-COALESCE(i.external,0) AS "remainingUnbilled",
 COALESCE(e.funding,0) AS "prepaidFunded", COALESCE(e.usage,0) AS "prepaidUsed",
 COALESCE(e.funding,0)-COALESCE(e.usage,0) AS "prepaidBalance",
 e.recognized AS "recognizedToDate", f.original_contract_amount+COALESCE(e.changes,0)-e.recognized AS "unrecognizedAmount",
 CASE WHEN f.project_id IS NULL THEN 'Contract amount not verified' ELSE 'Finance-maintained contract basis' END AS "dataStatus"
FROM projects p LEFT JOIN clients c ON c.client_id=p.client_id
LEFT JOIN accounting_engagement_profiles f ON f.project_id=p.project_id
LEFT JOIN LATERAL (
 SELECT sum(amount) FILTER(WHERE kind='contract_change') AS changes,
 sum(amount) FILTER(WHERE kind='prepaid_funding') AS funding,
 sum(amount) FILTER(WHERE kind='prepaid_usage') AS usage,
 sum(amount) FILTER(WHERE kind='revenue') AS recognized
 FROM accounting_entries WHERE project_id=p.project_id AND effective_date<=(now() AT TIME ZONE 'UTC')::date
) e ON true
LEFT JOIN LATERAL (
 SELECT sum(subtotal_amount+adjustment_amount) FILTER(WHERE invoice_status NOT IN ('draft','void','voided','cancelled','canceled')) AS net,
 sum(tax_amount) FILTER(WHERE invoice_status NOT IN ('draft','void','voided','cancelled','canceled')) AS tax,
 sum(total_amount) FILTER(WHERE invoice_status NOT IN ('draft','void','voided','cancelled','canceled')) AS gross,
 max((immutable_snapshot_json->>'previouslyBilledOutsidePulse')::numeric) AS external
 FROM billing_invoices WHERE project_id=p.project_id
) i ON true;
CREATE OR REPLACE VIEW accounting_milestone_report AS
SELECT m.project_id,m.milestone_id AS "milestoneId",p.project_code AS "engagementId",p.project_name AS engagement,
 c.client_name AS customer,c.salesforce_account_id AS "salesforceAccountId",'USD'::text AS currency,
 m.name AS milestone,m.amount AS "milestoneAmount",m.scheduled_date AS "scheduledDate",
 m.accepted_date AS "acceptanceDate",m.acceptance_reference AS "acceptanceReference",
 CASE WHEN m.billing_invoice_id IS NOT NULL THEN 'invoiced' WHEN m.accepted_date IS NOT NULL THEN 'accepted' ELSE 'scheduled' END AS status,
 i.invoice_number AS "invoiceNumber",i.invoice_status AS "invoiceStatus",i.invoice_date AS "invoiceDate"
FROM accounting_milestones m JOIN projects p ON p.project_id=m.project_id
LEFT JOIN clients c ON c.client_id=p.client_id LEFT JOIN billing_invoices i ON i.billing_invoice_id=m.billing_invoice_id;
CREATE OR REPLACE VIEW accounting_invoice_report AS
SELECT i.project_id,c.client_name AS customer,c.salesforce_account_id AS "salesforceAccountId",
 p.project_code AS "engagementId",p.project_name AS engagement,'USD'::text AS currency,
 i.invoice_number AS "invoiceNumber",i.invoice_date AS "invoiceDate",i.invoice_status AS "invoiceStatus",
 i.billing_period_start AS "billingPeriodStart",i.billing_period_end AS "billingPeriodEnd",
 i.subtotal_amount AS subtotal,i.adjustment_amount AS adjustments,i.tax_amount AS tax,i.total_amount AS total,
 m.name AS milestone,i.salesforce_id_snapshot AS "salesforceOpportunityId",
 CASE WHEN i.invoice_status IN ('draft','void','voided','cancelled','canceled') THEN false ELSE true END AS "includedInTotal"
FROM billing_invoices i JOIN projects p ON p.project_id=i.project_id LEFT JOIN clients c ON c.client_id=p.client_id
LEFT JOIN accounting_milestones m ON m.billing_invoice_id=i.billing_invoice_id;
CREATE OR REPLACE VIEW accounting_time_report AS
SELECT t.project_id,t.time_entry_id AS "timeEntryId",p.project_code AS "engagementId",p.project_name AS engagement,
 c.client_name AS customer,c.salesforce_account_id AS "salesforceAccountId",'USD'::text AS currency,
 COALESCE(l.work_date,r.work_date,t.work_date) AS "workDate",COALESCE(NULLIF(l.resource_name_snapshot,''),u.display_name) AS employee,COALESCE(NULLIF(l.task_name_snapshot,''),to_jsonb(task)->>'task_name') AS task,
 COALESCE(l.approved_hours,r.hours,t.hours) AS hours,t.status AS "approvalStatus",COALESCE(l.unit_rate,r.unit_rate) AS "historicalRate",COALESCE(l.line_amount,round(r.hours*r.unit_rate,2)) AS "billableAmount",
 CASE WHEN l.billing_invoice_id IS NOT NULL THEN 'Invoice snapshot' WHEN r.unit_rate IS NOT NULL THEN 'Finance-confirmed dated rate' ELSE 'Rate confirmation required' END AS "rateSource",
 CASE WHEN l.billing_invoice_id IS NOT NULL THEN 'billed' ELSE 'unbilled' END AS "billingStatus",
 i.invoice_number AS "invoiceNumber"
FROM time_entries t JOIN projects p ON p.project_id=t.project_id LEFT JOIN clients c ON c.client_id=p.client_id
JOIN app_users u ON u.user_id=t.user_id LEFT JOIN project_tasks task ON task.task_id=t.task_id
LEFT JOIN (billing_invoice_lines l JOIN billing_invoices bi ON bi.billing_invoice_id=l.billing_invoice_id
 AND bi.invoice_status NOT IN ('draft','void','voided','cancelled','canceled')) ON l.time_entry_id=t.time_entry_id
LEFT JOIN billing_invoices i ON i.billing_invoice_id=l.billing_invoice_id
LEFT JOIN LATERAL(SELECT * FROM accounting_time_rates WHERE time_entry_id=t.time_entry_id ORDER BY created_at DESC,rate_snapshot_id LIMIT 1) r ON true
WHERE t.billable=true AND t.hours>0;
CREATE OR REPLACE VIEW accounting_revenue_report AS
WITH monthly AS (
 SELECT project_id,date_trunc('month',effective_date)::date AS period,sum(amount) AS recognized,
 count(*) AS entries,string_agg(DISTINCT reference,'; ') AS refs
 FROM accounting_entries WHERE kind='revenue' GROUP BY project_id,date_trunc('month',effective_date)::date
)
SELECT m.project_id,p.project_code AS "engagementId",p.project_name AS engagement,c.client_name AS customer,
 c.salesforce_account_id AS "salesforceAccountId",'USD'::text AS currency,m.period AS "accountingPeriod",
 m.recognized AS "recognizedAmount",sum(m.recognized) OVER(PARTITION BY m.project_id ORDER BY m.period) AS "recognizedToDate",
 m.entries AS "entryCount",m.refs AS "approvalReferences",'Finance-approved entries'::text AS "recognitionBasis"
FROM monthly m JOIN projects p ON p.project_id=m.project_id LEFT JOIN clients c ON c.client_id=p.client_id;
CREATE OR REPLACE FUNCTION accounting_reject_mutation() RETURNS trigger LANGUAGE plpgsql AS $immutable$
BEGIN RAISE EXCEPTION 'Accounting history is append-only; record a signed adjustment instead.'; END $immutable$;
DO $triggers$ DECLARE t text; BEGIN
 FOREACH t IN ARRAY ARRAY['accounting_entries','accounting_profile_audit','accounting_operations','accounting_time_rates'] LOOP
  EXECUTE format('DROP TRIGGER IF EXISTS accounting_history_immutable ON %I',t);
  EXECUTE format('CREATE TRIGGER accounting_history_immutable BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION accounting_reject_mutation()',t);
 END LOOP;
END $triggers$;
DO $grant$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='ptp_app') THEN
  GRANT SELECT,INSERT,UPDATE ON accounting_engagement_profiles,accounting_milestones TO ptp_app;
  GRANT SELECT,INSERT ON accounting_entries,accounting_profile_audit,accounting_time_rates,accounting_operations TO ptp_app;
  GRANT SELECT ON accounting_engagement_report,accounting_milestone_report,accounting_time_report,accounting_revenue_report,accounting_invoice_report TO ptp_app;
  GRANT UPDATE(salesforce_account_id) ON clients TO ptp_app;
 END IF;
END $grant$;
INSERT INTO schema_migrations(migration_id,description) VALUES('135_accounting_engagement_reporting','Accounting summary, milestone billing, historical time rates and Finance-approved monthly revenue') ON CONFLICT DO NOTHING;
COMMIT;
