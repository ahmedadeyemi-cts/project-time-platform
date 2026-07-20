# API contract

The isolated route group is `/api/integration-event-gateway`. Read surfaces: overview, sources, contracts, deliveries, dead-letter-policy, security-policy. Locked operations: events/intake, events/{id}/replay, events/{id}/quarantine, deliveries/{id}/retry, connectors/{code}/test. Locked handlers return HTTP 423 before reading a request body. `Program.cs` remains unchanged.
