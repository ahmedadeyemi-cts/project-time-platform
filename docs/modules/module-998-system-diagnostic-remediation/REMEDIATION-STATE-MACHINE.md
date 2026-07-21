# Module 998 Remediation State Machine

```text
observation
    ↓
prepare (locked)
    ↓ separated authorization
approve (locked)
    ↓ bounded non-production target
stage (locked)
    ↓ production authorization
promote (locked)
    ↓ sanitized verification
verify (locked)
   ↙ ↘
rollback (locked)   close (locked)
    ↓
verify → close
```

## Invariants

- The requester cannot self-approve.
- Approval cannot bypass staging or verification.
- View-As never supplies authority.
- Every proposed operation must declare impact, target, owner, evidence,
  verification, time limit, and rollback plan.
- Production promotion, containment, and rollback require separate authority.
- Failure to prove a gate means locked, not implied approval.
- Module 998 source contains no execution adapter and no durable workflow store.

The current implementation returns `423 operation_locked` for every registered
operation before reading a body or changing state.
