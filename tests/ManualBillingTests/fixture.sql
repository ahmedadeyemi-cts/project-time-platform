-- Disposable fixture adds only pre-existing dependencies of the actual billing migrations.
ALTER TABLE projects ADD COLUMN contract_type text NOT NULL DEFAULT 'Time and Materials',
    ADD COLUMN project_coordinator_user_id uuid REFERENCES app_users,
    ADD COLUMN certinia_id_number text NOT NULL DEFAULT '',
    ADD COLUMN sell_quote_number text NOT NULL DEFAULT '',
    ADD COLUMN salesforce_id_number text NOT NULL DEFAULT '';
CREATE TABLE app_roles(app_role_id uuid PRIMARY KEY, role_code text, is_active boolean);
CREATE TABLE app_user_role_assignments(user_id uuid, app_role_id uuid, is_active boolean);
CREATE TABLE work_register_project_lifecycle(project_id uuid PRIMARY KEY, is_archived boolean NOT NULL DEFAULT false);
CREATE TABLE work_rate_cards(rate_card_id uuid PRIMARY KEY);
CREATE TABLE work_rate_card_lines(rate_line_id uuid PRIMARY KEY,rate_card_id uuid REFERENCES work_rate_cards);
