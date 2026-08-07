# Module 998 Security and Operations Boundary

Module 998 reads sanitized ProjectPulse runtime and database metadata. It does
not retrieve secret values, tokens, connection strings, raw log bodies, packet
data, or raw provider errors. Request bodies are bounded and processed only
after actual-session authorization, View-As protection, management permission,
and schema readiness.

Native diagnostics and evidence persistence are operational. The native health
refresh runbook changes diagnostic evidence only. The Azure Container Apps
restart adapter is available only when managed identity, the narrow revision
read/restart permissions, and an exact target allowlist are all present. It
requires a staged request, atomically claims execution, and records complete,
partial, or failed Azure acceptance before returning. Other production-changing
actions remain adapter-gated and perform no command or network call.

All session and remediation changes write Module 998 audit evidence. Approval
by the requester is rejected by both the API transition and database constraint.
