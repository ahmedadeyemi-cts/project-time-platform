# API contract

The isolated route group is `/api/observability-slo-health`. Read surfaces: overview, services, signals, slos, alerts, integrations, retention-policy. Locked operations: signals/intake, slos/{id}/evaluate, alerts/{id}/acknowledge, alerts/{id}/resolve, connectors/{code}/test. Locked handlers return HTTP 423 before reading a request body. `Program.cs` remains unchanged.
