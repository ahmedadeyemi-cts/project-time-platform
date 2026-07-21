# Module 998 Remediation State Machine

```text
diagnostic session
  -> prepare
  -> approve (different actor)
  -> stage
  -> execute native action or approved adapter
  -> verify
  -> close
```

The native `refresh_health_snapshot` action reruns sanitized checks, replaces
the session's current findings, retains the execution result, and then requires
a separate verification run. External actions such as restart, scale, rollback,
replay, configuration refresh, or database repair remain in their approved
state until the named adapter is configured.

Rollback is required for production-changing runbooks and is adapter-gated.
Closure is allowed only after verification or a recorded rollback.
