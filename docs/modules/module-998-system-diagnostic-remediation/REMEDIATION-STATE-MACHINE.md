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

The restart claim carries a three-minute execution lease and a unique claim ID.
Final evidence can be written only by that exact claim. If the server exits or
cannot persist the Azure result before the lease expires, the next execution
attempt atomically moves the request to `failed` with an `indeterminate` result
and a manual-reconciliation requirement. It never retries Azure automatically;
an operator must inspect the allowlisted app and prepare a newly approved request
only after the infrastructure state is known.

Verification reads the revisions retained in the execution evidence and queries
Azure through the same managed identity. A restart can become `verified` only
when every accepted revision is present, active, `Healthy`, and `Running`.
Generic ProjectPulse checks remain supporting evidence and cannot substitute for
this Azure state proof.

A partial restart can never become verified: its retained failed revision moves
the request to `failed` with manual reconciliation required. For a fully accepted
restart, an unavailable adapter or a revision that is still activating leaves
the request in `executed` and stores retryable verification evidence. A later
Verify action reuses the original execution evidence rather than nesting or
overwriting it.

Scale, rollback, replay, configuration refresh, and database repair remain in
their approved state until the named adapter is configured.

Rollback is required for production-changing runbooks and is adapter-gated.
Closure is allowed only after verification or a recorded rollback.
