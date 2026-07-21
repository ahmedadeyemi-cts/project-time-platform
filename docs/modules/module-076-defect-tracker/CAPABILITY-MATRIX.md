# Module 076 Capability Matrix

This module implements the user-approved defect-log and defect-triage scope from the ProjectPulse UAT/go-live backlog. No new v1.8 requirement identifier is invented where the repository tracker has not assigned one.

| Capability | Source state | Runtime state |
|---|---|---|
| US Signal-branded defect register and required headers | Implemented | Read center registered |
| Help assistant intake path | Implemented | Opens Module 076 with source attribution |
| GitHub issue form | Implemented | Available after source merge; automatic reconciliation locked |
| Claude through GitHub | Contract implemented | Signed webhook locked |
| ChatGPT through GitHub | Contract implemented | Signed webhook locked |
| Automatic `DEF-{YYYY}-{SEQUENCE}` ID | Contract implemented | Durable sequence locked |
| Ahmed default identity assignment | Module 062 lookup implemented | Read resolution active; durable assignment locked |
| Identity-backed reassignment dropdown | Implemented | Read options available to authorized roles; save locked |
| Date added / date resolved / resolution time | Server policy implemented | Durable transitions locked |
| Manager notification on open | Module 067/outbox contract implemented | Outbox and delivery locked |
| Reporter notification on resolution | Module 067/outbox contract implemented | Outbox and delivery locked |
| Comments and history | API/data contract implemented | Durable append-only store locked |
| Role-safe all/own scope | Authorization contract implemented | No live inventory store connected |
| Database migration or write | Excluded | Not authorized |
| GitHub App/webhook secret and activation | Excluded | Not authorized |
| Direct AI execution | Excluded | Not authorized; future triage must use Module 064 |
| External email delivery | Excluded | Not authorized; Module 067 owner |

Source phase: `076_COMPLETE_SOURCE_FAIL_CLOSED`.
