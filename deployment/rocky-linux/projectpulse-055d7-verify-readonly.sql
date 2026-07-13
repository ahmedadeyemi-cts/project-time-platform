BEGIN TRANSACTION READ ONLY;

SELECT
    column_name,
    data_type,
    is_nullable
FROM information_schema.columns
WHERE table_schema = 'public'
  AND table_name = 'projects'
  AND column_name IN (
      'work_type',
      'contract_type',
      'sell_quote_number',
      'salesforce_id_number',
      'certinia_id_number',
      'sow_signed_date'
  )
ORDER BY column_name;

SELECT
    trigger_name,
    event_manipulation,
    action_timing
FROM information_schema.triggers
WHERE event_object_schema = 'public'
  AND event_object_table = 'work_register_intake_commits'
  AND trigger_name IN (
      'trg_projectpulse055d7_after_intake_commit',
      'trg_projectpulse055d7_sync_task_assignment_history'
  )
ORDER BY trigger_name;

WITH latest_commits AS (
    SELECT
        commit.work_register_intake_package_id,
        commit.project_id,
        commit.committed_at
    FROM work_register_intake_commits commit
    ORDER BY commit.committed_at DESC
    LIMIT 10
)
SELECT
    latest.committed_at,
    latest.work_register_intake_package_id AS package_id,
    project.project_id,
    project.project_code,
    project.project_name,
    project.work_type,
    project.contract_type,
    project.sell_quote_number,
    project.salesforce_id_number,
    project.certinia_id_number,
    project.sow_signed_date,
    (
        SELECT count(*)
        FROM work_register_project_documents document
        WHERE document.project_id = project.project_id
          AND document.document_name <> ''
    ) AS named_document_count,
    (
        SELECT count(*)
        FROM project_assignments assignment
        WHERE assignment.project_id = project.project_id
          AND assignment.assignment_source = 'work_register_intake_final_save'
    ) AS synchronized_assignment_count
FROM latest_commits latest
JOIN projects project
  ON project.project_id = latest.project_id
ORDER BY latest.committed_at DESC;

SELECT
    project.project_code,
    project.project_name,
    project.work_type,
    task.task_code,
    task.task_name,
    app_user.display_name AS assigned_user,
    assignment.assigned_hours,
    assignment.allocation_percent,
    CASE
        WHEN lower(project.work_type) IN ('project', 'iqs') THEN 'regular'
        ELSE 'requests'
    END AS expected_time_entry_section
FROM project_assignments assignment
JOIN projects project
  ON project.project_id = assignment.project_id
JOIN project_tasks task
  ON task.task_id = assignment.task_id
JOIN app_users app_user
  ON app_user.user_id = assignment.user_id
WHERE assignment.assignment_source = 'work_register_intake_final_save'
ORDER BY project.created_at DESC, task.task_code, app_user.display_name
LIMIT 100;

SELECT
    project.project_code,
    document.document_type,
    document.document_name,
    document.original_file_name,
    document.document_routing_status
FROM work_register_project_documents document
JOIN projects project
  ON project.project_id = document.project_id
WHERE document.work_register_intake_package_id IS NOT NULL
ORDER BY document.created_at DESC
LIMIT 50;

ROLLBACK;
