# API contract

The isolated route group is `/api/customer-delivery-acceptance`. Read surfaces: overview, engagements, milestones, artifacts, reviews, acceptance-policy, sharing-policy. Locked operations: engagements/{id}/invite, artifacts/{id}/share, reviews/{id}/comment, reviews/{id}/accept, reviews/{id}/reject, shares/{id}/revoke. Locked handlers return HTTP 423 before reading a request body. `Program.cs` remains unchanged.
