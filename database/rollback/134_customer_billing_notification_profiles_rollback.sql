BEGIN;

DELETE FROM enterprise_notification_policies
WHERE policy_code = 'CUSTOMER_INVOICE_WORKFLOW_ACTION_REQUIRED'
  AND producer_contract = 'module_021_042_customer_billing_profile_v1';

DELETE FROM schema_migrations
WHERE migration_id = '134_customer_billing_notification_profiles';

DROP TABLE IF EXISTS customer_billing_notification_profile_audit;
DROP TABLE IF EXISTS customer_billing_notification_profiles;

COMMIT;
