# ConnectWise SELL setup — Module 026

This is ConnectWise SELL (now ConnectWise CPQ), using the CPQ API.

1. Open Module 026 → ConnectWise SELL → Configure connection.
2. Save the recommended configuration. Leave Enabled off until the keys arrive.
3. Obtain the tenant access key from the `accesskey` URL parameter in ConnectWise
   SELL. Generate a public/private key pair under Settings → Organization Setup
   → API Keys. Retain the private key when it is first shown.
4. Return to Edit connection, enter all three keys, and Save credential securely.
   They are encrypted together and cannot be retrieved from ProjectPulse.
5. Enable and save the connection, then select Test availability.

| Setting | Value |
| --- | --- |
| Provider key | `connectwise_sell` |
| API origin | `https://sellapi.quosalsell.com` |
| Read-only test | `GET /api/quotes?page=1&pageSize=1` |
| Quote lookup | `GET /api/quotes/{recordId}` (quote ID, not quote number) |
| Authentication | Basic; username = access key + `+` + public key; password = private key |
| Content type | `application/json; version=1.0` |

ProjectPulse constructs the header. Do not supply a bearer token, PSA company ID,
client ID or account password. A different regional/private API origin requires
a reviewed adapter allowlist change. Redirects and private targets are blocked.

**Available** means the read-only quote API returned a valid quote list (empty
is valid). It does not enable customer synchronization, pricing imports or
document publishing. Customer sync now reports that a quote-customer adapter is
required, because the old contacts API belongs to another vendor. Quote lookup
maps name, quote number, account name and quote total; separate quote items need
a reviewed labor-rate adapter before work intake can create records. Document
submission remains blocked by its publisher readiness check. The test creates
no external records and sends no documents or email.

Apply migration `111_connectwise_sell_provider.sql` through the normal reviewed
release process before using the new provider with customer-source or SOW
submission storage. It adds a disabled connection, retires the old one, records
the authority switch and permits new submission destinations. Reapplication
preserves configuration. Existing customer links, credentials, receipts and
manual/other CRM source selections are retained. Old credentials are never copied.
This PR does not apply migrations or deploy anything.

Protocol references checked September 19, 2026:

- [ConnectWise developer portal — CPQ](https://developer.connectwise.com/Products/ConnectWise_CPQ)
  (detailed vendor documentation requires a developer login).
- [CPQ integration implementation](https://github.com/msoukhomlinov/n8n-nodes-connectwise-cpq)
  and its [published Sell API schema](https://github.com/msoukhomlinov/n8n-nodes-connectwise-cpq/blob/main/.docs/references/SellAPI.json).
- [Cognition360 key generation guide](https://help.cognition360.com/hc/en-us/articles/20517034139291-Generating-your-API-keys-for-ConnectWise-Sell).

Local tests use synthetic keys and responses. A live tenant connection test is
still required once the keys are issued; it has not been performed.
