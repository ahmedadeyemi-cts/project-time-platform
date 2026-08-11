-- Pulse / Celar AI canonical runtime labels migration 082.
--
-- This is intentionally forward-only. It updates mutable, human-facing labels
-- without rewriting migration history, immutable audit evidence, durable source
-- system values, API headers, configuration keys, or cryptographic associated
-- data. Stable compatibility identifiers retain their existing ProjectPulse and
-- PULSE_AI names.

BEGIN;

DO $pulse082_prerequisites$
BEGIN
    IF to_regclass('public.schema_migrations') IS NULL THEN
        RAISE EXCEPTION 'Migration 082 requires public.schema_migrations.';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM schema_migrations
        WHERE migration_id = '075_pulse_product_rebrand'
    ) THEN
        RAISE EXCEPTION 'Migration 082 requires 075_pulse_product_rebrand.';
    END IF;
END;
$pulse082_prerequisites$;

-- Update only explicitly approved display columns. Codes, keys, routes, JSON,
-- source-system values, audit tables, and historical policy evidence are not in
-- this allowlist.
DO $pulse082_display_columns$
DECLARE
    target RECORD;
    has_updated_at BOOLEAN;
    update_sql TEXT;
BEGIN
    FOR target IN
        SELECT configured.table_name, configured.column_name
        FROM (VALUES
            ('app_roles', 'role_name'),
            ('app_roles', 'role_description'),
            ('app_permissions', 'permission_name'),
            ('app_permissions', 'permission_description'),
            ('app_feature_catalog', 'feature_name'),
            ('app_feature_catalog', 'feature_description'),
            ('auth_identity_providers', 'provider_name'),
            ('reminder_rules', 'rule_name'),
            ('reminder_rules', 'subject_template'),
            ('reminder_rules', 'body_template'),
            ('enterprise_notification_policies', 'policy_name'),
            ('enterprise_notification_policies', 'subject_template'),
            ('enterprise_notification_policies', 'text_template'),
            ('system_email_provider_consumers', 'consumer_name'),
            ('system_email_provider_consumers', 'consumer_description'),
            ('signed_sow_handoff_notification_templates', 'subject_template'),
            ('signed_sow_handoff_notification_templates', 'body_template'),
            ('reporting_external_connection_catalog', 'connection_name'),
            ('crm_integration_field_mappings', 'projectpulse_destination')
        ) AS configured(table_name, column_name)
    LOOP
        IF to_regclass(format('public.%I', target.table_name)) IS NULL
           OR NOT EXISTS (
                SELECT 1
                FROM pg_attribute
                WHERE attrelid = to_regclass(format('public.%I', target.table_name))
                  AND attname = target.column_name
                  AND NOT attisdropped
           ) THEN
            CONTINUE;
        END IF;

        SELECT EXISTS (
            SELECT 1
            FROM pg_attribute
            WHERE attrelid = to_regclass(format('public.%I', target.table_name))
              AND attname = 'updated_at'
              AND NOT attisdropped
        )
        INTO has_updated_at;

        update_sql := format(
            'UPDATE public.%I '
            || 'SET %I = replace(replace(replace(%I, ''Project Pulse'', ''Pulse''), ''ProjectPulse'', ''Pulse''), ''Pulse AI'', ''Celar AI'')%s '
            || 'WHERE %I LIKE ''%%Project Pulse%%'' OR %I LIKE ''%%ProjectPulse%%'' OR %I LIKE ''%%Pulse AI%%''',
            target.table_name,
            target.column_name,
            target.column_name,
            CASE WHEN has_updated_at THEN ', updated_at = NOW()' ELSE '' END,
            target.column_name,
            target.column_name,
            target.column_name
        );

        EXECUTE update_sql;
    END LOOP;
END;
$pulse082_display_columns$;

-- These two records are deterministic demo/foundation data, not customer data.
-- Restricting by immutable seed identifiers prevents broad project or expense
-- content rewriting.
DO $pulse082_seed_labels$
BEGIN
    IF to_regclass('public.projects') IS NOT NULL
       AND EXISTS (
            SELECT 1 FROM pg_attribute
            WHERE attrelid = to_regclass('public.projects')
              AND attname = 'project_description'
              AND NOT attisdropped
       ) THEN
        UPDATE projects
        SET project_description = replace(replace(project_description, 'Project Pulse', 'Pulse'), 'ProjectPulse', 'Pulse')
        WHERE project_code = 'USS-PSA-2026'
          AND project_description LIKE '%Project Pulse%';
    END IF;

    IF to_regclass('public.expense_reports') IS NOT NULL
       AND EXISTS (
            SELECT 1 FROM pg_attribute
            WHERE attrelid = to_regclass('public.expense_reports')
              AND attname = 'report_title'
              AND NOT attisdropped
       ) THEN
        UPDATE expense_reports
        SET report_title = replace(replace(report_title, 'Project Pulse', 'Pulse'), 'ProjectPulse', 'Pulse')
        WHERE report_number = 'EXP-2026-0001'
          AND report_title LIKE '%Project Pulse%';
    END IF;
END;
$pulse082_seed_labels$;

