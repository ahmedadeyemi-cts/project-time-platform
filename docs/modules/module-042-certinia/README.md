# Module 042 — Certinia Invoice Delivery Foundation

Module 042 extends the existing immutable ProjectPulse invoice ledger with a safe Certinia delivery foundation. It is built forward from the Module 058 CI/CD branch and does not replace or remove Modules 057 or 058.

## Delivered behavior

- Generates a server-side PDF invoice artifact.
- Generates an Excel-compatible `.xls` invoice artifact.
- Hides engineer and PM/PC names by default.
- Includes resource names only after an explicit user choice.
- Downloads the selected artifact directly from ProjectPulse.
- Queues an immutable invoice artifact before Certinia API access exists.
- Sends the queued artifact manually after the connector is configured.
- Retries failed transmissions through `external_integration_outbox`.
- Synchronizes Certinia status through the nightly endpoint and GitHub Actions workflow.
- Appends queue, send, failure, and status events to `billing_invoice_events`.
- Leaves source time and immutable invoice snapshot fields unchanged.

## Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| GET | `/api/billing/certinia/configuration` | Returns safe connector readiness without secrets. |
| GET | `/api/billing/invoices/{invoiceId}/certinia-status` | Returns outbox and immutable event history. |
| GET | `/api/billing/invoices/{invoiceId}/document` | Generates PDF or Excel-compatible invoice output. |
| POST | `/api/billing/invoices/{invoiceId}/certinia/send` | Queues an artifact or queues and sends it immediately. |
| POST | `/api/billing/certinia/process-outbox` | Processes pending and retryable deliveries. |
| POST | `/api/billing/certinia/sync-status` | Synchronizes remote invoice status. |
| POST | `/api/billing/certinia/nightly` | Runs outbox processing and status synchronization. |

## Existing database foundation

This module uses existing objects only:

- `external_integration_connections`
- `external_integration_outbox`
- `external_integration_sync_runs`
- `billing_invoice_events`
- `billing_invoices`
- `billing_invoice_lines`

No migration is included. Queue idempotency is calculated from the immutable invoice snapshot SHA256, document format, and resource-name choice.

The Certinia external identifier and remote status are stored in the existing outbox JSON payload so the immutable invoice snapshot does not need to be rewritten.

## Runtime configuration

Non-secret values:

- `PROJECTPULSE_CERTINIA_ENABLED`
- `PROJECTPULSE_CERTINIA_BASE_URL`
- `PROJECTPULSE_CERTINIA_TOKEN_URL`
- `PROJECTPULSE_CERTINIA_UPLOAD_PATH`
- `PROJECTPULSE_CERTINIA_STATUS_PATH_TEMPLATE`
- `PROJECTPULSE_CERTINIA_SCOPE`
- `PROJECTPULSE_CERTINIA_TRANSPORT`
- `PROJECTPULSE_CERTINIA_DEFAULT_DOCUMENT_FORMAT`
- `PROJECTPULSE_CERTINIA_TIMEOUT_SECONDS`

Secret-backed values:

- `PROJECTPULSE_CERTINIA_CLIENT_ID`
- `PROJECTPULSE_CERTINIA_CLIENT_SECRET`
- `PROJECTPULSE_CERTINIA_SYNC_TOKEN`

The deployment intentionally sets `PROJECTPULSE_CERTINIA_ENABLED=false`. Configure secret references and connector values separately, validate them, then explicitly enable transmission.

## Payload compatibility

Successful upload responses accept these external identifier fields:

- `externalId`
- `id`
- `invoiceId`
- `recordId`
- `certiniaInvoiceId`

Status responses accept:

- `status`
- `invoiceStatus`
- `state`

Certinia states such as `sent`, `posted`, `delivered`, `open`, `settled`, and `paid` are mapped to the existing ProjectPulse invoice status vocabulary.

## Nightly workflow

`.github/workflows/projectpulse-certinia-nightly.yml` calls:

`POST /api/billing/certinia/nightly`

with the `X-ProjectPulse-Integration-Token` header. The token must be stored as the `PROJECTPULSE_CERTINIA_SYNC_TOKEN` GitHub Actions secret. The API base URL may be set with the `PROJECTPULSE_API_BASE_URL` repository variable.

## Safety invariants

Deployment does not:

- create an invoice;
- consume an invoice number;
- modify database schema;
- transmit an invoice to Certinia;
- modify Microsoft Entra ID;
- expose connector secrets;
- discard Modules 057 or 058.
