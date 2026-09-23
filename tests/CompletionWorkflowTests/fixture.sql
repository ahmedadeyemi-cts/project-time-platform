-- Disposable fixture for existing operational tables absent from migration 001.
-- Real 001 + 038 are applied by the test; no live database is accessed.
ALTER TABLE projects ADD COLUMN contract_type text NOT NULL DEFAULT 'Time and Materials';
CREATE TABLE app_roles(app_role_id uuid PRIMARY KEY, role_code text, is_active boolean);
CREATE TABLE app_user_role_assignments(user_id uuid, app_role_id uuid, is_active boolean);
CREATE TABLE work_register_project_lifecycle(project_id uuid PRIMARY KEY, is_archived boolean NOT NULL DEFAULT false);
CREATE TABLE project_billing_profiles(project_id uuid PRIMARY KEY, purchase_order_required boolean DEFAULT false);
CREATE TABLE project_purchase_orders(project_purchase_order_id uuid PRIMARY KEY,project_id uuid, authorized_amount numeric);
CREATE TABLE project_expense_uploads(project_expense_upload_id uuid PRIMARY KEY,project_id uuid,source_sha256 text,
    version_number integer,total_amount numeric,reimbursable_amount numeric,billing_treatment text,is_current boolean,deleted_at timestamptz);
CREATE TABLE billing_invoices(billing_invoice_id uuid PRIMARY KEY,project_id uuid,invoice_number text,invoice_type text,
    invoice_status text,total_amount numeric DEFAULT 100,updated_at timestamptz DEFAULT now());
CREATE TABLE billing_invoice_lines(billing_invoice_line_id uuid PRIMARY KEY,billing_invoice_id uuid,time_entry_id uuid,line_amount numeric DEFAULT 100);
CREATE TABLE external_integration_outbox(outbox_id uuid PRIMARY KEY,system_code text,local_entity text,local_entity_id uuid,
    delivery_status text,payload_json jsonb DEFAULT '{}'::jsonb);
CREATE TABLE work_register_change_history(work_register_change_history_id uuid PRIMARY KEY,work_id uuid,action text,
    change_summary text,changed_fields_csv text,changed_by_user_id uuid,old_value_json jsonb,new_value_json jsonb,changed_at timestamptz);
CREATE TABLE billing_invoice_events(billing_invoice_event_id uuid PRIMARY KEY,billing_invoice_id uuid,event_type text,
    prior_status text,new_status text,event_reason text,actor_user_id uuid,event_json jsonb,created_at timestamptz);