-- Preserve conversation content and immutable answer evidence. Only the mutable
-- placeholder title and future default are canonicalized.
DO $pulse082_conversation_default$
BEGIN
    IF to_regclass('public.pulse_ai_conversations') IS NOT NULL
       AND EXISTS (
            SELECT 1 FROM pg_attribute
            WHERE attrelid = to_regclass('public.pulse_ai_conversations')
              AND attname = 'title'
              AND NOT attisdropped
       ) THEN
        ALTER TABLE pulse_ai_conversations
            ALTER COLUMN title SET DEFAULT 'New Celar AI conversation';

        UPDATE pulse_ai_conversations
        SET title = 'New Celar AI conversation'
        WHERE title = 'New Pulse AI conversation';
    END IF;
END;
$pulse082_conversation_default$;

-- Function names are compatibility contracts and remain unchanged. Only their
-- human-readable messages and the canonical new-conversation sentinel change.
DO $pulse082_function_labels$
BEGIN
    IF to_regprocedure('public.pulse_ai_052_block_processing_event_mutation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION pulse_ai_052_block_processing_event_mutation()
            RETURNS TRIGGER
            LANGUAGE plpgsql
            AS $body$
            BEGIN
                RAISE EXCEPTION 'Celar AI document processing event evidence is immutable.';
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.pulse_ai_053_block_retrieval_event_mutation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION pulse_ai_053_block_retrieval_event_mutation()
            RETURNS TRIGGER
            LANGUAGE plpgsql
            AS $body$
            BEGIN
                RAISE EXCEPTION 'Celar AI retrieval event evidence is immutable.';
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.pulse_ai_054_block_tool_event_mutation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION pulse_ai_054_block_tool_event_mutation()
            RETURNS TRIGGER
            LANGUAGE plpgsql
            AS $body$
            BEGIN
                RAISE EXCEPTION 'Celar AI system tool evidence is immutable.';
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.pulse_ai_054_increment_conversation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION pulse_ai_054_increment_conversation()
            RETURNS TRIGGER
            LANGUAGE plpgsql
            AS $body$
            BEGIN
                UPDATE pulse_ai_conversations
                SET message_count = message_count + 1,
                    last_message_at = NEW.created_at,
                    updated_at = NOW(),
                    title = CASE
                        WHEN NEW.role = 'user'
                         AND (title IN ('New Celar AI conversation', 'New Pulse AI conversation') OR BTRIM(title) = '')
                        THEN LEFT(REGEXP_REPLACE(BTRIM(NEW.message_text), '\s+', ' ', 'g'), 240)
                        ELSE title
                    END
                WHERE pulse_ai_conversation_id = NEW.pulse_ai_conversation_id;
                RETURN NEW;
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.projectpulse048_block_system_audit_mutation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION projectpulse048_block_system_audit_mutation()
            RETURNS TRIGGER LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'Pulse system audit evidence is immutable.';
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.projectpulse_056_block_immutable_mutation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION projectpulse_056_block_immutable_mutation()
            RETURNS TRIGGER LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'Pulse migration 056 evidence is immutable.';
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.projectpulse_056a_block_immutable_mutation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION projectpulse_056a_block_immutable_mutation()
            RETURNS TRIGGER LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'Pulse migration 056A scope evidence is immutable.';
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.projectpulse_062_block_evidence_mutation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION projectpulse_062_block_evidence_mutation()
            RETURNS TRIGGER LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'Pulse migration 062 evidence is immutable.';
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.projectpulse_063_block_evidence_mutation()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION projectpulse_063_block_evidence_mutation()
            RETURNS TRIGGER LANGUAGE plpgsql AS $body$
            BEGIN
                RAISE EXCEPTION 'Pulse migration 063 evidence is immutable.';
            END;
            $body$
        $definition$;
    END IF;

    IF to_regprocedure('public.projectpulse066_guard_issued_project_number()') IS NOT NULL THEN
        EXECUTE $definition$
            CREATE OR REPLACE FUNCTION projectpulse066_guard_issued_project_number()
            RETURNS TRIGGER
            LANGUAGE plpgsql
            AS $body$
            DECLARE
                issuance_authorized BOOLEAN :=
                    coalesce(current_setting('projectpulse.project_number_issuance', TRUE), '') = 'on';
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    IF NEW.project_code ~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$'
                       AND NOT issuance_authorized THEN
                        RAISE EXCEPTION 'Pulse project numbers may be issued only by the governed Module 055D database workflow.';
                    END IF;
                    RETURN NEW;
                END IF;

                IF NEW.project_code IS NOT DISTINCT FROM OLD.project_code THEN
                    RETURN NEW;
                END IF;

                IF OLD.project_code ~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$' THEN
                    RAISE EXCEPTION 'Issued Pulse project numbers are immutable.';
                END IF;

                IF NEW.project_code ~ '^(PRO|SR|IQS|INT|PRES)-[A-Z0-9]{8}$'
                   AND NOT issuance_authorized THEN
                    RAISE EXCEPTION 'Pulse project numbers may be issued only by the governed Module 055D database workflow.';
                END IF;

                RETURN NEW;
            END;
            $body$
        $definition$;
    END IF;
END;
$pulse082_function_labels$;

INSERT INTO schema_migrations(migration_id, description, applied_at)
VALUES (
    '082_pulse_celar_ai_canonical_labels',
    'Canonical Pulse and Celar AI labels for mutable runtime catalogs, templates, and active database function messages; compatibility identifiers and historical evidence remain unchanged.',
    NOW()
)
ON CONFLICT (migration_id) DO NOTHING;

COMMIT;
