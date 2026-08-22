#!/usr/bin/env bash
set -Eeuo pipefail

migration='database/migrations/096_project_planning_document_authority.sql'
rollback='database/rollback/096_project_planning_document_authority_rollback.sql'

test -s "$migration"
test -s "$rollback"

grep -Fq 'project_planning_document_authority' "$migration"
grep -Fq 'projectpulse_reconcile_project_planning_document_authority' "$migration"
grep -Fq 'statement_of_work' "$migration"
grep -Fq "'gsd'" "$migration"
grep -Fq 'source_sha256' "$migration"
grep -Fq 'document_version_id' "$migration"
grep -Fq 'is_current' "$migration"
grep -Fq 'current_project_planning_document_authority' "$migration"
grep -Fq '096_project_planning_document_authority' "$migration"
grep -Fq 'DROP TABLE IF EXISTS project_planning_document_authority' "$rollback"
grep -Fq '096_project_planning_document_authority' "$rollback"

echo 'PROJECT_PLANNING_DOCUMENT_AUTHORITY_MIGRATION_096=PASSED'
