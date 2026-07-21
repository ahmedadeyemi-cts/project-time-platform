# Authorization and security

Actual ProjectPulse session authority is required. View-As is read-only. View and management roles are bounded to Administrator, Manager, Project Manager, Project Team Coordinator, Solution Architect plus explicit `customer-acceptance.view` or `customer-acceptance.manage` permission. Even authorized management requests remain locked. No secret, credential, token, provider error, external call, or mutation is exposed.
