# 026 CRM Integration Framework

## 026A CRM Integration Framework Foundation

Status: Applied pending validation and commit.

## Purpose
Create the CRM integration framework for Salesforce and Zendesk Sell so CRM-originated opportunities, quotes, owners, Solution Architect assignments, service lines, and handoff metadata can later feed the SOW Generator and Sales-to-Delivery Intake workflows.

## Scope
- Dashboard module card.
- Standalone `#crm-integration` route.
- Salesforce connector placeholder.
- Zendesk Sell connector placeholder.
- CRM-to-SOW field mapping.
- CRM-to-intake field mapping.
- Sync-readiness checklist.
- Manual promotion model.
- No CRM credentials, tokens, or API keys stored in the repository.

## Future Backend Endpoints
- `GET /api/crm/integrations/summary`
- `GET /api/crm/integrations/field-mapping`
- `POST /api/crm/integrations/test-connection`
- `POST /api/crm/integrations/sync-preview`
- `POST /api/crm/integrations/promote-to-sow`
- `POST /api/crm/integrations/promote-to-intake`

## Security Position
All real Salesforce and Zendesk Sell credentials should be stored outside the repository in root-only environment files or a secrets manager. Frontend code must never contain CRM credentials, API keys, or tokens.
