BEGIN;

CREATE OR REPLACE FUNCTION projectpulse_restrict_manual_cloud_domain_user_insert()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
    normalized_email TEXT;
    normalized_source_provider TEXT;
BEGIN
    normalized_email := lower(coalesce(NEW.email, ''));
    normalized_source_provider := upper(coalesce(NEW.source_provider, ''));

    IF normalized_email = '' THEN
        RAISE EXCEPTION 'User email is required.';
    END IF;

    -- Manual local accounts are allowed only for the internal break-glass/local domain.
    IF normalized_email LIKE '%@ussignal.local' THEN
        RETURN NEW;
    END IF;

    -- Test Entra users must come from test Entra SSO/sync and must have an Entra object id.
    IF normalized_email LIKE '%@onenecklab.com' THEN
        IF normalized_source_provider = 'ENTRA_ID_TEST'
           AND NEW.entra_object_id IS NOT NULL THEN
            RETURN NEW;
        END IF;

        RAISE EXCEPTION 'Manual creation of @onenecklab.com users is blocked. Import this user through Entra test sync or SSO JIT.';
    END IF;

    -- Production Entra users must come from production Entra sync and must have an Entra object id.
    IF normalized_email LIKE '%@ussignal.com' THEN
        IF normalized_source_provider = 'ENTRA_ID'
           AND NEW.entra_object_id IS NOT NULL THEN
            RETURN NEW;
        END IF;

        RAISE EXCEPTION 'Manual creation of @ussignal.com users is blocked. Import this user through production Entra sync.';
    END IF;

    RAISE EXCEPTION 'Manual user creation is restricted to @ussignal.local accounts only. Cloud users must be imported from Entra.';
END;
$$;

DROP TRIGGER IF EXISTS trg_projectpulse_restrict_manual_cloud_domain_user_insert ON app_users;

CREATE TRIGGER trg_projectpulse_restrict_manual_cloud_domain_user_insert
BEFORE INSERT ON app_users
FOR EACH ROW
EXECUTE FUNCTION projectpulse_restrict_manual_cloud_domain_user_insert();

INSERT INTO schema_migrations (migration_id, description, applied_at)
VALUES (
    '019n_restrict_manual_cloud_domain_users',
    'Restrict manual user creation to local accounts and require Entra users to come from SSO or Graph sync',
    NOW()
)
ON CONFLICT (migration_id) DO UPDATE
SET description = EXCLUDED.description,
    applied_at = EXCLUDED.applied_at;

COMMIT;
