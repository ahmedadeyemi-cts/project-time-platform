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
a separate verification run. The Azure Container Apps `restart_service` action
requires the staged state and an exact allowlisted target. Promotion atomically
claims the request before Azure is called, so concurrent requests and retries
cannot restart the same active revisions more than once. Every accepted revision
is retained; a later revision failure records an executed partial outcome and
requires verification instead of presenting the operation as a total failure.
After the claim commits, a bounded server-owned execution and evidence-finalizing
transaction continue independently of the initiating HTTP connection.

Scale, rollback, replay, configuration refresh, and database repair remain in
their approved state until the named adapter is configured.

Rollback is required for production-changing runbooks and is adapter-gated.
Closure is allowed only after verification or a recorded rollback.
