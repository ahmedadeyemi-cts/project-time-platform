# AZ-07A1 — Source Repository Detection Corrected

The source checkpoint script now detects a Git worktree using `git rev-parse`, supports `.git` files/worktree pointers, and falls back to the current working directory. No source files or Azure resources were modified.
