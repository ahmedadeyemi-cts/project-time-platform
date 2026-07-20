# ProjectPulse Native Administration — Modules 064–070 and 073–074

## PROJECTPULSE_NATIVE_ADMINISTRATION_MIGRATION_032

Checkpoint B2 adds one shared, versioned ProjectPulse PostgreSQL administration layer for the remaining Modules 064–070 and 073–074.

The shared layer provides:

- schema-driven edit and save controls on each module route;
- actual-session authorization with explicit `SUPER_ADMINISTRATOR` and `ADMINISTRATOR` authority;
- preservation of existing delegated editor roles where already governed;
- View-As write blocking;
- optimistic concurrency through expected revision numbers;
- immutable revision history and restore actions;
- sanitized audit records in `projectpulse_module_audit_events`;
- Module 062/app_users identity dropdowns where a module stores user ownership;
- rejection of usable secret, password, token, credential, API-key, private-key, and connection-string fields.

This source does **not** activate Entra, Key Vault, AI-provider secrets, SMTP delivery, or any external system. Migration 032 is committed as reviewed source and remains unapplied until the separate test-database gate.

## Module document shapes

| Module | Native document |
|---|---|
| 064 | Non-secret AI routing and model-selection metadata |
| 065 | Entra application and secret-rotation metadata; no usable secret value |
| 066 | FlowHive plan, baseline, WBS, dependency, owner, and collaboration records |
| 067 | Non-secret global mail target and sender metadata; delivery remains locked |
| 068 | Curated architecture component records |
| 069 | Qualification and certification records |
| 070 | Saved capacity forecast scenarios |
| 073 | Effective-dated sales coverage alignments |
| 074 | OEM and vendor directory records |

## Release state

- Checkpoint A: complete and committed.
- Checkpoint B1: complete and committed.
- Checkpoint B2: source implementation and validation in this commit.
- `MIGRATION_032_APPLIED=NO`
- `DATABASE_CHANGED=NO`
- `DEPLOYED=NO`
