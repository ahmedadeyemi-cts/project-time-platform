# Authorization and security

Actual ProjectPulse session authority is required. View-As is read-only. View and management roles are bounded to Administrator, Manager, Integration Administrator, Security Administrator plus explicit `integration-events.view` or `integration-events.manage` permission. Even authorized management requests remain locked. No secret, credential, token, provider error, external call, or mutation is exposed.
