# Module 078 — Ask Celar AI Availability Monitoring and Automatic Defects

## Scope

Module 078 owns the meaning of service health, thresholds, suppressions, and recovery. Ask Celar AI presents the controls and troubleshooting evidence. Module 076 stores the resulting defects. Module 083 owns future bounded external automation.

The initial implementation is protected-Test only. Every policy is seeded with automatic creation disabled.

## Two-key activation

A monitor creates a defect only when both controls are true:

```text
PROJECTPULSE_CELAR_AI_AUTOMATIC_DEFECTS_ENABLED=true
module076_monitor_policies.machine_creation_enabled=true
```

Production always evaluates the deployment-level flag as disabled. A per-policy database value cannot override that source boundary.

## Seeded Test policies

| Policy | Failure threshold | Window | Recovery | Initial priority |
|---|---:|---:|---:|---|
| Pulse web | 3 | 3 minutes | 3 successes + 15 stable minutes | Critical |
| Pulse API | 3 | 3 minutes | 3 successes + 15 stable minutes | Critical |
| Pulse database | 3 | 3 minutes | 3 successes + 15 stable minutes | Critical |
| Pulse SSO | 3 | 3 minutes | 3 successes + 15 stable minutes | Critical |
| All Celar AI targets | 3 | 5 minutes | 3 successes + 15 stable minutes | Critical |
| Private inference | 3 | 5 minutes | 3 successes + 15 stable minutes | High |
| Private embeddings | 3 | 5 minutes | 3 successes + 15 stable minutes | High |
| Private OCR | 3 | 5 minutes | 3 successes + 15 stable minutes | High |
| Private malware scan | 3 | 5 minutes | 3 successes + 15 stable minutes | High |
| Module 064 routing | 3 | 5 minutes | 3 successes + 15 stable minutes | High |
| GitHub API/repository | 3 | 10 minutes | 3 successes + 15 stable minutes | High |
| GitHub Actions during release | 2 | 5 minutes | 3 successes + 15 stable minutes | Critical |
| Module 067 delivery | 5 | 15 minutes | 3 successes + 15 stable minutes | High |
| Celar AI TLS certificate | 2 daily checks | 24 hours | 3 successful checks | Critical |
| ClamAV signature freshness | 1 daily check | 24 hours | 3 successful checks | High |

These are versioned Test defaults. They are not represented as approved Production SLOs.

## Probe results

Every probe result is append-only and contains only bounded operational metadata:

```text
policy code
component code
probe code
healthy / degraded / failed / suppressed / unknown
sanitized failure code
sanitized detail
latency
HTTP status
correlation ID
release SHA
observed timestamp
```

No prompt, private document, token, cookie, credential, response body, or embedding vector is stored.

## Threshold evaluation

The evaluator reads the current window in reverse chronological order and counts consecutive states. Intermittent success breaks a failure sequence. Intermittent failure breaks a recovery sequence.

A threshold crossing creates or updates a machine defect only when:

- the deployment is protected Test;
- automatic monitoring is enabled;
- the individual policy is enabled;
- the individual policy permits machine creation;
- no active suppression matches the environment and component;
- the rate limit has not been exceeded;
- the approved default assignee resolves to an active Module 062 identity.

## Fingerprint and deduplication

The stable incident fingerprint includes:

```text
environment
component
policy
release SHA
```

Failure code, latency, and HTTP status remain occurrence evidence. This allows the recovery probe to resolve the same incident while preserving every specific failure observation.

A partial unique index permits only one active machine-created defect for a fingerprint and environment.

## Rate limiting

The initial limit is:

```text
maximum 10 new automatic defects per hour
```

Failures beyond the limit remain in append-only probe evidence but do not create an unbounded number of defect records. The limit protects Module 076 and the notification pipeline from an outage storm.

## Suppressions

A suppression must include:

- environment;
- component;
- reason;
- owner identity;
- start time;
- expiration time.

Suppressions expire automatically. A suppression prevents automatic defect creation but does not erase probe evidence.

Approved use cases include:

- scheduled maintenance;
- Container App revision warm-up;
- planned model restart;
- explicitly authorized canary tests;
- database maintenance;
- controlled Test fault injection.

## Recovery

A machine-created defect becomes eligible for automatic resolution only after:

```text
3 consecutive healthy probes
AND
15 minutes since the last failed or degraded probe
```

Recovery appends:

- a recovered occurrence;
- a recovery comment;
- an append-only lifecycle event;
- a Module 067 resolution-notification outbox item.

A user-created defect is never automatically resolved.

## Flapping

A recurrence during the policy’s flapping window reopens the same machine-created defect. The system increments the flapping count, appends evidence, and queues a reopen notification.

When the configured reopen threshold is reached, priority is escalated by one level up to Critical and an escalation notification is queued.

## Read-only adapters

Initial probes are intentionally bounded:

- database: connection and `SELECT 1`;
- private runtime: authenticated, exact-host, IP-pinned HTTPS readiness;
- Module 064: governed capability route state;
- GitHub: exact allowlisted repository metadata endpoint;
- Module 067: defect-notification outbox age and count;
- Pulse health: deployment-managed HTTPS endpoints on the configured Pulse origin.

The monitor does not execute model prompts or use an AI model to decide whether a threshold was crossed.

## Fault injection

The synthetic harness is Test-only and separately gated:

```text
PROJECTPULSE_CELAR_AI_SYNTHETIC_FAILURES_ENABLED=true
```

It records allowlisted synthetic probe evidence. It does not make a real service unavailable.

The suite covers private inference, embeddings, OCR, malware scanning, provider routing, GitHub status codes and timeouts, GitHub Actions, database timeouts, notification delivery, latency, stale evidence, invalid citations, duplicate delivery, and flapping.

## Activation sequence

1. Apply migration 084 in protected Test.
2. Deploy source with automatic monitoring and synthetic failures disabled.
3. Verify readiness and observe-only probes.
4. Enable the synthetic harness.
5. Run every allowlisted scenario while machine creation remains disabled.
6. Validate failure counters and suppressions.
7. Enable one monitor policy through Ask Celar AI.
8. Cross the threshold and verify exactly one Ahmed-assigned Module 076 defect.
9. Repeat failures and verify occurrence append without duplication.
10. Send recovery evidence and verify the complete stability rule.
11. Test recurrence and priority escalation.
12. Disable the policy and synthetic harness after UAT.

## Current source boundary

This package contains policy and execution source. It does not activate Production monitoring, define Production SLOs, alter Oracle services, install an external watchdog, or create GitHub issues.
