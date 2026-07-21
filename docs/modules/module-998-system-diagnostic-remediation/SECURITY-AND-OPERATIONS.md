# Module 998 Security and Operations Boundary

Module 998 reads sanitized ProjectPulse runtime and database metadata. It does
not retrieve secret values, tokens, connection strings, raw log bodies, packet
data, or raw provider errors. Request bodies are bounded and processed only
after actual-session authorization, View-As protection, management permission,
and schema readiness.

Native diagnostics and evidence persistence are operational. The native health
refresh runbook changes diagnostic evidence only. Every production-changing
action is adapter-gated, preserves its approved plan, returns the exact missing
adapter, and performs no command or network call.

All session and remediation changes write Module 998 audit evidence. Approval
by the requester is rejected by both the API transition and database constraint.
