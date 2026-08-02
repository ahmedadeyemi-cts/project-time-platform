# Celar AI migration 061 registration hotfix

## Purpose

Migration `061_celar_ai_capability_routing` previously created its governed capability-routing and private-model schema but did not register itself in `schema_migrations`. The consolidated Test migration transaction therefore applied migrations 060–064 successfully and then failed its final registration verification.

## Correction

- Migration 061 now creates `schema_migrations` when running in an isolated validation database.
- Migration 061 inserts or updates the canonical `061_celar_ai_capability_routing` registration after the schema and default routes are complete.
- The rollback removes the registration together with the migration-owned tables.
- The focused test validates initial application, repeated application, registration uniqueness, rollback, and reapplication.

## Deployment effect

This is an idempotent registration repair. Reapplying migrations 060–064 to Test preserves the additive schema already created by the first transaction and records the missing migration 061 evidence before the exact merged application release is deployed.

No Production deployment is included.
