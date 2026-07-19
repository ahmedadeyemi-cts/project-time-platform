# Module 073 API Contract

| Method | Route | Authority | Result |
|---|---|---|---|
| `GET` | `/api/sales-coverage/capabilities` | Authenticated | Role and persistence boundary |
| `GET` | `/api/sales-coverage/source-signals` | Authenticated | Existing project/intake AE and SA relationships |
| `GET` | `/api/sales-coverage/identity-options` | Editor roles | Categorized stable identities |
| `POST` | `/api/sales-coverage/validate` | Editor roles | Non-persistent normalized draft and row errors |

The POST endpoint is computational only. It returns `persistencePerformed=false` and performs no SQL or external mutation.

Required draft fields are AE, primary Resale Operations, Solution Architect, territory, team, and effective start. Backup Resale Operations and effective end are optional; primary/backup must differ and end cannot precede start.
