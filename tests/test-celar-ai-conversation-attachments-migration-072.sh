#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONTAINER="projectpulse-celar-ai-attachments-072-${GITHUB_RUN_ID:-local}-$$"
DB_USER="projectpulse"
DB_NAME="projectpulse"
DB_PASSWORD="projectpulse-test-only"
IMAGE="postgres:16-alpine@sha256:57c72fd2a128e416c7fcc499958864df5301e940bca0a56f58fddf30ffc07777"
MIGRATION="/workspace/database/migrations/072_celar_ai_conversation_attachments.sql"
ROLLBACK="/workspace/database/rollback/072_celar_ai_conversation_attachments_rollback.sql"

cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

psql_exec() {
  docker exec -i -e PGPASSWORD="$DB_PASSWORD" "$CONTAINER" \
    psql -v ON_ERROR_STOP=1 -U "$DB_USER" -d "$DB_NAME" "$@"
}
value() { psql_exec -Atqc "$1" | tr -d '\r'; }
assert_eq() {
  local expected="$1" actual="$2" label="$3"
  [[ "$actual" == "$expected" ]] || {
    echo "ASSERTION_FAILED $label expected=$expected actual=$actual" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label=$actual"
}
expect_failure() {
  local sql="$1" label="$2"
  if psql_exec -c "$sql" >/tmp/celar-ai-072-failure.log 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  echo "ASSERTION_PASSED $label"
}
expect_file_failure() {
  local file="$1" expected="$2" label="$3"
  if psql_exec -f "$file" >/tmp/celar-ai-072-file-failure.log 2>&1; then
    echo "ASSERTION_FAILED $label unexpectedly_succeeded" >&2
    exit 1
  fi
  grep -Fq "$expected" /tmp/celar-ai-072-file-failure.log || {
    echo "ASSERTION_FAILED $label missing_expected_error=$expected" >&2
    exit 1
  }
  echo "ASSERTION_PASSED $label"
}

docker run --detach --rm \
  --name "$CONTAINER" \
  -e POSTGRES_USER="$DB_USER" \
  -e POSTGRES_PASSWORD="$DB_PASSWORD" \
  -e POSTGRES_DB="$DB_NAME" \
  -v "$ROOT:/workspace:ro" \
  "$IMAGE" >/dev/null

for attempt in $(seq 1 60); do
  if psql_exec -Atqc 'SELECT 1;' >/dev/null 2>&1; then break; fi
  [[ "$attempt" != 60 ]] || { docker logs "$CONTAINER" >&2 || true; exit 1; }
  sleep 1
done

psql_exec -f /workspace/tests/fixtures/celar-ai-production-hardening-prerequisites.sql >/dev/null
psql_exec -c "ALTER TABLE project_intake_documents ADD COLUMN IF NOT EXISTS uploaded_by_user_id UUID NULL REFERENCES app_users(user_id);" >/dev/null
psql_exec -c "INSERT INTO app_users(user_id,email,display_name) VALUES('10000000-0000-0000-0000-000000000002','celar-ci-second@example.invalid','Celar CI Second User');" >/dev/null
for prerequisite in \
  052_document_intelligence_runtime.sql \
  053_intelligence_answer_orchestration.sql \
  054_pulse_ai_system_intelligence_conversations.sql \
  061_celar_ai_capability_routing.sql \
  071_ai_runtime_production_hardening.sql; do
  psql_exec -f "/workspace/database/migrations/$prerequisite" >/dev/null
done

psql_exec -f "$MIGRATION" >/dev/null
psql_exec -f "$MIGRATION" >/dev/null

assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='072_celar_ai_conversation_attachments';")" migration_registered_once
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename='pulse_ai_conversation_attachments';")" attachment_table_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM pg_tables WHERE schemaname='public' AND tablename='pulse_ai_conversation_attachment_purge_audit';")" purge_audit_table_created
assert_eq 6 "$(value "SELECT COUNT(*) FROM pg_indexes WHERE schemaname='public' AND tablename='pulse_ai_conversation_attachments';")" attachment_indexes_created
assert_eq 1 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='ATTACH_CELAR_AI_CHAT_DOCUMENTS';")" attachment_permission_created
assert_eq "$(value "SELECT COUNT(DISTINCT app_role_id) FROM app_role_permissions rp JOIN app_permissions p USING(app_permission_id) WHERE p.permission_code='ASK_PULSE_AI_SYSTEM_INTELLIGENCE';")" "$(value "SELECT COUNT(DISTINCT app_role_id) FROM app_role_permissions rp JOIN app_permissions p USING(app_permission_id) WHERE p.permission_code='ATTACH_CELAR_AI_CHAT_DOCUMENTS';")" ask_roles_inherit_attachment_permission
assert_eq 1 "$(value "SELECT COUNT(*) FROM project_intake_documents WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001' AND project_intake_request_id='40000000-0000-0000-0000-000000000001';")" existing_project_document_preserved

expect_failure "INSERT INTO project_intake_documents(project_intake_document_id,project_intake_request_id,project_id,document_type,document_category,original_file_name,stored_file_name,storage_path,upload_source) VALUES('60000000-0000-0000-0000-000000000099',NULL,NULL,'other','other','invalid.txt','invalid.txt','/mnt/invalid','manual_upload');" ownerless_non_chat_document_rejected
expect_failure "INSERT INTO project_intake_documents(project_intake_document_id,project_intake_request_id,project_id,document_type,document_category,original_file_name,stored_file_name,storage_path,upload_source) VALUES('60000000-0000-0000-0000-000000000098',NULL,NULL,'chat_attachment','chat_attachment','invalid.txt','invalid.txt','/mnt/invalid','celar_ai_chat_attachment');" chat_document_without_uploader_rejected
expect_failure "INSERT INTO project_intake_documents(project_intake_document_id,project_intake_request_id,project_id,document_type,document_category,original_file_name,stored_file_name,storage_path,uploaded_by_user_id,upload_source) VALUES('60000000-0000-0000-0000-000000000097',NULL,NULL,'chat_attachment','chat_attachment','orphan.txt','orphan.txt','/mnt/orphan','10000000-0000-0000-0000-000000000001','celar_ai_chat_attachment');" chat_document_without_attachment_rejected

psql_exec <<'SQL'
BEGIN;
INSERT INTO pulse_ai_conversations(
  pulse_ai_conversation_id,actual_user_id,effective_user_id,conversation_mode,title,retention_until
) VALUES (
  '60000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'system_help','evidence.pdf private customer secret',NOW() + INTERVAL '90 days'
);
INSERT INTO pulse_ai_conversations(
  pulse_ai_conversation_id,actual_user_id,effective_user_id,conversation_mode,title,retention_until
) VALUES (
  '60000000-0000-0000-0000-000000000011',
  '10000000-0000-0000-0000-000000000002',
  '10000000-0000-0000-0000-000000000002',
  'system_help','Other owner conversation',NOW() + INTERVAL '90 days'
);

INSERT INTO project_intake_documents(
  project_intake_document_id,project_intake_request_id,project_id,
  document_type,document_category,original_file_name,stored_file_name,
  storage_path,content_type,size_bytes,uploaded_by_user_id,upload_source,
  engineering_visible,ai_timesheet_context_enabled,extraction_status,
  pulse_ai_processing_status,pulse_ai_classification,is_active
) VALUES (
  '60000000-0000-0000-0000-000000000002',NULL,NULL,
  'chat_attachment','chat_attachment','evidence.pdf','random.pdf',
  '/mnt/projectpulse-ci/celar-ai/evidence.pdf','application/pdf',1024,
  '10000000-0000-0000-0000-000000000001','celar_ai_chat_attachment',
  FALSE,FALSE,'not_started','not_requested','restricted_conversation_attachment',TRUE
);

INSERT INTO pulse_ai_conversation_attachments(
  pulse_ai_conversation_attachment_id,pulse_ai_conversation_id,
  project_intake_document_id,uploaded_by_user_id,correlation_id,retention_until
) VALUES (
  '60000000-0000-0000-0000-000000000003',
  '60000000-0000-0000-0000-000000000001',
  '60000000-0000-0000-0000-000000000002',
  '10000000-0000-0000-0000-000000000001',
  'migration-072-test',NOW() + INTERVAL '90 days'
);
COMMIT;

INSERT INTO pulse_ai_document_versions(
  pulse_ai_document_version_id,project_intake_document_id,source_sha256,
  document_version,classification,extraction_method,extraction_contract_version,
  section_count,chunk_count,character_count
) VALUES (
  '60000000-0000-0000-0000-000000000004',
  '60000000-0000-0000-0000-000000000002',repeat('a',64),
  'attachment-v1','restricted_conversation_attachment','test','test-v1',1,1,18
);
INSERT INTO pulse_ai_document_sections(
  pulse_ai_document_version_id,project_intake_document_id,section_index,
  citation_anchor,section_text,character_count,text_sha256
) VALUES (
  '60000000-0000-0000-0000-000000000004',
  '60000000-0000-0000-0000-000000000002',0,
  'section:1','private test text',17,repeat('b',64)
);
INSERT INTO pulse_ai_document_chunks(
  chunk_id,pulse_ai_document_version_id,project_intake_document_id,
  document_version,classification,engineering_visible,access_scope,
  chunk_index,citation_anchor,chunk_text,source_sha256,text_sha256,
  character_count,estimated_token_count,embedding,embedding_dimension,
  embedding_model,embedding_status,index_status
) VALUES (
  repeat('c',64),'60000000-0000-0000-0000-000000000004',
  '60000000-0000-0000-0000-000000000002','attachment-v1',
  'restricted_conversation_attachment',FALSE,'conversation_owner_only',0,
  'section:1','private test text',repeat('a',64),repeat('b',64),17,5,
  ARRAY[0.1,0.2]::double precision[],2,'test','ready','ready'
);

INSERT INTO pulse_ai_answer_runs(
  pulse_ai_answer_run_id,feature_code,purpose_code,answer_status,
  actual_user_id,effective_user_id,question_text,question_sha256,
  request_filters_json,prompt_contract_version,retrieval_contract_version,retrieval_mode,
  retrieved_chunk_count,cited_source_count,source_document_count,
  source_version_count,input_character_count,output_character_count,
  confidence_score,coverage_score,citation_coverage_score,answer_json
) VALUES (
  '60000000-0000-0000-0000-000000000005','PULSE_AI_PRIVATE_HELP_SEARCH',
  'help_search','completed','10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'What does evidence.pdf say?',repeat('d',64),
  '{"AttachmentIds":["60000000-0000-0000-0000-000000000003"]}'::jsonb,
  'test-prompt-v1','test-retrieval-v1',
  'lexical',1,1,1,1,28,31,0.9000,0.9000,1.0000,
  '{"directConclusion":"private attachment answer"}'::jsonb
);

INSERT INTO pulse_ai_answer_runs(
  pulse_ai_answer_run_id,feature_code,purpose_code,answer_status,
  actual_user_id,effective_user_id,question_text,question_sha256,
  request_filters_json,prompt_contract_version,retrieval_contract_version,
  retrieval_mode,answer_json
) VALUES (
  '60000000-0000-0000-0000-000000000012','PULSE_AI_PRIVATE_HELP_SEARCH',
  'help_search','insufficient_evidence',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'Uncited private attachment question that must still be purged.',repeat('e',64),
  '{"AttachmentIds":["60000000-0000-0000-0000-000000000003"]}'::jsonb,
  'test-prompt-v1','test-retrieval-v1','none',
  '{"missingEvidence":["private filename evidence.pdf"]}'::jsonb
);

INSERT INTO pulse_ai_retrieval_events(
  pulse_ai_retrieval_event_id,pulse_ai_answer_run_id,actual_user_id,
  effective_user_id,feature_code,event_code,event_status,retrieval_mode,
  candidate_count,authorized_candidate_count,returned_chunk_count,
  correlation_id,evidence_json
) VALUES (
  '60000000-0000-0000-0000-000000000006',
  '60000000-0000-0000-0000-000000000005',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001',
  'PULSE_AI_PRIVATE_HELP_SEARCH','private_retrieval_completed','succeeded',
  'lexical',1,1,1,'migration-072-retrieval',
  '{"rawQuestionLogged":false,"rawChunkTextLogged":false,"displayStringsLogged":false,"attachmentFileNamesLogged":false,"missingEvidenceCount":1,"conflictCount":0}'::jsonb
);

INSERT INTO pulse_ai_answer_citations(
  pulse_ai_answer_citation_id,pulse_ai_answer_run_id,chunk_id,
  project_intake_document_id,pulse_ai_document_version_id,source_type,
  source_module,document_category,document_version,original_file_name,
  citation_anchor,rank_order,combined_score,source_sha256,text_sha256
) VALUES (
  '60000000-0000-0000-0000-000000000007',
  '60000000-0000-0000-0000-000000000005',repeat('c',64),
  '60000000-0000-0000-0000-000000000002',
  '60000000-0000-0000-0000-000000000004','conversation_attachment',
  '011','chat_attachment','attachment-v1','evidence.pdf','section:1',1,
  0.9000,repeat('a',64),repeat('b',64)
);

INSERT INTO pulse_ai_answer_feedback(
  pulse_ai_answer_feedback_id,pulse_ai_answer_run_id,actual_user_id,
  effective_user_id,feedback_type,feedback_reason,corrected_answer_json,
  training_candidate
) VALUES (
  '60000000-0000-0000-0000-000000000008',
  '60000000-0000-0000-0000-000000000005',
  '10000000-0000-0000-0000-000000000001',
  '10000000-0000-0000-0000-000000000001','accepted_with_edits',
  'The private attachment confirms the answer.',
  '{"answer":"private corrected attachment answer"}'::jsonb,TRUE
);

INSERT INTO pulse_ai_conversation_messages(
  pulse_ai_conversation_message_id,pulse_ai_conversation_id,sequence_number,
  role,message_text,structured_response_json,private_answer_run_id,correlation_id
) VALUES
  ('60000000-0000-0000-0000-000000000009',
   '60000000-0000-0000-0000-000000000001',1,'user',
   'What does the attached evidence say?',
   '{"attachmentIds":["60000000-0000-0000-0000-000000000003"]}'::jsonb,
   NULL,'migration-072-user'),
  ('60000000-0000-0000-0000-000000000010',
   '60000000-0000-0000-0000-000000000001',2,'assistant',
   'The private attachment says the answer.',
   '{"status":"completed","attachmentIds":["60000000-0000-0000-0000-000000000003"]}'::jsonb,
   '60000000-0000-0000-0000-000000000005','migration-072-assistant');

UPDATE pulse_ai_conversation_attachments
SET last_selected_at = NOW(), purge_attempt_count = 1,
    purge_last_attempt_at = NOW(), purge_diagnostic_code = 'test_pending'
WHERE pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003';
SQL

expect_failure "BEGIN; INSERT INTO project_intake_documents(project_intake_document_id,project_intake_request_id,project_id,document_type,document_category,original_file_name,stored_file_name,storage_path,uploaded_by_user_id,upload_source) VALUES('60000000-0000-0000-0000-000000000021',NULL,NULL,'chat_attachment','chat_attachment','mismatch.txt','mismatch.txt','/mnt/mismatch','10000000-0000-0000-0000-000000000001','celar_ai_chat_attachment'); INSERT INTO pulse_ai_conversation_attachments(pulse_ai_conversation_attachment_id,pulse_ai_conversation_id,project_intake_document_id,uploaded_by_user_id,retention_until) VALUES('60000000-0000-0000-0000-000000000022','60000000-0000-0000-0000-000000000001','60000000-0000-0000-0000-000000000021','10000000-0000-0000-0000-000000000002',NOW()+INTERVAL '1 day'); COMMIT;" mismatched_attachment_uploader_rejected
expect_failure "BEGIN; INSERT INTO project_intake_documents(project_intake_document_id,project_intake_request_id,project_id,document_type,document_category,original_file_name,stored_file_name,storage_path,uploaded_by_user_id,upload_source) VALUES('60000000-0000-0000-0000-000000000023',NULL,'30000000-0000-0000-0000-000000000001','chat_attachment','chat_attachment','project.txt','project.txt','/mnt/project','10000000-0000-0000-0000-000000000001','celar_ai_chat_attachment'); INSERT INTO pulse_ai_conversation_attachments(pulse_ai_conversation_attachment_id,pulse_ai_conversation_id,project_intake_document_id,uploaded_by_user_id,retention_until) VALUES('60000000-0000-0000-0000-000000000024','60000000-0000-0000-0000-000000000001','60000000-0000-0000-0000-000000000023','10000000-0000-0000-0000-000000000001',NOW()+INTERVAL '1 day'); COMMIT;" chat_document_project_link_rejected
expect_failure "BEGIN; INSERT INTO project_intake_documents(project_intake_document_id,project_intake_request_id,project_id,document_type,document_category,original_file_name,stored_file_name,storage_path,uploaded_by_user_id,upload_source) VALUES('60000000-0000-0000-0000-000000000025',NULL,NULL,'chat_attachment','chat_attachment','owner.txt','owner.txt','/mnt/owner','10000000-0000-0000-0000-000000000001','celar_ai_chat_attachment'); INSERT INTO pulse_ai_conversation_attachments(pulse_ai_conversation_attachment_id,pulse_ai_conversation_id,project_intake_document_id,uploaded_by_user_id,retention_until) VALUES('60000000-0000-0000-0000-000000000026','60000000-0000-0000-0000-000000000011','60000000-0000-0000-0000-000000000025','10000000-0000-0000-0000-000000000001',NOW()+INTERVAL '1 day'); COMMIT;" mismatched_conversation_owner_rejected

assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_conversation_attachments WHERE uploaded_by_user_id IS NOT NULL AND last_selected_at IS NOT NULL AND purge_attempt_count=1;")" mutable_lifecycle_evidence_allowed
expect_failure "UPDATE pulse_ai_conversation_attachments SET pulse_ai_conversation_id=gen_random_uuid() WHERE pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003';" immutable_attachment_ownership
expect_failure "UPDATE pulse_ai_conversations SET actual_user_id='10000000-0000-0000-0000-000000000002',effective_user_id='10000000-0000-0000-0000-000000000002' WHERE pulse_ai_conversation_id='60000000-0000-0000-0000-000000000001';" conversation_attachment_owner_reassignment_rejected
expect_failure "DELETE FROM pulse_ai_conversation_attachments WHERE pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003';" attachment_delete_cannot_orphan_document
expect_failure "DELETE FROM project_intake_documents WHERE project_intake_document_id='60000000-0000-0000-0000-000000000002';" document_delete_requires_governed_purge
expect_failure "DELETE FROM pulse_ai_conversations WHERE pulse_ai_conversation_id='60000000-0000-0000-0000-000000000001';" conversation_delete_requires_attachment_cleanup
expect_file_failure "$ROLLBACK" 'Refusing migration 072 rollback while Celar AI conversation attachment records remain.' guarded_rollback_with_live_attachment

psql_exec <<'SQL'
BEGIN;
INSERT INTO pulse_ai_conversation_attachment_purge_audit(
  pulse_ai_conversation_attachment_id,pulse_ai_conversation_id,
  project_intake_document_id,uploaded_by_user_id,correlation_id,purge_reason,
  retention_until,revoked_at,storage_purged_at
)
SELECT pulse_ai_conversation_attachment_id,pulse_ai_conversation_id,
       project_intake_document_id,uploaded_by_user_id,correlation_id,
       'migration test purge',retention_until,NOW(),NOW()
FROM pulse_ai_conversation_attachments
WHERE pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003';

UPDATE pulse_ai_conversations conversation
SET title='Celar AI private attachment conversation',updated_at=NOW()
FROM pulse_ai_conversation_attachments attachment
WHERE attachment.pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003'
  AND conversation.pulse_ai_conversation_id=attachment.pulse_ai_conversation_id;

CREATE TEMP TABLE celar_ai_attachment_purge_answer_runs(
  pulse_ai_answer_run_id UUID PRIMARY KEY
) ON COMMIT DROP;
INSERT INTO celar_ai_attachment_purge_answer_runs(pulse_ai_answer_run_id)
SELECT citation.pulse_ai_answer_run_id
FROM pulse_ai_answer_citations citation
JOIN pulse_ai_conversation_attachments attachment
  ON attachment.project_intake_document_id=citation.project_intake_document_id
WHERE attachment.pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003'
UNION
SELECT answer_run.pulse_ai_answer_run_id
FROM pulse_ai_answer_runs answer_run
JOIN pulse_ai_conversation_attachments attachment
  ON attachment.pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003'
WHERE answer_run.request_filters_json @> jsonb_build_object(
  'AttachmentIds',jsonb_build_array(attachment.pulse_ai_conversation_attachment_id::text)
);

SELECT answer_run.pulse_ai_answer_run_id
FROM pulse_ai_answer_runs answer_run
WHERE answer_run.pulse_ai_answer_run_id IN (
  SELECT pulse_ai_answer_run_id FROM celar_ai_attachment_purge_answer_runs
)
FOR UPDATE;

UPDATE pulse_ai_conversation_messages message
SET message_text = '[Private attachment-derived content removed by the governed retention policy.]',
    structured_response_json = jsonb_build_object(
      'status','private_attachment_retention_purged','rawContentRetained',FALSE
    ),
    private_answer_run_id = NULL,
    model_provider = '',model_name = '',source_states_json = '[]'::jsonb
WHERE message.private_answer_run_id IN (
  SELECT pulse_ai_answer_run_id FROM celar_ai_attachment_purge_answer_runs
)
OR EXISTS (
  SELECT 1 FROM pulse_ai_conversation_attachments attachment
  WHERE attachment.pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003'
    AND attachment.pulse_ai_conversation_id=message.pulse_ai_conversation_id
    AND message.structured_response_json @> jsonb_build_object(
      'attachmentIds',jsonb_build_array(attachment.pulse_ai_conversation_attachment_id::text)
    )
);

UPDATE pulse_ai_answer_feedback feedback
SET feedback_reason='[Private attachment-derived feedback removed by the governed retention policy.]',
    corrected_answer_json=jsonb_build_object(
      'status','private_attachment_retention_purged','rawContentRetained',FALSE
    ),
    training_candidate=FALSE,training_review_status='needs_redaction'
WHERE feedback.pulse_ai_answer_run_id IN (
  SELECT pulse_ai_answer_run_id FROM celar_ai_attachment_purge_answer_runs
);

UPDATE pulse_ai_answer_runs answer_run
SET answer_status='blocked',project_code='',
    question_text='[Private attachment-derived question removed by the governed retention policy.]',
    question_sha256=repeat('0',64),request_filters_json='{}'::jsonb,
    private_model_provider='',private_model_name='',retrieval_mode='none',
    retrieved_chunk_count=0,cited_source_count=0,source_document_count=0,
    source_version_count=0,input_character_count=0,output_character_count=0,
    confidence_score=0,coverage_score=0,citation_coverage_score=0,
    answer_json=jsonb_build_object(
      'status','private_attachment_retention_purged','rawContentRetained',FALSE
    ),
    warning_codes=jsonb_build_array('private_attachment_retention_purged'),
    missing_evidence='[]'::jsonb,conflicts_json='[]'::jsonb,
    source_health_json='{}'::jsonb,
    privacy_evidence_json=jsonb_build_object('privateAttachmentContentPurged',TRUE),
    diagnostic_code='private_attachment_retention_purged',diagnostic_message=''
WHERE answer_run.pulse_ai_answer_run_id IN (
  SELECT pulse_ai_answer_run_id FROM celar_ai_attachment_purge_answer_runs
);

DELETE FROM pulse_ai_answer_citations citation
WHERE citation.pulse_ai_answer_run_id IN (
  SELECT pulse_ai_answer_run_id FROM celar_ai_attachment_purge_answer_runs
);

DELETE FROM project_intake_documents document
USING pulse_ai_conversation_attachments attachment
WHERE attachment.pulse_ai_conversation_attachment_id='60000000-0000-0000-0000-000000000003'
  AND document.project_intake_document_id=attachment.project_intake_document_id;
COMMIT;
SQL
assert_eq 0 "$(value "SELECT COUNT(*) FROM pulse_ai_conversation_attachments;")" document_cleanup_cascades_attachment_metadata
assert_eq 0 "$(value "SELECT COUNT(*) FROM pulse_ai_document_sections WHERE project_intake_document_id='60000000-0000-0000-0000-000000000002';")" purge_removes_extracted_section_text
assert_eq 0 "$(value "SELECT COUNT(*) FROM pulse_ai_document_chunks WHERE project_intake_document_id='60000000-0000-0000-0000-000000000002';")" purge_removes_chunks_and_embeddings
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_retrieval_events WHERE pulse_ai_answer_run_id='60000000-0000-0000-0000-000000000005';")" immutable_retrieval_event_preserved
assert_eq 0 "$(value "SELECT COUNT(*) FROM pulse_ai_retrieval_events WHERE pulse_ai_answer_run_id='60000000-0000-0000-0000-000000000005' AND evidence_json::text ILIKE '%evidence.pdf%';")" immutable_retrieval_event_excludes_private_filename
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_answer_runs WHERE pulse_ai_answer_run_id='60000000-0000-0000-0000-000000000005' AND answer_status='blocked' AND question_sha256=repeat('0',64) AND answer_json->>'status'='private_attachment_retention_purged';")" answer_run_content_redacted_in_place
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_answer_runs WHERE pulse_ai_answer_run_id='60000000-0000-0000-0000-000000000012' AND answer_status='blocked' AND question_sha256=repeat('0',64) AND answer_json->>'status'='private_attachment_retention_purged';")" uncited_attachment_answer_run_redacted_from_request_scope
assert_eq 0 "$(value "SELECT COUNT(*) FROM pulse_ai_answer_citations WHERE pulse_ai_answer_run_id='60000000-0000-0000-0000-000000000005';")" attachment_citations_removed
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_answer_feedback WHERE pulse_ai_answer_run_id='60000000-0000-0000-0000-000000000005' AND training_candidate=FALSE AND corrected_answer_json->>'status'='private_attachment_retention_purged';")" attachment_feedback_redacted
assert_eq 2 "$(value "SELECT COUNT(*) FROM pulse_ai_conversation_messages WHERE pulse_ai_conversation_id='60000000-0000-0000-0000-000000000001' AND message_text='[Private attachment-derived content removed by the governed retention policy.]' AND structured_response_json->>'status'='private_attachment_retention_purged';")" attachment_messages_redacted
assert_eq 1 "$(value "SELECT COUNT(*) FROM pulse_ai_conversations WHERE pulse_ai_conversation_id='60000000-0000-0000-0000-000000000001' AND title='Celar AI private attachment conversation';")" attachment_conversation_title_redacted
expect_failure "UPDATE pulse_ai_answer_runs SET answer_status='completed',question_text='resurrected private attachment content',answer_json='{\"answer\":\"resurrected private attachment content\"}'::jsonb,diagnostic_code='' WHERE pulse_ai_answer_run_id='60000000-0000-0000-0000-000000000005';" purged_answer_content_cannot_be_resurrected
expect_failure "INSERT INTO pulse_ai_answer_feedback(pulse_ai_answer_run_id,actual_user_id,effective_user_id,feedback_type,feedback_reason,corrected_answer_json,training_candidate) VALUES('60000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','accepted_with_edits','late private feedback after purge','{\"answer\":\"late private correction after purge\"}'::jsonb,FALSE);" purged_answer_rejects_late_feedback
expect_failure "UPDATE pulse_ai_answer_feedback SET feedback_reason='restored private feedback',corrected_answer_json='{\"answer\":\"restored\"}'::jsonb WHERE pulse_ai_answer_run_id='60000000-0000-0000-0000-000000000005';" purged_answer_feedback_cannot_be_restored
psql_exec -c "DELETE FROM pulse_ai_conversations WHERE pulse_ai_conversation_id='60000000-0000-0000-0000-000000000001';" >/dev/null
assert_eq 0 "$(value "SELECT COUNT(*) FROM pulse_ai_conversations WHERE pulse_ai_conversation_id='60000000-0000-0000-0000-000000000001';")" conversation_deletable_after_content_purge
expect_file_failure "$ROLLBACK" 'Refusing migration 072 rollback while Celar AI attachment purge-audit records remain.' guarded_rollback_with_purge_audit
psql_exec -c "DELETE FROM pulse_ai_conversation_attachment_purge_audit;" >/dev/null
psql_exec -c "DELETE FROM pulse_ai_conversations WHERE pulse_ai_conversation_id='60000000-0000-0000-0000-000000000011';" >/dev/null
psql_exec -f "$ROLLBACK" >/dev/null

assert_eq '' "$(value "SELECT COALESCE(to_regclass('public.pulse_ai_conversation_attachments')::text,'');")" rollback_removed_attachment_table
assert_eq 'NO' "$(value "SELECT is_nullable FROM information_schema.columns WHERE table_schema='public' AND table_name='project_intake_documents' AND column_name='project_intake_request_id';")" rollback_restored_request_owner_requirement
assert_eq 0 "$(value "SELECT COUNT(*) FROM app_permissions WHERE permission_code='ATTACH_CELAR_AI_CHAT_DOCUMENTS';")" rollback_removed_attachment_permission
assert_eq 1 "$(value "SELECT COUNT(*) FROM project_intake_documents WHERE project_intake_document_id='50000000-0000-0000-0000-000000000001';")" rollback_preserved_existing_project_document

psql_exec -f "$MIGRATION" >/dev/null
assert_eq 1 "$(value "SELECT COUNT(*) FROM schema_migrations WHERE migration_id='072_celar_ai_conversation_attachments';")" safe_reapply

echo 'CELAR_AI_CONVERSATION_ATTACHMENTS_MIGRATION_072=PASS'
