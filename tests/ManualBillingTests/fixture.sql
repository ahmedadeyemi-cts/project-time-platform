-- Disposable fixture adds only pre-existing dependencies of the actual billing migrations.
ALTER TABLE projects ADD COLUMN contract_type text NOT NULL DEFAULT 'Fixed Price',
    ADD COLUMN project_coordinator_user_id uuid REFERENCES app_users,
    ADD COLUMN certinia_id_number text NOT NULL DEFAULT '',
    ADD COLUMN sell_quote_number text NOT NULL DEFAULT '',
    ADD COLUMN salesforce_id_number text NOT NULL DEFAULT '';
CREATE TABLE app_roles(app_role_id uuid PRIMARY KEY, role_code text, is_active boolean);
CREATE TABLE app_user_role_assignments(user_id uuid, app_role_id uuid, is_active boolean);
CREATE TABLE work_register_project_lifecycle(project_id uuid PRIMARY KEY, is_archived boolean NOT NULL DEFAULT false);
CREATE TABLE work_rate_cards(rate_card_id uuid PRIMARY KEY, rate_card_code text, rate_card_name text,
 status text, effective_start_date date, effective_end_date date, client_id uuid);
CREATE TABLE work_rate_card_lines(rate_line_id uuid PRIMARY KEY,rate_card_id uuid REFERENCES work_rate_cards,
 sku_code text, display_name text, description text, labor_category text, time_type text, unit_type text,
 rate_amount numeric, billable_default boolean, is_active boolean, display_order int);
CREATE TABLE work_register_change_history(work_register_change_history_id uuid PRIMARY KEY,work_id uuid,action text,
    change_summary text,changed_fields_csv text,changed_by_user_id uuid,old_value_json jsonb,new_value_json jsonb,changed_at timestamptz);

-- Existing customer-source read-model dependencies; no live credentials are seeded.
CREATE TABLE crm_integration_providers(provider_key text PRIMARY KEY, provider_name text, provider_type text,
 auth_model text, base_url text, api_key_header text, api_key_prefix text, record_lookup_url_template text,
 import_mapping_json jsonb, is_enabled boolean, availability_status text);
CREATE TABLE crm_integration_credentials(provider_key text, credential_kind text);
CREATE TABLE customer_directory_source_authority(customer_source_authority_id int PRIMARY KEY,
 source_mode text, provider_key text, updated_at timestamptz DEFAULT now());
INSERT INTO customer_directory_source_authority(customer_source_authority_id,source_mode,provider_key) VALUES(1,'sell','connectwise_sell');

-- Current expense evidence is rechecked inside the invoice transaction.
CREATE TABLE project_expense_uploads(project_expense_upload_id uuid PRIMARY KEY,project_id uuid,
 is_current boolean NOT NULL DEFAULT true,deleted_at timestamptz,period_start date,period_end date,
 uploaded_at timestamptz NOT NULL DEFAULT now(),reimbursable_amount numeric NOT NULL DEFAULT 0);
