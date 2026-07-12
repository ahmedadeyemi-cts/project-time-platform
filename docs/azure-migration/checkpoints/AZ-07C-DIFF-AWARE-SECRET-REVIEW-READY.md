# AZ-07C — Diff-Aware Secret Review Ready

Date: 2026-07-12

## Current source checkpoint

The read-only AZ-07B review confirmed:

- Repository HEAD matches `5a221da29cdfc1134e5d603175b311ff97658b67` and `origin/main`.
- Four tracked files are modified.
- Two SQL files are untracked.
- No files are staged.
- One tracked file is generated Python bytecode and should be excluded from the eventual source commit.
- No Dockerfiles currently exist.
- `git diff --check` passed.
- The full-file heuristic scan reported 19 credential-like assignment matches in `Program.cs`, but this scan included unchanged baseline code and did not establish that the modified lines contain secrets.

## Next review

Run `deployment/azure/scripts/az07c-diff-aware-secret-review-readonly.sh` on the Oracle Linux source host, or use its equivalent inline form when GitHub CLI is unavailable.

The review:

- scans only added lines in tracked text files;
- scans all lines in untracked text files;
- compares baseline and worktree finding counts;
- prints finding types and line numbers only;
- never prints matched secret values or source patches;
- changes no source files and performs no Git write operation.

## Safety decision

Source staging, commit, image creation, and application deployment remain blocked until AZ-07C is reviewed.
