# Module 073 Data Design Gate

Current `projects` and `project_intake_requests` supply AE/SA source signals but cannot represent primary/backup Resale Operations or effective-dated coverage history. They are read only and must not be repurposed.

A future design must define an immutable alignment ID, four stable user IDs, territory/team, start/end, active state, version, actor, reason, created/updated timestamps, overlap rules, soft retirement, and audit events. It must define whether multiple territories per AE and overlapping handoffs are allowed before any schema or persistence source is written.
