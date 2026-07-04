# 026 CRM Integration Framework

## Status
Applied as complete Module 026 framework pending validation and commit.

## 026A CRM Integration Framework Foundation
Created the dashboard module and standalone `#crm-integration` route.

## 026B CRM Provider Registry + Audit Foundation
Added database migration and framework tables:

- `crm_integration_providers`
- `crm_integration_field_mappings`
- `crm_integration_sync_preview_events`
- `crm_integration_promotion_events`
- `crm_integration_readiness_reviews`

Seeded provider placeholders:

- Salesforce
- Zendesk Sell

Seeded shared field mappings from CRM records into ProjectPulse workflows.

## 026C CRM-to-SOW / CRM-to-Intake Promotion Preview
Added preview workflow for:

- CRM sync preview
- Promote to SOW Generator preview
- Promote to Sales-to-Delivery Intake preview
- Promotion audit event design

## 026D Module 026 Closeout
Added module readiness checklist and closeout documentation.

## Security Position
Module 026 does not store CRM credentials, API keys, or tokens in the repository. Future real connector credentials must be configured server-side using root-only environment files or a secret manager.

## Workflow Placement
- CRM records feed Module 025 SOW Generator.
- CRM records feed Module 024 Sales-to-Delivery Intake Foundation.
- Signed CRM/SOW state feeds Module 027 Signed SOW Handoff + Assignment Trigger.
