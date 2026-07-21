# Authorization and security

Actual ProjectPulse session authority is required. View-As is read-only. View and management roles are bounded to Administrator, Manager, Data Steward, Security Administrator plus explicit `data-governance.view` or `data-governance.manage` permission. Even authorized management requests remain locked. No secret, credential, token, provider error, external call, or mutation is exposed.
