# Module 026 authorization matrix

| Capability | Sales / AE / Inside Sales / SA | Project Team Coordinator | Integration Administrator | Administrator |
|---|---:|---:|---:|---:|
| View provider status | Yes | Yes | Yes | Yes |
| View credential values | Never | Never | Never | Never |
| Add a manual platform | No | No | Yes | Yes |
| Change endpoint/auth metadata | No | No | Yes | Yes |
| Replace a write-only credential | No | No | Yes | Yes |
| Start OAuth consent | No | No | Yes | Yes |
| Run an availability test | No | No | Yes | Yes |
| Mutate while using View-As | No | No | No | No |

Permission codes:

- `VIEW_INTEGRATIONS_026`
- `MANAGE_INTEGRATIONS_026`

Provider configuration, credential rotation, OAuth completion, and connection
tests create Module 026 audit events using the actual ProjectPulse user ID.
