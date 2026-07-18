# MODULE 042 Recovery Artifact Manifest

This manifest records the exact recovery artifacts re-supplied on 2026-07-15. It exists so future recovery work can verify that the correct files are being used before rebuilding or deploying.

| Artifact | Size (bytes) | SHA-256 |
|---|---:|---|
| `run-module-042-live-db-preflight-standalone(1).sh` | 21418 | `639490c8ffed4edbf4fee586a60bb9567e83b3776a3a3a5dc143803b0a33cbbe` |
| `module-042-live-schema-review-20260714T231604Z(1).txt` | 26169 | `d4dfc730e9e21e722eaf3e8090f201c0119d9611e07b586b30904036e87940fd` |
| `validate-module-042-migration-056-live-rollback(1).sh` | 30201 | `5682ac67cc958896076c112b01e3b6d6e79a921c218830ec55c4f46b87e37647` |
| `apply-and-verify-module-042-migration-056(1).sh` | 35146 | `221ca26dbe12978052ee38032b3ddfc82d6d9b40e491ce0ef16bc93aeea07f36` |
| `module-042-implementation-source-20260715T001528Z(1).txt` | 873888 | `a62e8feb24e6486c2ee06fb8c220b51ae8867f8d4dd65b6b16ec3f21142bc1f3` |
| `implement-module-042-live-billing-slice(1).sh` | 134186 | `5c18301593cf6888aabab75820b4909adb35981e3c12297b010c4c67bae2d296` |
| `implement-module-042-live-billing-slice-e497-eof-fixed(1).sh` | 134185 | `2fd2d8d26a0b2f80f0c1b73da7a61189178a40248425d4b5234eff5d82b02cd7` |
| `implement-module-042-live-billing-slice-e497-dotnet10-fixed(1).sh` | 135793 | `54f10be2d966a925b8a2223b554eb528b7c518542ad00c57a3abba18beb2ee63` |
| `implement-module-042-live-billing-slice-e497-dotnet10-eof-fixed(1).sh` | 135792 | `1dd2b207ec30009e8db295d2aa029cd837e8549242bc880223a32f0ad51b61f8` |
| `push-build-deploy-module-042-live-billing-no-gh(1).sh` | 18097 | `a01eb23690eb2d99d74b7ee067611f4f831a7720cf32da3242de8b41bdeb1978` |
| `push-build-deploy-module-042-live-billing(1).sh` | 18004 | `7c5d312ecac93da47df318c456545b12b40d76c0e2acdb614d641e20156ff96d` |

## Preferred implementation generator

Use:

`implement-module-042-live-billing-slice-e497-dotnet10-eof-fixed(1).sh`

Expected SHA-256:

`1dd2b207ec30009e8db295d2aa029cd837e8549242bc880223a32f0ad51b61f8`

This is the version that passed:

- source guards
- static implementation validation
- .NET 10 backend build
- frontend production build
- frontend billing-route bundle validation
- staged whitespace validation
- local commit creation

## Source-of-truth rule

Before any future long-running build, deployment, database inspection, or recovery operation, the exact generator, deployment script, reports, and resulting source commit must be committed and pushed to Git.
