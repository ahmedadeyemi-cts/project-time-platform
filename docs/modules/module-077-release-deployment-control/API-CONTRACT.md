# API contract

The isolated route group is `/api/release-deployment-control`. Read surfaces: overview, releases, environments, gates, evidence, rollback-policy. Locked operations: releases/prepare, releases/{id}/approve, releases/{id}/promote, releases/{id}/verify, releases/{id}/rollback. Locked handlers return HTTP 423 before reading a request body. `Program.cs` remains unchanged.
