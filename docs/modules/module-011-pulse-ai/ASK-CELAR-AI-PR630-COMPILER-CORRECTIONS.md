# PR #630 compiler corrections

The consolidated Ask Celar AI source package uses `PulseAiPrivateEndpointPolicy.ResolutionResult.Approved` for exact-host and IP-pin decisions in both the Oracle runtime probe and the governed real-probe transport.

The evidence-metadata array reader now uses a bounded explicit loop rather than capturing a `ref` counter inside a LINQ lambda. The existing maximum array length, recursive property counter, allowed-key policy, secret-pattern rejection, and fail-closed behavior remain unchanged.

These corrections are source-only. They do not deploy Test or Production, apply Migration 084, enable monitoring or automatic defects, change Oracle infrastructure, read a secret value, or alter PR #629.
