# AZ-07A — Source Code Checkpoint Ready

Date: 2026-07-12

## Purpose

Inspect the Oracle Linux source repository before any Azure image build while preserving all current tracked and untracked files.

## Safety controls

- Read-only source inspection.
- No `git add`, commit, checkout, reset, stash, clean, pull, or fetch.
- No application build.
- No Azure image build.
- No patch or file-content collection.
- Changed file names and SHA-256 hashes are recorded locally for traceability.
- Generated state and logs remain outside the repository.

## Decision rule

- A clean worktree permits preparation of a reproducible image build.
- Any tracked, staged, or untracked source change blocks the image build until it is reviewed, sanitized, committed, and pushed.

## Next action

Run `deployment/azure/scripts/az07a-source-code-checkpoint-readonly.sh` on the Oracle Linux source host with the application repository at `/opt/project-time-platform/app/project-time-platform-022`.