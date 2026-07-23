# AZ-05C2B1H — Clean Restore Retry Submitted and Pending

**Date:** 2026-07-12

## Submission

The isolated PostgreSQL restore retry was submitted successfully.

- Run Command: `phdrestoreseedretry20260712t184644z`
- Result prefix: `restore-results/retry-20260712T184644Z`
- Guest state directory: `/var/lib/project-health-dashboard/az05c2b1h-20260712t184644z`
- Guest log: `/var/log/phd-az05c2b1h-20260712t184644z-restore-validation.log`
- Submission mode: asynchronous managed Run Command
- First restore attempt: terminal `Failed`, exit code `6`
- Temporary result-upload role: existing

## Immediate status

The first status query returned:

- execution state: `Pending`
- exit code: `0`
- error: none
- output: empty

`Pending` is an active pre-execution state. It does not indicate a restore failure and does not authorize resubmission.

## Status-script correction

The compact retry status script previously classified only `Running` as active. It now classifies:

- `Pending` as `WAITING_TO_START`
- `Running` as `STILL_RUNNING`
- terminal failure states as `FAILED`
- successful terminal execution with the expected marker as `PASSED`

## Safety

Do not rerun the retry submitter while this Run Command is `Pending` or `Running`. Continue with read-only status checks only.
