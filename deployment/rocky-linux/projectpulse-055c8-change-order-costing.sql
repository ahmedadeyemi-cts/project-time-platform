-- 055C.8 - Change order costing management
-- Sidecar model. Does not overwrite original project baseline/cost history.

BEGIN;

CREATE TABLE IF NOT EXISTS work_register_change_orders (
    work_register_change_order_id uuid PRIMARY KEY,
    project_id uuid NOT NULL,
    change_order_number varchar(120) NOT NULL DEFAULT '',
    title text NOT NULL DEFAULT '',
    status varchar(40) NOT NULL DEFAULT 'approved',
    change_order_date date NOT NULL DEFAULT CURRENT_DATE,
    approval_reference text NOT NULL DEFAULT '',
    reason text NOT NULL DEFAULT '',
    total_amount numeric(14,2) NOT NULL DEFAULT 0,
    created_by_user_id uuid NULL,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS work_register_change_order_lines (
    work_register_change_order_line_id uuid PRIMARY KEY,
    work_register_change_order_id uuid NOT NULL,
    project_id uuid NOT NULL,
    line_type varchar(80) NOT NULL,
    description text NOT NULL DEFAULT '',
    quantity numeric(14,2) NOT NULL DEFAULT 0,
    unit_rate numeric(14,2) NOT NULL DEFAULT 0,
    amount numeric(14,2) NOT NULL DEFAULT 0,
    billable boolean NOT NULL DEFAULT TRUE,
    utilization_eligible boolean NOT NULL DEFAULT TRUE,
    created_at timestamp with time zone NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_work_register_change_orders_project
    ON work_register_change_orders(project_id);

CREATE INDEX IF NOT EXISTS idx_work_register_change_orders_date
    ON work_register_change_orders(change_order_date DESC);

CREATE INDEX IF NOT EXISTS idx_work_register_change_order_lines_project
    ON work_register_change_order_lines(project_id);

CREATE INDEX IF NOT EXISTS idx_work_register_change_order_lines_order
    ON work_register_change_order_lines(work_register_change_order_id);

COMMIT;
