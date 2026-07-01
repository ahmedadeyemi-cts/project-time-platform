-- 028 SOW-Aware AI Time Entry Generator
-- Non-destructive schema for future persistence of SOW/GSD scope alignment reviews.
-- The runtime API is adapter-ready and can operate before 024-027 final tables are merged.

CREATE TABLE IF NOT EXISTS projectpulse_sow_scope_reviews (
    review_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    customer_name text NOT NULL DEFAULT '',
    project_name text NOT NULL DEFAULT '',
    source_sow_artifact_id uuid NULL,
    source_gsd_artifact_id uuid NULL,
    outcome text NOT NULL DEFAULT 'needs_review',
    alignment_score integer NOT NULL DEFAULT 0,
    ready_check_count integer NOT NULL DEFAULT 0,
    risk_check_count integer NOT NULL DEFAULT 0,
    missing_context_count integer NOT NULL DEFAULT 0,
    claude_prompt text NULL,
    evidence_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_by_user_id uuid NULL,
    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS projectpulse_sow_scope_review_items (
    review_item_id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    review_id uuid NOT NULL REFERENCES projectpulse_sow_scope_reviews(review_id) ON DELETE CASCADE,
    time_entry_reference text NULL,
    generated_time_entry_text text NOT NULL DEFAULT '',
    scope_alignment text NOT NULL DEFAULT 'needs_review',
    risk_level text NOT NULL DEFAULT 'medium',
    reason text NOT NULL DEFAULT '',
    recommended_action text NOT NULL DEFAULT '',
    created_at_utc timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_projectpulse_sow_scope_reviews_created_at
    ON projectpulse_sow_scope_reviews(created_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_projectpulse_sow_scope_review_items_review_id
    ON projectpulse_sow_scope_review_items(review_id);
