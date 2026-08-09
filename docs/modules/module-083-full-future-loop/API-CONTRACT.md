# Module 083 API Contract

All endpoints use `/api/full-future-loop` and require an authenticated Pulse session except `/capabilities`.

## Create a sandbox loop

`POST /loops`

```json
{
  "title": "Full Future Loop Sandbox Test",
  "description": "Validate the complete lifecycle.",
  "changeType": "major",
  "selectiveGovernance": true,
  "sourceRepository": "ahmedadeyemi-cts/project-time-platform",
  "sourceBranch": "sandbox/full-future-loop",
  "sourceCommit": ""
}
```

## Apply one governed action

`POST /loops/{loopId}/actions`

```json
{
  "action": "approve_governance",
  "notes": "Approved for sandbox validation.",
  "expectedRevision": 1
}
```

The server rejects actions that do not match the current lifecycle stage and returns the valid next actions.

## Run the complete deterministic sandbox

`POST /loops/{loopId}/run-full-sandbox`

The loop must be at `governance_pending` or `private_development`. The endpoint records every governed stage through `verified_closed` in one transaction.

## Agent Keep

`POST /loops/{loopId}/agent-keep`

```json
{
  "question": "What is the current status and next governed action?",
  "openSupportIssue": false
}
```

Agent Keep uses only the selected loop state and approved sandbox evidence. It has no private-source, deployment, cloud, secret, or production mutation capability.

## Reset

`POST /loops/{loopId}/reset`

Reset requires Module 083 management authority. It increments the test iteration and returns the work item to its initial governed stage without deleting prior events or artifacts.
