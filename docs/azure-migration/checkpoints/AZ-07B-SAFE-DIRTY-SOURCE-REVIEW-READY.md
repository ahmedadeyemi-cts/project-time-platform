# AZ-07B — Safe Dirty Source Review Ready

Date: 2026-07-12

## Purpose

Review the six uncommitted source paths without printing patch content or secret values and without modifying the source repository.

## Checks

- Confirm source HEAD and branch.
- Inventory tracked and untracked changed paths.
- Record file types, sizes, and SHA-256 hashes.
- Produce Git diff statistics and numeric statistics without patch content.
- Run `git diff --check` for whitespace errors.
- Classify the tracked Python bytecode file as a generated artifact.
- Scan UTF-8 changed files for likely credential, token, private-key, SAS, and embedded-credential patterns.
- Report only finding type and line number; never print matched values.
- Confirm Dockerfile count.

## Safety

- Read-only source review.
- No `git add`, commit, checkout, fetch, reset, stash, or clean.
- No application build.
- No Azure image build or resource creation.
- No patch content or secret values written to GitHub.

## Decision gate

Source commit and image build remain blocked until the review output is evaluated. The `.pyc` generated binary must not be included in the application source commit.
