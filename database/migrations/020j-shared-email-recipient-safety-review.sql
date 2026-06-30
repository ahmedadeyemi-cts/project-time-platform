-- 020J Shared Email Recipient Safety Review
-- Prevents real batch email sends until recipients are reviewed and approved.

CREATE TABLE IF NOT EXISTS system_email_recipient_safety_rules (
    system_email_recipient_safety_rule_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    rule_code text NOT NULL UNIQUE,
    rule_name text NOT NULL,
    rule_description text NOT NULL,
    risk_level text NOT NULL,
    blocks_send boolean NOT NULL DEFAULT false,
    is_active boolean NOT NULL DEFAULT true,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS system_email_recipient_safety_reviews (
    system_email_recipient_safety_review_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    consumer_key text NOT NULL,
    scenario text NOT NULL,
    delivery_mode text NOT NULL,
    provider text NOT NULL,
    review_status text NOT NULL DEFAULT 'generated',
    total_recipient_count integer NOT NULL DEFAULT 0,
    blocked_count integer NOT NULL DEFAULT 0,
    warning_count integer NOT NULL DEFAULT 0,
    clear_count integer NOT NULL DEFAULT 0,
    generated_by_user_id uuid NULL,
    generated_by_email text NULL,
    generated_at timestamptz NOT NULL DEFAULT now(),
    approved_by_user_id uuid NULL,
    approved_by_email text NULL,
    approved_at timestamptz NULL,
    expires_at timestamptz NULL,
    review_message text NULL
);

CREATE INDEX IF NOT EXISTS idx_system_email_recipient_safety_reviews_latest
    ON system_email_recipient_safety_reviews(consumer_key, scenario, generated_at DESC);

CREATE INDEX IF NOT EXISTS idx_system_email_recipient_safety_reviews_status
    ON system_email_recipient_safety_reviews(review_status);

CREATE TABLE IF NOT EXISTS system_email_recipient_safety_review_items (
    system_email_recipient_safety_review_item_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    review_id uuid NOT NULL REFERENCES system_email_recipient_safety_reviews(system_email_recipient_safety_review_id) ON DELETE CASCADE,
    recipient_email text NOT NULL,
    recipient_display_name text NULL,
    recipient_kind text NOT NULL DEFAULT 'primary',
    manager_email text NULL,
    cc_emails text[] NOT NULL DEFAULT ARRAY[]::text[],
    risk_codes text[] NOT NULL DEFAULT ARRAY[]::text[],
    risk_level text NOT NULL DEFAULT 'clear',
    safety_status text NOT NULL DEFAULT 'clear',
    block_send boolean NOT NULL DEFAULT false,
    details text NOT NULL DEFAULT '',
    source_payload jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_system_email_recipient_safety_review_items_review
    ON system_email_recipient_safety_review_items(review_id);

CREATE INDEX IF NOT EXISTS idx_system_email_recipient_safety_review_items_status
    ON system_email_recipient_safety_review_items(safety_status, block_send);

INSERT INTO system_email_recipient_safety_rules (
    rule_code,
    rule_name,
    rule_description,
    risk_level,
    blocks_send,
    is_active
)
VALUES
    ('EMPTY_OR_INVALID_RECIPIENT', 'Empty or invalid recipient', 'Recipient email is empty or does not contain an @ symbol.', 'high', true, true),
    ('LOCAL_OR_TEST_DOMAIN', 'Local or test domain', 'Recipient, manager, or CC email ends in .local or another non-routable local/test domain.', 'high', true, true),
    ('DEMO_OR_TEST_USER', 'Demo or test user', 'Recipient appears to be a demo/test user based on email or display name.', 'high', true, true),
    ('DUPLICATE_DISPLAY_NAME', 'Duplicate display name', 'More than one recipient has the same display name, which can indicate duplicate test/user accounts.', 'medium', true, true),
    ('DUPLICATE_EMAIL', 'Duplicate email', 'The same recipient email appears more than once in the generated send list.', 'medium', true, true),
    ('MISSING_MANAGER_EMAIL', 'Missing manager email', 'Recipient is missing a manager email for escalation/copy workflow.', 'medium', false, true),
    ('NON_ROUTABLE_MANAGER_OR_CC', 'Non-routable manager or CC', 'Manager or CC email is non-routable, which would prevent manager copy or escalation delivery.', 'high', true, true),
    ('EXTERNAL_DOMAIN_REVIEW', 'External domain review', 'Recipient email is outside the expected US Signal / OneNeck domain set and requires review.', 'medium', false, true)
ON CONFLICT (rule_code) DO UPDATE
SET
    rule_name = EXCLUDED.rule_name,
    rule_description = EXCLUDED.rule_description,
    risk_level = EXCLUDED.risk_level,
    blocks_send = EXCLUDED.blocks_send,
    is_active = EXCLUDED.is_active,
    updated_at = now();

DO $$
DECLARE
    role_record record;
BEGIN
    FOR role_record IN
        SELECT rolname
        FROM pg_roles
        WHERE rolcanlogin = true
          AND rolname <> 'postgres'
    LOOP
        EXECUTE format('GRANT USAGE ON SCHEMA public TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.system_email_recipient_safety_rules TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.system_email_recipient_safety_reviews TO %I', role_record.rolname);
        EXECUTE format('GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE public.system_email_recipient_safety_review_items TO %I', role_record.rolname);
    END LOOP;
END $$;
