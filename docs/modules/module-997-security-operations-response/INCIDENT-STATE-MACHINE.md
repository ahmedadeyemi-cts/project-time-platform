# Module 997 Incident State Machine

| Step | State | Operational behavior |
|---|---|---|
| 1 | Detect | Review native authentication/audit signals and stored alerts |
| 2 | Triage | Validate severity, confidence, user, IP, session, and affected resource |
| 3 | Declare | Create a durable incident, owner, and first timeline event |
| 4 | Contain | Prepare, separately approve, and execute an available containment action |
| 5 | Eradicate | Record eradication evidence performed through the owning system |
| 6 | Recover | Record restoration and business verification |
| 7 | Review | Retain lessons learned and control improvements |
| 8 | Close | Require recovery/review state and a closure summary |

At any active incident state, an authorized analyst can start a Module 998
diagnostic session. The diagnostic session ID is linked back to the incident and
the handoff is written to the incident timeline.
