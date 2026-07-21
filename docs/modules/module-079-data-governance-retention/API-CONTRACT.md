# API contract

The isolated route group is `/api/data-governance-retention`. Read surfaces: overview, domains, classifications, retention-policies, lineage, legal-holds, privacy-policy. Locked operations: records/classify, legal-holds/create, legal-holds/{id}/release, retention/execute, privacy/export, privacy/delete. Locked handlers return HTTP 423 before reading a request body. `Program.cs` remains unchanged.
