# Module 065 Rotation State Machine

| State | Entry requirement | Allowed next action |
|---|---|---|
| `prepared` | Valid non-secret plan and actual authorized actor | Request approval or stage when policy allows one actor |
| `awaiting_approval` | Policy requires second actor | Approve or reject |
| `approved` | Eligible different actor approved | Stage write-only secret |
| `secret_staged` | Approved adapter stored version without returning value | Run token-acquisition test |
| `validated` | Sanitized test succeeded | Explicit activation |
| `active_overlap` | Validated version activated; prior version retained | Complete overlap or roll back |
| `active` | Approved overlap window completed | Future rotation |
| `rolled_back` | Prior governed version restored | Review evidence and prepare replacement |
| `failed` | Sanitized terminal failure | Review evidence; prepare/retry only under policy |

## Invariants

- The initiating actor cannot satisfy a required second approval.
- Approval cannot be inferred from role alone; a required approval is an explicit recorded decision.
- Initiator and required second approver are different actual users.
- Secret staging cannot produce a response containing the secret or store reference.
- Validation succeeds only when the approved adapter acquires a token using the staged version; the token is discarded.
- Activation requires the validated state and an explicit action.
- Rollback is available during overlap and targets an approved previous version.
- Every transition has a correlation identifier and sanitized timestamped evidence.
- Adapter implementations must enforce transitions atomically, not trust the frontend's displayed state.
- Activation cannot precede successful validation.
