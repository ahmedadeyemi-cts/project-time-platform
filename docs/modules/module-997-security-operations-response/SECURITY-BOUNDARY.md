# Module 997 Security Boundary

## Directly observed

- Valid ProjectPulse actual session.
- Server-side role and permission authorization.
- Authorization-database connectivity through `SELECT 1`.

## Never inferred

- An empty alert or incident list does not mean the environment is healthy.
- A delegated control does not mean the control is currently effective.
- A configured product name does not mean its connector is available.
- An indicator does not authorize blocking or containment.

## Excluded data and behavior

- Raw logs, packet captures, exploit payloads, customer content, credentials,
  tokens, secret values, private topology, and raw provider errors.
- Security-product queries, threat-feed queries, AI calls, identity-provider
  calls, endpoint or firewall actions, external mail, and ticketing actions.
- Durable case mutation, containment, eradication, recovery, export, and closure.

Exceptions are logged only by exception type. Responses contain sanitized
dependency-unavailable messages and no provider or secret detail.
