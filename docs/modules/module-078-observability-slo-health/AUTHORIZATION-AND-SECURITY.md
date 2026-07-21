# Authorization and security

Actual ProjectPulse session authority is required. View-As is read-only. View and management roles are bounded to Administrator, Manager, Engineering Team Lead, Security Administrator plus explicit `observability.view` or `observability.manage` permission. Even authorized management requests remain locked. No secret, credential, token, provider error, external call, or mutation is exposed.
