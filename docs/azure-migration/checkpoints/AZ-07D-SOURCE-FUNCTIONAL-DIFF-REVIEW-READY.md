# AZ-07D — Source Functional Diff Review Ready

Date: 2026-07-12

## Purpose

Prepare a final read-only structural review of the six known dirty source paths before any Git write action.

The review reports:

- Changed-file counts and expected-path validation
- Added backend route identifiers
- Added backend type and method identifiers
- Added frontend API paths, functions, and hook references
- Added CSS selectors
- SQL migration object and statement summaries
- Generated `.pyc` tracking and ignore-rule status
- Available backend test projects and frontend package scripts

## Safety

- No source patch content is printed.
- No secret value is printed.
- No source file is modified.
- No Git stage, commit, checkout, fetch, reset, stash, or clean action is performed.
- No application or Azure image build is started.

## Next action

Run `deployment/azure/scripts/az07d-source-functional-diff-summary-readonly.sh` on the Oracle Linux source host. Because GitHub CLI is not installed there, the operational command may be supplied inline while the canonical script remains version-controlled on the Azure migration branch.
