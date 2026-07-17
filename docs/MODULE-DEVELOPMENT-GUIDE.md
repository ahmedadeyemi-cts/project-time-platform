# ProjectPulse Module Development Guide

## Purpose

This guide defines the required process for developing, combining, validating,
committing, pushing, and deploying ProjectPulse modules without removing or
overwriting completed work.

## Current Forward-Moving Baseline

- Branch: `source/module-059-restored-on-current-live-20260717`
- Commit: `c651dc71228cda89d42cf0fa4224371082e07a38`
- Azure revision: `ca-phd-test-web-westus3--m059current-0717170903`
- The baseline includes the current application and the restored Module 059
  Session Intelligence drawer.

A new module must not begin from an older commit.

## Core Rules

1. Every module uses a separate clone or Git worktree.
2. Every module uses a separate branch.
3. A module never reuses another module's working directory.
4. The base branch and full base commit are recorded before editing begins.
5. Expected files and application areas are recorded before implementation.
6. Existing modules are built and validated before deployment.
7. Source is committed and pushed before Azure deployment.
8. The deployed image identifies the exact Git commit used to build it.
9. A successfully deployed commit becomes the next forward-moving baseline.
10. An older module branch is never deployed directly after the baseline advances.
11. `git add -A` is not used until every modified and untracked file is reviewed.
12. Azure, API, database, and Entra changes require explicit scope declaration.

## Naming Standards

Branches:

- `feature/module-NNN-description-YYYYMMDD`
- `source/module-NNN-description-YYYYMMDD`
- `repair/module-NNN-description-YYYYMMDD`
- `docs/module-description-YYYYMMDD`

Workspaces:

- `$HOME/project-time-platform-module-NNN-description`
- `$HOME/project-time-platform-module-governance`

## Required Module Record

Before implementation, record:

- module number and description;
- owner and status;
- workspace and branch;
- base branch and base commit;
- expected files or components;
- dependencies;
- overlapping files;
- GitHub status;
- Azure status;
- final commit and deployment revision.

Allowed status values:

- Planned
- Ready
- Active
- Blocked
- Ready for Validation
- Pushed
- Deployed
- Superseded
- Abandoned

## Before Editing

Run:

```bash
pwd
git branch --show-current
git rev-parse HEAD
git status --short --branch
```

The directory, branch, and commit must match the module record.

## Before Staging

Run:

```bash
git status --short
git diff --stat
git diff --check
git diff
```

Stage only reviewed paths:

```bash
git add path/to/real-file1 path/to/real-file2
```

Do not copy placeholder paths literally.

## Before Commit

Run:

```bash
git diff --cached --check
git diff --cached --stat
git diff --cached
```

The staged changes must contain only intended module work.

## Before Push

Run:

```bash
git fetch origin
git status --short --branch
git log --oneline --decorate -5
```

Push only the current branch:

```bash
git push --set-upstream origin "$(git branch --show-current)"
```

## Integration and Conflict Review

A module created from an older baseline must be rebased, merged, or recreated on
the newest approved baseline before deployment.

Use real refs:

```bash
git diff --name-status \
  source/module-059-restored-on-current-live-20260717...feature/module-061-new-feature-20260717
```

The words `NEWEST_BASELINE` and `MODULE_BRANCH` are documentation placeholders,
not literal Git references.

Two modules have a potential conflict when they modify the same file. Additional
review is always required for:

- `App.jsx`;
- routing;
- authentication and authorization;
- shared layout and global CSS;
- API startup and dependency injection;
- database migrations;
- shared domain models;
- deployment files;
- environment configuration.

A clean Git merge does not prove that two modules are functionally compatible.

## Before Deployment

Record:

- source branch;
- full source commit;
- successful build;
- intended container tag;
- target application;
- API-change status;
- database-change status;
- Entra-change status;
- rollback image or revision.

Deploy only the exact commit already pushed to GitHub.

## After Deployment

Validate:

- authentication;
- navigation;
- shared layout;
- previously deployed modules;
- new module functionality;
- API health;
- browser console;
- relevant logs.

Record the successful commit as the new forward-moving baseline.

## Rollback

Rollback Azure to the previous verified revision or image.

Do not use force-push or Git history rewriting as an application rollback.
