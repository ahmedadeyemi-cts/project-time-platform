using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Npgsql;

namespace ProjectTime.Api.Ai;

public enum CelarAiInternalDataQueryKind
{
    PersonProjectCount,
    PersonProjectList,
    PersonTaskCount,
    PersonTaskList,
    PersonWorkSummary,
    ProjectStakeholderLookup,
    ProjectHistory
}

public sealed record CelarAiInternalDataQuery(
    CelarAiInternalDataQueryKind Kind,
    string PersonReference,
    bool CountRequested,
    string ProjectReference = "",
    string RequestedProjectRole = "");

/// <summary>
/// Resolves structured Pulse facts locally from the authoritative database.
/// Every query is read-only, permission-scoped to the effective user, and
/// deterministic. No question text, identity, row, or result is sent to an
/// external model.
/// </summary>
public sealed partial class CelarAiInternalDataService
{
    public const string ContractVersion = "celar-ai-internal-data-v1-20260807";
    public const string IntentCode = "internal_data";
    public const string EnterpriseFactsVersion = "celar-ai-enterprise-internal-facts-v1-20260901";

    private static readonly Regex[] PersonWorkSummaryPatterns =
    [
        new(@"^\s*how\s+many\s+(?:active\s+)?projects?\s+(?:and|,)\s+(?:how\s+many\s+)?(?:active\s+)?tasks?\s+(?:does|do)\s+(?<person>.+?)\s+(?:have|has)(?:\s+assigned(?:\s+to\s+(?:him|her|them))?)?\s*[?.!]*\s*$", Options),
        new(@"^\s*how\s+many\s+(?:active\s+)?tasks?\s+(?:and|,)\s+(?:how\s+many\s+)?(?:active\s+)?projects?\s+(?:does|do)\s+(?<person>.+?)\s+(?:have|has)(?:\s+assigned(?:\s+to\s+(?:him|her|them))?)?\s*[?.!]*\s*$", Options),
        new(@"^\s*(?:show|give|tell)(?:\s+me)?\s+(?<person>.+?)(?:'s|’s)?\s+(?:current\s+|active\s+)?projects?\s+(?:and|,)\s+(?:current\s+|active\s+)?tasks?\s*[?.!]*\s*$", Options)
    ];

    private static readonly Regex[] PersonProjectCountPatterns =
    [
        new(@"^\s*how\s+many\s+(?:active\s+)?projects?\s+(?:does|do)\s+(?<person>.+?)\s+(?:have|has)(?:\s+assigned(?:\s+to\s+(?:him|her|them))?)?\s*[?.!]*\s*$", Options),
        new(@"^\s*how\s+many\s+(?:active\s+)?projects?\s+(?:are\s+)?assigned\s+to\s+(?<person>.+?)\s*[?.!]*\s*$", Options),
        new(@"^\s*(?:count|total)\s+(?:the\s+)?(?:active\s+)?projects?\s+(?:assigned\s+to|for)\s+(?<person>.+?)\s*[?.!]*\s*$", Options)
    ];

    private static readonly Regex[] PersonProjectListPatterns =
    [
        new(@"^\s*(?:which|what)\s+(?:active\s+)?projects?\s+(?:does|do)\s+(?<person>.+?)\s+(?:have|manage|work\s+on)\s*[?.!]*\s*$", Options),
        new(@"^\s*(?:which|what|list|show(?:\s+me)?)\s+(?:active\s+)?projects?\s+(?:are\s+)?(?:assigned\s+to|for)\s+(?<person>.+?)\s*[?.!]*\s*$", Options),
        new(@"^\s*(?:list|show)\s+(?<person>.+?)(?:'s|’s)\s+(?:active\s+)?projects?\s*[?.!]*\s*$", Options),
        new(@"^\s*(?:what|which)\s+(?:projects?|work)\s+(?:is|are)\s+(?<person>.+?)\s+(?:assigned\s+to|working\s+on)\s*[?.!]*\s*$", Options)
    ];

    private static readonly Regex[] PersonTaskCountPatterns =
    [
        new(@"^\s*how\s+many\s+(?:active\s+)?tasks?\s+(?:does|do)\s+(?<person>.+?)\s+(?:have|has)(?:\s+assigned(?:\s+to\s+(?:him|her|them))?)?\s*[?.!]*\s*$", Options),
        new(@"^\s*how\s+many\s+(?:active\s+)?tasks?\s+(?:are\s+)?assigned\s+to\s+(?<person>.+?)\s*[?.!]*\s*$", Options)
    ];

    private static readonly Regex[] PersonTaskListPatterns =
    [
        new(@"^\s*(?:which|what)\s+(?:active\s+)?tasks?\s+(?:does|do)\s+(?<person>.+?)\s+(?:have|work\s+on)\s*[?.!]*\s*$", Options),
        new(@"^\s*(?:which|what|list|show(?:\s+me)?)\s+(?:active\s+)?tasks?\s+(?:are\s+)?(?:assigned\s+to|for)\s+(?<person>.+?)\s*[?.!]*\s*$", Options),
        new(@"^\s*what\s+(?:is|are)\s+(?<person>.+?)\s+(?:working\s+on|doing|assigned\s+to)\s*[?.!]*\s*$", Options),
        new(@"^\s*what\s+(?:does|do)\s+(?<person>.+?)\s+(?:work\s+on|have\s+assigned)\s*[?.!]*\s*$", Options)
    ];

    private static readonly Regex[] ProjectStakeholderPatterns =
    [
        new(@"^\s*who\s+(?:is|are)\s+(?:assigned\s+as\s+)?(?:the\s+)?(?<role>account\s+executive|ae|sales\s*person|sales\s+person|sales\s+rep(?:resentative)?|solution\s+architect|sa|project\s+manager|pm)\s*(?:assigned\s+)?(?:to|for|on)\s+(?:project\s+)?(?<project>.+?)\s*[?.!]*\s*$", Options),
        new(@"^\s*who\s+is\s+assigned\s+as\s+(?:the\s+)?(?<role>account\s+executive|ae|sales\s*person|sales\s+person|sales\s+rep(?:resentative)?|solution\s+architect|sa|project\s+manager|pm)\s+(?:to|for|on)\s+(?:project\s+)?(?<project>.+?)\s*[?.!]*\s*$", Options),
        new(@"^\s*(?:what|who)\s+(?:is\s+)?(?:the\s+)?(?<role>account\s+executive|ae|sales\s*person|sales\s+person|sales\s+rep(?:resentative)?|solution\s+architect|sa|project\s+manager|pm)\s+(?:for|on)\s+(?:project\s+)?(?<project>.+?)\s*[?.!]*\s*$", Options)
    ];

    private static readonly Regex[] ProjectHistoryPatterns =
    [
        new(@"^\s*(?:show|give|tell)(?:\s+me)?\s+(?:the\s+)?(?:project\s+)?(?:history|historical\s+context|timeline)\s+(?:for|of|on)\s+(?:project\s+)?(?<project>.+?)\s*[?.!]*\s*$", Options),
        new(@"^\s*what\s+(?:is|was)\s+(?:the\s+)?(?:project\s+)?(?:history|historical\s+context|timeline)\s+(?:for|of|on)\s+(?:project\s+)?(?<project>.+?)\s*[?.!]*\s*$", Options),
        new(@"^\s*what\s+happened\s+(?:on|with|to)\s+(?:project\s+)?(?<project>.+?)\s*[?.!]*\s*$", Options)
    ];

    private const RegexOptions Options = RegexOptions.IgnoreCase
        | RegexOptions.CultureInvariant
        | RegexOptions.Compiled;

    private const string ScopeCte = """
        WITH requester AS (
            SELECT
                user_id,
                COALESCE(team_name, '') AS team_name,
                COALESCE(department_name, '') AS department_name,
                COALESCE(department, '') AS department
            FROM app_users
            WHERE user_id = @effective_user_id
              AND is_active = TRUE
        ),
        team_members AS (
            SELECT DISTINCT member.user_id
            FROM app_users member
            CROSS JOIN requester
            WHERE member.is_active = TRUE
              AND (
                  (requester.team_name <> '' AND LOWER(COALESCE(member.team_name, '')) = LOWER(requester.team_name))
                  OR (requester.department_name <> '' AND LOWER(COALESCE(member.department_name, '')) = LOWER(requester.department_name))
                  OR (requester.department <> '' AND LOWER(COALESCE(member.department, '')) = LOWER(requester.department))
                  OR EXISTS (
                      SELECT 1
                      FROM reporting_relationships relationship
                      WHERE relationship.employee_user_id = member.user_id
                        AND (relationship.manager_user_id = @effective_user_id OR relationship.team_lead_user_id = @effective_user_id)
                        AND relationship.effective_start_date <= CURRENT_DATE
                        AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date >= CURRENT_DATE)
                  )
                  OR EXISTS (
                      SELECT 1
                      FROM projectpulse_team_scope_assignments scope
                      WHERE scope.scoped_user_id = @effective_user_id
                        AND scope.is_active = TRUE
                        AND (
                            (scope.team_name IS NOT NULL AND LOWER(COALESCE(member.team_name, '')) = LOWER(scope.team_name))
                            OR (scope.department_name IS NOT NULL AND LOWER(COALESCE(member.department_name, '')) = LOWER(scope.department_name))
                            OR scope.manager_user_id = member.user_id
                        )
                  )
              )
        ),
        current_task_assignments_raw AS (
            SELECT
                assignment.project_id,
                task.task_id,
                assignment.user_id,
                task.task_code,
                task.task_name,
                assignment.effective_start_date,
                assignment.effective_end_date,
                COALESCE(assignment.assigned_hours, 0)::numeric AS assigned_hours,
                'project_assignments'::text AS source_code,
                2 AS source_priority
            FROM project_assignments assignment
            JOIN project_tasks task ON task.task_id = assignment.task_id
            WHERE assignment.task_id IS NOT NULL
              AND task.is_active = TRUE
              AND assignment.effective_start_date <= CURRENT_DATE
              AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE)
              AND COALESCE(assignment.module001a_closeout_status, 'active') = 'active'

            UNION ALL

            SELECT
                history.project_id,
                task.task_id,
                history.assigned_user_id,
                task.task_code,
                COALESCE(NULLIF(history.task_name_snapshot, ''), task.task_name) AS task_name,
                history.effective_start_date,
                history.effective_end_date,
                COALESCE(history.allocated_hours, 0)::numeric AS assigned_hours,
                'work_register_task_assignment_history'::text AS source_code,
                1 AS source_priority
            FROM work_register_task_assignment_history history
            JOIN project_tasks task
              ON task.project_id = history.project_id
             AND (task.task_id::text = history.task_id_text OR task.task_code = history.task_id_text)
            WHERE history.assigned_user_id IS NOT NULL
              AND LOWER(history.assignment_status) = 'active'
              AND task.is_active = TRUE
              AND history.effective_start_date <= CURRENT_DATE
              AND (history.effective_end_date IS NULL OR history.effective_end_date >= CURRENT_DATE)
              AND NOT EXISTS (
                  SELECT 1
                  FROM project_assignments closed_assignment
                  JOIN project_tasks closed_task ON closed_task.task_id = closed_assignment.task_id
                  WHERE closed_assignment.project_id = history.project_id
                    AND closed_assignment.user_id = history.assigned_user_id
                    AND (closed_task.task_id::text = history.task_id_text OR closed_task.task_code = history.task_id_text)
                    AND COALESCE(closed_assignment.module001a_closeout_status, 'active') <> 'active'
              )
        ),
        current_task_assignments AS (
            SELECT DISTINCT ON (project_id, task_id, user_id)
                project_id,
                task_id,
                user_id,
                task_code,
                task_name,
                effective_start_date,
                effective_end_date,
                assigned_hours,
                source_code
            FROM current_task_assignments_raw
            ORDER BY project_id, task_id, user_id, source_priority, effective_start_date DESC
        ),
        current_project_people AS (
            SELECT DISTINCT
                assignment.project_id,
                assignment.user_id,
                'project_assignments'::text AS source_code
            FROM project_assignments assignment
            WHERE assignment.effective_start_date <= CURRENT_DATE
              AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE)
              AND COALESCE(assignment.module001a_closeout_status, 'active') = 'active'

            UNION

            SELECT DISTINCT
                history.project_id,
                history.assigned_user_id AS user_id,
                'work_register_task_assignment_history'::text AS source_code
            FROM work_register_task_assignment_history history
            WHERE history.assigned_user_id IS NOT NULL
              AND LOWER(history.assignment_status) = 'active'
              AND history.effective_start_date <= CURRENT_DATE
              AND (history.effective_end_date IS NULL OR history.effective_end_date >= CURRENT_DATE)
              AND NOT EXISTS (
                  SELECT 1
                  FROM project_assignments closed_assignment
                  JOIN project_tasks closed_task ON closed_task.task_id = closed_assignment.task_id
                  WHERE closed_assignment.project_id = history.project_id
                    AND closed_assignment.user_id = history.assigned_user_id
                    AND (closed_task.task_id::text = history.task_id_text OR closed_task.task_code = history.task_id_text)
                    AND COALESCE(closed_assignment.module001a_closeout_status, 'active') <> 'active'
              )

            UNION

            SELECT DISTINCT
                request.project_id,
                assignment.user_id,
                'engineering_resource_request_assignments'::text AS source_code
            FROM engineering_resource_requests request
            JOIN engineering_resource_request_assignments assignment
              ON assignment.engineering_resource_request_id = request.engineering_resource_request_id
            WHERE request.project_id IS NOT NULL
              AND LOWER(assignment.assignment_status) IN ('assigned', 'confirmed', 'active', 'in_progress')
              AND LOWER(COALESCE(request.request_status, '')) NOT IN ('cancelled', 'canceled', 'rejected', 'closed')
              AND COALESCE(request.target_start_date, CURRENT_DATE) <= CURRENT_DATE
              AND (request.target_end_date IS NULL OR request.target_end_date >= CURRENT_DATE)
        ),
        scoped_projects_all AS (
            SELECT DISTINCT project.*
            FROM projects project
            CROSS JOIN requester
            WHERE (
               @is_broad_scope = TRUE
               OR (@can_view_managed_projects = TRUE AND project.project_manager_user_id = @effective_user_id)
               OR project.account_executive_user_id = @effective_user_id
               OR project.solution_architect_user_id = @effective_user_id
               OR EXISTS (
                    SELECT 1
                    FROM current_project_people own_assignment
                    WHERE own_assignment.project_id = project.project_id
                      AND own_assignment.user_id = @effective_user_id
               )
               OR (
                    @can_view_team_scope = TRUE
                    AND (
                        project.project_manager_user_id IN (SELECT user_id FROM team_members)
                        OR project.account_executive_user_id IN (SELECT user_id FROM team_members)
                        OR project.solution_architect_user_id IN (SELECT user_id FROM team_members)
                        OR EXISTS (
                            SELECT 1
                            FROM current_project_people team_assignment
                            WHERE team_assignment.project_id = project.project_id
                              AND team_assignment.user_id IN (SELECT user_id FROM team_members)
                        )
                    )
               )
              )
        ),
        scoped_projects AS (
            SELECT *
            FROM scoped_projects_all
            WHERE LOWER(COALESCE(status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
        ),
        authorized_people AS (
            SELECT user_id FROM requester
            UNION
            SELECT project_manager_user_id
            FROM scoped_projects
            WHERE project_manager_user_id IS NOT NULL
            UNION
            SELECT account_executive_user_id
            FROM scoped_projects
            WHERE account_executive_user_id IS NOT NULL
            UNION
            SELECT solution_architect_user_id
            FROM scoped_projects
            WHERE solution_architect_user_id IS NOT NULL
            UNION
            SELECT DISTINCT assignment.user_id
            FROM current_project_people assignment
            JOIN scoped_projects project ON project.project_id = assignment.project_id
        )
        """;

    private static readonly string ExactPersonSql = ScopeCte + """
        SELECT
            person.user_id,
            person.display_name,
            person.email,
            BOOL_OR(alias.celar_ai_identity_alias_id IS NOT NULL) AS matched_verified_alias
        FROM app_users person
        JOIN authorized_people allowed ON allowed.user_id = person.user_id
        LEFT JOIN celar_ai_identity_aliases alias
          ON alias.user_id = person.user_id
         AND alias.is_active = TRUE
         AND alias.is_verified = TRUE
         AND regexp_replace(lower(trim(alias.alias_text)), '[^a-z0-9]+', '', 'g') = @normalized_person
        WHERE person.is_active = TRUE
          AND (
              regexp_replace(lower(trim(person.display_name)), '[^a-z0-9]+', '', 'g') = @normalized_person
              OR lower(trim(person.email)) = @person_lower
              OR alias.celar_ai_identity_alias_id IS NOT NULL
          )
        GROUP BY person.user_id, person.display_name, person.email
        ORDER BY
            CASE WHEN regexp_replace(lower(trim(person.display_name)), '[^a-z0-9]+', '', 'g') = @normalized_person THEN 0 ELSE 1 END,
            person.display_name,
            person.email;
        """;

    private static readonly string AuthorizedPeopleSql = ScopeCte + """
        SELECT DISTINCT person.user_id, person.display_name, person.email
        FROM app_users person
        JOIN authorized_people allowed ON allowed.user_id = person.user_id
        WHERE person.is_active = TRUE
        ORDER BY person.display_name, person.email
        LIMIT 500;
        """;

    private static readonly string ExactNamePartPersonSql = ScopeCte + """
        , exact_name_part_people AS (
            SELECT DISTINCT person.user_id, person.display_name, person.email
            FROM app_users person
            JOIN authorized_people allowed ON allowed.user_id = person.user_id
            CROSS JOIN LATERAL regexp_split_to_table(
                lower(trim(person.display_name)),
                '[^a-z0-9]+'
            ) name_part(value)
            WHERE person.is_active = TRUE
              AND name_part.value = @name_part
        )
        SELECT
            person.user_id,
            person.display_name,
            person.email,
            COUNT(*) OVER () AS authorized_match_count
        FROM exact_name_part_people person
        ORDER BY person.display_name, person.email
        LIMIT 8;
        """;

    private static readonly string PersonProjectsSql = ScopeCte + """
        , person_projects AS (
            SELECT
                project.project_id,
                project.project_code,
                project.project_name,
                project.status,
                project.project_manager_user_id = @person_user_id AS is_project_manager,
                project.account_executive_user_id = @person_user_id AS is_account_executive,
                project.solution_architect_user_id = @person_user_id AS is_solution_architect,
                EXISTS (
                    SELECT 1
                    FROM current_project_people assignment
                    WHERE assignment.project_id = project.project_id
                      AND assignment.user_id = @person_user_id
                ) AS is_assigned_resource,
                (
                    SELECT COUNT(DISTINCT assignment.task_id)::bigint
                    FROM current_task_assignments assignment
                    WHERE assignment.project_id = project.project_id
                      AND assignment.user_id = @person_user_id
                ) AS active_task_assignment_count
            FROM scoped_projects project
            WHERE LOWER(COALESCE(project.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
              AND (
                  project.project_manager_user_id = @person_user_id
                  OR project.account_executive_user_id = @person_user_id
                  OR project.solution_architect_user_id = @person_user_id
                  OR EXISTS (
                      SELECT 1
                      FROM current_project_people assignment
                      WHERE assignment.project_id = project.project_id
                        AND assignment.user_id = @person_user_id
                  )
              )
        )
        SELECT
            project_id,
            project_code,
            project_name,
            status,
            is_project_manager,
            is_account_executive,
            is_solution_architect,
            is_assigned_resource,
            active_task_assignment_count,
            COUNT(*) OVER ()::bigint AS total_count
        FROM person_projects
        ORDER BY project_code, project_name
        LIMIT 100;
        """;

    private static readonly string PersonTasksSql = ScopeCte + """
        , person_tasks AS (
            SELECT DISTINCT
                task.task_id,
                project.project_id,
                project.project_code,
                project.project_name,
                task.task_code,
                task.task_name,
                assignment.effective_start_date,
                assignment.effective_end_date,
                assignment.assigned_hours,
                assignment.source_code
            FROM scoped_projects project
            JOIN current_task_assignments assignment ON assignment.project_id = project.project_id
            JOIN project_tasks task ON task.task_id = assignment.task_id
            WHERE assignment.user_id = @person_user_id
              AND LOWER(COALESCE(project.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
        )
        SELECT
            task_id,
            project_id,
            project_code,
            project_name,
            task_code,
            task_name,
            effective_start_date,
            effective_end_date,
            assigned_hours,
            source_code,
            COUNT(*) OVER ()::bigint AS total_count,
            (SELECT COUNT(DISTINCT project_id)::bigint FROM person_tasks) AS total_project_count
        FROM person_tasks
        ORDER BY project_code, task_code, task_name
        LIMIT 100;
        """;

    private static readonly string ExactProjectSql = ScopeCte + """
        SELECT
            project.project_id,
            project.project_code,
            project.project_name,
            project.status,
            COALESCE(project.project_description, ''),
            project.start_date,
            project.end_date,
            project.created_at,
            project.updated_at,
            pm.display_name,
            ae.display_name,
            sa.display_name
        FROM scoped_projects_all project
        LEFT JOIN app_users pm ON pm.user_id = project.project_manager_user_id AND pm.is_active = TRUE
        LEFT JOIN app_users ae ON ae.user_id = project.account_executive_user_id AND ae.is_active = TRUE
        LEFT JOIN app_users sa ON sa.user_id = project.solution_architect_user_id AND sa.is_active = TRUE
        WHERE regexp_replace(lower(trim(project.project_code)), '[^a-z0-9]+', '', 'g') = @normalized_project
           OR regexp_replace(lower(trim(project.project_name)), '[^a-z0-9]+', '', 'g') = @normalized_project
        ORDER BY
            CASE WHEN regexp_replace(lower(trim(project.project_code)), '[^a-z0-9]+', '', 'g') = @normalized_project THEN 0 ELSE 1 END,
            project.project_code,
            project.project_name;
        """;

    private static readonly string AuthorizedProjectsSql = ScopeCte + """
        SELECT project_id, project_code, project_name
        FROM scoped_projects_all
        ORDER BY project_code, project_name
        LIMIT 500;
        """;

    private const string ProjectHistorySql = """
        SELECT
            audit.created_at,
            audit.process_area,
            audit.event_type,
            audit.prior_state,
            audit.new_state,
            audit.summary,
            audit.reason,
            actor.display_name
        FROM work_lifecycle_audit_events audit
        LEFT JOIN app_users actor ON actor.user_id = audit.actor_user_id
        WHERE audit.project_id = @project_id
        ORDER BY audit.created_at DESC, audit.work_lifecycle_audit_event_id DESC
        LIMIT 100;
        """;

    private const string SourceReadinessSql = """
        WITH required_table(table_name) AS (
            VALUES
                ('app_users'),
                ('projects'),
                ('project_tasks'),
                ('project_assignments'),
                ('reporting_relationships'),
                ('projectpulse_team_scope_assignments'),
                ('engineering_resource_requests'),
                ('engineering_resource_request_assignments'),
                ('work_register_task_assignment_history'),
                ('celar_ai_identity_aliases')
        ),
        required_column(table_name, column_name) AS (
            VALUES
                ('app_users', 'team_name'),
                ('app_users', 'department_name'),
                ('app_users', 'department'),
                ('app_users', 'is_active'),
                ('projects', 'project_code'),
                ('projects', 'project_name'),
                ('projects', 'project_description'),
                ('projects', 'project_manager_user_id'),
                ('projects', 'account_executive_user_id'),
                ('projects', 'solution_architect_user_id'),
                ('projects', 'status'),
                ('projects', 'start_date'),
                ('projects', 'end_date'),
                ('projects', 'created_at'),
                ('projects', 'updated_at'),
                ('project_tasks', 'project_id'),
                ('project_tasks', 'task_code'),
                ('project_tasks', 'task_name'),
                ('project_tasks', 'is_active'),
                ('project_assignments', 'project_id'),
                ('project_assignments', 'task_id'),
                ('project_assignments', 'user_id'),
                ('project_assignments', 'effective_start_date'),
                ('project_assignments', 'effective_end_date'),
                ('project_assignments', 'assigned_hours'),
                ('project_assignments', 'module001a_closeout_status'),
                ('reporting_relationships', 'employee_user_id'),
                ('reporting_relationships', 'manager_user_id'),
                ('reporting_relationships', 'team_lead_user_id'),
                ('reporting_relationships', 'effective_start_date'),
                ('reporting_relationships', 'effective_end_date'),
                ('projectpulse_team_scope_assignments', 'scoped_user_id'),
                ('projectpulse_team_scope_assignments', 'team_name'),
                ('projectpulse_team_scope_assignments', 'department_name'),
                ('projectpulse_team_scope_assignments', 'manager_user_id'),
                ('projectpulse_team_scope_assignments', 'is_active'),
                ('engineering_resource_requests', 'project_id'),
                ('engineering_resource_requests', 'request_status'),
                ('engineering_resource_requests', 'target_start_date'),
                ('engineering_resource_requests', 'target_end_date'),
                ('engineering_resource_request_assignments', 'engineering_resource_request_id'),
                ('engineering_resource_request_assignments', 'user_id'),
                ('engineering_resource_request_assignments', 'assignment_status'),
                ('work_register_task_assignment_history', 'project_id'),
                ('work_register_task_assignment_history', 'task_id_text'),
                ('work_register_task_assignment_history', 'task_name_snapshot'),
                ('work_register_task_assignment_history', 'assigned_user_id'),
                ('work_register_task_assignment_history', 'allocated_hours'),
                ('work_register_task_assignment_history', 'assignment_status'),
                ('work_register_task_assignment_history', 'effective_start_date'),
                ('work_register_task_assignment_history', 'effective_end_date'),
                ('celar_ai_identity_aliases', 'user_id'),
                ('celar_ai_identity_aliases', 'alias_text'),
                ('celar_ai_identity_aliases', 'is_verified'),
                ('celar_ai_identity_aliases', 'is_active')
        ),
        problems AS (
            SELECT 'missing_relation_' || table_name AS problem
            FROM required_table
            WHERE to_regclass('public.' || table_name) IS NULL

            UNION ALL

            SELECT 'missing_column_' || required.table_name || '_' || required.column_name
            FROM required_column required
            WHERE to_regclass('public.' || required.table_name) IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM information_schema.columns available
                  WHERE available.table_schema = 'public'
                    AND available.table_name = required.table_name
                    AND available.column_name = required.column_name
              )
        )
        SELECT COALESCE(array_agg(problem ORDER BY problem), ARRAY[]::text[])
        FROM problems;
        """;

    private readonly PulseAiSystemIntelligenceRepository _repository;
    private readonly ILogger<CelarAiInternalDataService> _logger;

    public CelarAiInternalDataService(
        PulseAiSystemIntelligenceRepository repository,
        ILogger<CelarAiInternalDataService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public static bool IsSupportedQuestion(string? question) =>
        ParseQuestion(question) is not null;

    public static CelarAiInternalDataQuery? ParseQuestion(string? question)
    {
        var value = question?.Trim() ?? string.Empty;
        if (value.Length == 0) return null;

        // A team is a set of authorized people, not an ambiguous person name.
        // Leave group questions to the enterprise evidence planner.
        if (Regex.IsMatch(value, @"\b(?:my team|our team|team members|my direct reports|our engineers)\b", Options)) return null;

        return MatchPerson(value, PersonWorkSummaryPatterns, CelarAiInternalDataQueryKind.PersonWorkSummary, true)
            ?? MatchPerson(value, PersonProjectCountPatterns, CelarAiInternalDataQueryKind.PersonProjectCount, true)
            ?? MatchPerson(value, PersonProjectListPatterns, CelarAiInternalDataQueryKind.PersonProjectList, false)
            ?? MatchPerson(value, PersonTaskCountPatterns, CelarAiInternalDataQueryKind.PersonTaskCount, true)
            ?? MatchPerson(value, PersonTaskListPatterns, CelarAiInternalDataQueryKind.PersonTaskList, false)
            ?? MatchProjectStakeholder(value)
            ?? MatchProject(value, ProjectHistoryPatterns, CelarAiInternalDataQueryKind.ProjectHistory);
    }

    public async Task<PulseAiSystemQuestionResult?> TryAnswerAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        PulseAiSystemQuestionRequest request,
        HttpContext context,
        CancellationToken cancellationToken = default)
    {
        var query = ParseQuestion(request.Question);
        if (query is null) return null;

        var correlationId = CorrelationId(context);
        var detailLevel = DetailLevel(request.DetailLevel);
        var persistence = await BeginPersistenceAsync(
            actualUserId,
            effectiveUserId,
            access,
            request,
            detailLevel,
            correlationId,
            cancellationToken);

        try
        {
            var connectionString = ProjectPulseAiDatabaseConnection.Resolve()
                ?? throw new InvalidOperationException("Celar AI database configuration is unavailable.");
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await ValidateSourceReadinessAsync(connection, cancellationToken);

            AnswerOutcome outcome;
            if (IsPersonQuery(query.Kind))
            {
                var resolution = await ResolvePersonAsync(
                    connection,
                    effectiveUserId,
                    access,
                    query.PersonReference,
                    cancellationToken);
                if (resolution.Outcome != PersonResolutionOutcome.Resolved || resolution.Person is null)
                {
                    outcome = BuildPersonResolutionAnswer(query, resolution);
                }
                else
                {
                    outcome = query.Kind switch
                    {
                        CelarAiInternalDataQueryKind.PersonProjectCount or CelarAiInternalDataQueryKind.PersonProjectList
                            => await BuildProjectAnswerAsync(connection, effectiveUserId, access, query, resolution.Person, cancellationToken),
                        CelarAiInternalDataQueryKind.PersonWorkSummary
                            => await BuildWorkSummaryAnswerAsync(connection, effectiveUserId, access, query, resolution.Person, cancellationToken),
                        _ => await BuildTaskAnswerAsync(connection, effectiveUserId, access, query, resolution.Person, cancellationToken)
                    };
                }
            }
            else
            {
                var projectResolution = await ResolveProjectAsync(
                    connection,
                    effectiveUserId,
                    access,
                    query.ProjectReference,
                    cancellationToken);
                if (projectResolution.Outcome != ProjectResolutionOutcome.Resolved || projectResolution.Project is null)
                {
                    outcome = query.Kind == CelarAiInternalDataQueryKind.ProjectStakeholderLookup
                        && projectResolution.Outcome == ProjectResolutionOutcome.NotFound
                        ? await TryCustomerStakeholdersAsync(connection, effectiveUserId, access, query, cancellationToken)
                            ?? BuildProjectResolutionAnswer(query, projectResolution)
                        : BuildProjectResolutionAnswer(query, projectResolution);
                }
                else
                {
                    outcome = query.Kind == CelarAiInternalDataQueryKind.ProjectHistory
                        ? await BuildProjectHistoryAnswerAsync(connection, query, projectResolution.Project, cancellationToken)
                        : BuildProjectStakeholderAnswer(query, projectResolution.Project);
                }
            }

            return await FinishAsync(
                persistence,
                outcome.Answer,
                outcome.Sources,
                outcome.Status,
                detailLevel,
                correlationId,
                outcome.Warnings,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Celar AI internal-data query failed closed without logging question text or result rows. Diagnostic={Diagnostic}",
                Diagnostic(exception));
            var failure = BuildSourceUnavailableAnswer(query, Diagnostic(exception));
            return await FinishAsync(
                persistence,
                failure.Answer,
                failure.Sources,
                failure.Status,
                detailLevel,
                correlationId,
                failure.Warnings,
                cancellationToken);
        }
    }

    private static bool IsPersonQuery(CelarAiInternalDataQueryKind kind) =>
        kind is CelarAiInternalDataQueryKind.PersonProjectCount
            or CelarAiInternalDataQueryKind.PersonProjectList
            or CelarAiInternalDataQueryKind.PersonTaskCount
            or CelarAiInternalDataQueryKind.PersonTaskList
            or CelarAiInternalDataQueryKind.PersonWorkSummary;

    private static CelarAiInternalDataQuery? MatchPerson(
        string question,
        IReadOnlyList<Regex> patterns,
        CelarAiInternalDataQueryKind kind,
        bool countRequested)
    {
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(question);
            if (!match.Success) continue;
            var person = CleanPersonReference(match.Groups["person"].Value);
            if (person.Length is < 2 or > 255) return null;
            return new CelarAiInternalDataQuery(kind, person, countRequested);
        }
        return null;
    }

    private static CelarAiInternalDataQuery? MatchProjectStakeholder(string question)
    {
        foreach (var pattern in ProjectStakeholderPatterns)
        {
            var match = pattern.Match(question);
            if (!match.Success) continue;
            var project = CleanProjectReference(match.Groups["project"].Value);
            if (project.Length is < 2 or > 255) return null;
            var role = NormalizeProjectRole(match.Groups["role"].Value);
            if (role.Length == 0) return null;
            return new CelarAiInternalDataQuery(
                CelarAiInternalDataQueryKind.ProjectStakeholderLookup,
                string.Empty,
                false,
                project,
                role);
        }
        return null;
    }

    private static CelarAiInternalDataQuery? MatchProject(
        string question,
        IReadOnlyList<Regex> patterns,
        CelarAiInternalDataQueryKind kind)
    {
        foreach (var pattern in patterns)
        {
            var match = pattern.Match(question);
            if (!match.Success) continue;
            var project = CleanProjectReference(match.Groups["project"].Value);
            if (project.Length is < 2 or > 255) return null;
            return new CelarAiInternalDataQuery(kind, string.Empty, false, project);
        }
        return null;
    }

    private static async Task ValidateSourceReadinessAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SourceReadinessSql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var problems = value as string[] ?? [];
        if (problems.Length > 0)
            throw new CelarAiInternalDataSourceException(string.Join("+", problems.Take(8)));
    }

    private static async Task<PersonResolution> ResolvePersonAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        string personReference,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeIdentity(personReference);
        var exact = new List<PersonCandidate>();
        await using (var command = new NpgsqlCommand(ExactPersonSql, connection))
        {
            AddScopeParameters(command, effectiveUserId, access);
            command.Parameters.AddWithValue("normalized_person", normalized);
            command.Parameters.AddWithValue("person_lower", personReference.Trim().ToLowerInvariant());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                exact.Add(new PersonCandidate(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3)));
            }
        }

        if (exact.Count == 1)
            return new PersonResolution(PersonResolutionOutcome.Resolved, exact[0], []);
        if (exact.Count > 1)
            return new PersonResolution(PersonResolutionOutcome.Ambiguous, null, exact.Take(8).Select(candidate => candidate.DisplayName).ToArray());

        var exactNamePart = ExactNamePartReference(personReference);
        if (exactNamePart.Length > 0)
        {
            var partialNameMatches = new List<PersonCandidate>();
            long authorizedMatchCount = 0;
            await using var command = new NpgsqlCommand(ExactNamePartPersonSql, connection);
            AddScopeParameters(command, effectiveUserId, access);
            command.Parameters.AddWithValue("name_part", exactNamePart);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                authorizedMatchCount = reader.GetInt64(3);
                partialNameMatches.Add(new PersonCandidate(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    false));
            }

            if (authorizedMatchCount == 1 && partialNameMatches.Count == 1)
                return new PersonResolution(PersonResolutionOutcome.Resolved, partialNameMatches[0], []);
            if (authorizedMatchCount > 1)
                return new PersonResolution(
                    PersonResolutionOutcome.Ambiguous,
                    null,
                    partialNameMatches
                        .Select(candidate => candidate.DisplayName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(8)
                        .ToArray());
        }

        var suggestions = new List<(string Name, int Distance)>();
        await using (var command = new NpgsqlCommand(AuthorizedPeopleSql, connection))
        {
            AddScopeParameters(command, effectiveUserId, access);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(1);
                var distance = EditDistance(normalized, NormalizeIdentity(name));
                if (distance <= Math.Max(1, normalized.Length / 8))
                    suggestions.Add((name, distance));
            }
        }

        var closest = suggestions
            .OrderBy(value => value.Distance)
            .ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(value => value.Name)
            .ToArray();
        return new PersonResolution(PersonResolutionOutcome.NotFound, null, closest);
    }

    private static async Task<ProjectResolution> ResolveProjectAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        string projectReference,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeIdentity(projectReference);
        var exact = new List<ProjectCandidate>();
        await using (var command = new NpgsqlCommand(ExactProjectSql, connection))
        {
            AddScopeParameters(command, effectiveUserId, access);
            command.Parameters.AddWithValue("normalized_project", normalized);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                exact.Add(new ProjectCandidate(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                    reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
                    reader.GetFieldValue<DateTimeOffset>(7),
                    reader.GetFieldValue<DateTimeOffset>(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11)));
            }
        }

        if (exact.Count == 1)
            return new ProjectResolution(ProjectResolutionOutcome.Resolved, exact[0], []);
        if (exact.Count > 1)
            return new ProjectResolution(
                ProjectResolutionOutcome.Ambiguous,
                null,
                exact.Take(8).Select(project => $"{project.ProjectCode} — {project.ProjectName}").ToArray());

        var suggestions = new List<(string Label, int Distance)>();
        await using (var command = new NpgsqlCommand(AuthorizedProjectsSql, connection))
        {
            AddScopeParameters(command, effectiveUserId, access);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var code = reader.GetString(1);
                var name = reader.GetString(2);
                var distance = Math.Min(
                    EditDistance(normalized, NormalizeIdentity(code)),
                    EditDistance(normalized, NormalizeIdentity(name)));
                if (distance <= Math.Max(1, normalized.Length / 7))
                    suggestions.Add(($"{code} — {name}", distance));
            }
        }

        var closest = suggestions
            .OrderBy(value => value.Distance)
            .ThenBy(value => value.Label, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(value => value.Label)
            .ToArray();
        return new ProjectResolution(ProjectResolutionOutcome.NotFound, null, closest);
    }

    // Customer identity is discoverable only through authorized projects.
    private static readonly string CustomerStakeholdersSql = ScopeCte + """
            SELECT client.client_id, client.client_name, client.client_code,
                   project.project_code, project.project_name,
                   pm.display_name, ae.display_name, sa.display_name
            FROM scoped_projects_all project
            JOIN clients client ON client.client_id = project.client_id
            LEFT JOIN app_users pm ON pm.user_id = project.project_manager_user_id AND pm.is_active
            LEFT JOIN app_users ae ON ae.user_id = project.account_executive_user_id AND ae.is_active
            LEFT JOIN app_users sa ON sa.user_id = project.solution_architect_user_id AND sa.is_active
            WHERE client.is_active
              AND (regexp_replace(lower(trim(client.client_code)), '[^a-z0-9]+', '', 'g') = @customer
                OR regexp_replace(lower(trim(client.client_name)), '[^a-z0-9]+', '', 'g') = @customer)
            ORDER BY client.client_id, project.project_code, project.project_id
            LIMIT 501;
            """;
    private static async Task<AnswerOutcome?> TryCustomerStakeholdersAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        CelarAiInternalDataQuery query,
        CancellationToken cancellationToken)
    {
        var rows = new List<(Guid Id, string Name, string Code, string Project, string? Owner)>();
        await using var command = new NpgsqlCommand(CustomerStakeholdersSql, connection);
        command.CommandTimeout = 15;
        AddScopeParameters(command, effectiveUserId, access);
        command.Parameters.AddWithValue("customer", NormalizeIdentity(query.ProjectReference));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ownerColumn = query.RequestedProjectRole switch
        {
            "project_manager" => 5,
            "solution_architect" => 7,
            _ => 6
        };
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2),
                $"{reader.GetString(3)} — {reader.GetString(4)}",
                reader.IsDBNull(ownerColumn) ? null : reader.GetString(ownerColumn)));
        if (rows.Count == 0) return null;

        var label = query.RequestedProjectRole switch
        {
            "project_manager" => "Project Manager",
            "solution_architect" => "Solution Architect",
            _ => "Account Executive / Sales Rep"
        };
        var ambiguous = rows.Select(row => row.Id).Distinct().Count() != 1;
        var truncated = rows.Count > 500;
        var now = DateTimeOffset.UtcNow;
        var seed = BuildProjectResolutionAnswer(query,
            new ProjectResolution(ProjectResolutionOutcome.NotFound, null, []));
        var answer = seed.Answer with
        {
            DirectConclusion = ambiguous
                ? "More than one authorized customer matches that name or code. Specify the customer code."
                : truncated
                    ? "This customer has more than 500 accessible project records. Narrow the project scope to verify its recorded stakeholders."
                    : $"Recorded {label} assignments for {rows[0].Name} ({rows[0].Code}) are listed by authorized project below.",
            ExecutiveSummary = "Customer identity comes from the Customer Directory. These are project-level role assignments, not an inferred customer-wide owner.",
            ScopeAndFilters = ["Exact stored customer code or name; effective-user project permissions enforced.", "Current role fields across accessible project records, including historical projects."],
            CurrentState = ambiguous || truncated ? [] : rows.Select(row => $"{row.Project}: {row.Owner ?? "No active owner recorded"}.").ToArray(),
            SourceEvidence = ["Customer Directory joined to authorized projects and active stakeholder identities at request time."],
            KnownUnknownAndStaleValues = ["Customer-wide ownership is not inferred from project assignments. Records outside your scope are excluded."],
            RecommendedActions = ambiguous || truncated ? ["Specify an exact customer code and project scope."] : [],
            Limitations = ["This lookup reports recorded project ownership within your access scope."],
            CitationIds = ambiguous || truncated ? [] : [1],
            Confidence = ambiguous || truncated ? 0.2m : 0.99m,
            ConfidenceExplanation = "Direct parameterized retrieval from authorized current database records; no model-inferred owner."
        };
        return new AnswerOutcome(ambiguous || truncated ? "partial" : "completed", answer,
            ambiguous || truncated ? [] : [Source(1, "authorized_customer_project_stakeholders", "Customer and authorized project ownership", "021/019", "internal:celar-ai/customer-stakeholders", now, "Stored customer identity and permission-scoped project role relationships")], []);
    }

    private static async Task<AnswerOutcome> BuildProjectAnswerAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        CelarAiInternalDataQuery query,
        PersonCandidate person,
        CancellationToken cancellationToken)
    {
        var (rows, total) = await LoadPersonProjectsAsync(connection, effectiveUserId, access, person.UserId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var source = new[]
        {
            Source(1, "authorized_person_directory", "Authorized Pulse person identity", "062", "internal:celar-ai/identity-resolution", now, "Exact active identity or verified alias within the effective user's authorized scope"),
            Source(2, "authorized_person_projects", "Authorized project, role, and assignment records", "019/053I/055C", "internal:celar-ai/person-projects", now, "Distinct current projects after project role, team, assignment-date, closeout, and status scope")
        };
        var plural = total == 1 ? "project" : "projects";
        var details = rows.Select(ProjectRowDetail).ToArray();
        var conclusion = $"{person.DisplayName} has {total} active {plural} within your authorized Pulse scope.";
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: conclusion,
            ExecutiveSummary: total == 0
                ? "The person was resolved to one active authorized Pulse identity, but no current Project Manager, Account Executive, Solution Architect, or active resource relationship remained after the governed filters were applied."
                : "The result is a deterministic distinct-project count. It combines recorded project roles and current resource assignments without double-counting a project.",
            ScopeAndFilters:
            [
                $"Person: {person.DisplayName}; identity resolution: {(person.MatchedVerifiedAlias ? "verified alias" : "exact active Pulse identity")}.",
                "Project scope: current effective user's authorized project, PM, AE, SA, team, or assignment scope.",
                "Project statuses excluded: closed, completed, cancelled/canceled, and archived.",
                "Assignment filters: effective start date reached, effective end date not passed, and Module 001A closeout status active.",
                "Count rule: distinct project IDs across Project Manager, Account Executive, Solution Architect, and active resource relationships."
            ],
            CurrentState:
            [
                $"Distinct active project count: {total}.",
                $"Project detail rows returned: {rows.Count}{(total > rows.Count ? $" of {total}" : string.Empty)}.",
                "External providers called: none."
            ],
            DetailedAnalysis: details,
            ApiFindings: [],
            TroubleshootingFindings: [],
            RootCauseHypotheses: [],
            DiagnosticSteps: [],
            SourceEvidence:
            [
                "Source 1: active app_users identity plus an active verified Celar AI identity alias when applicable.",
                "Source 2: projects role ownership plus current project/task/resource assignments inside the requester's server-authorized scope."
            ],
            KnownUnknownAndStaleValues:
            [
                "Known: the distinct current project count and recorded project relationships at the data-as-of timestamp.",
                "Excluded: projects outside the requester's authorization scope, inactive people, ended/closed assignments, inactive tasks, and closed/completed/cancelled/archived projects.",
                "A zero means no qualifying current project relationship was found after these filters; it does not mean the person has never worked on a project."
            ],
            Assumptions: [],
            Conflicts: [],
            Limitations:
            [
                "This answer reflects recorded Pulse roles and assignments, not informal work or activity outside Pulse.",
                "At most 100 project detail rows are displayed, while the numeric count remains the complete distinct count."
            ],
            RisksAndImplications: [],
            RecommendedActions: total > 0
                ? ["Open Module 019 or Module 055C to review the cited project and assignment records."]
                : ["Confirm the person's identity and review Module 019/055C if a project relationship was expected."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#project-workspace", "#work-register"],
            CitationIds: [1, 2],
            Confidence: 0.98m,
            ConfidenceExplanation: "High confidence because an exact authorized identity was resolved and the value is a deterministic distinct count from current authoritative project, role, and assignment records.",
            DataAsOf: now);
        return new AnswerOutcome("completed", answer, source, []);
    }

    private static async Task<AnswerOutcome> BuildTaskAnswerAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        CelarAiInternalDataQuery query,
        PersonCandidate person,
        CancellationToken cancellationToken)
    {
        var (rows, total, totalProjects) = await LoadPersonTasksAsync(connection, effectiveUserId, access, person.UserId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var source = new[]
        {
            Source(1, "authorized_person_directory", "Authorized Pulse person identity", "062", "internal:celar-ai/identity-resolution", now, "Exact active identity or verified alias within the effective user's authorized scope"),
            Source(2, "authorized_person_tasks", "Authorized task assignment records", "001/019/055C", "internal:celar-ai/person-tasks", now, "Current active project tasks and assignments after effective-date, closeout, project-status, and record-scope filters")
        };
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: $"{person.DisplayName} has {total} active task assignment{(total == 1 ? string.Empty : "s")} across {totalProjects} visible active project{(totalProjects == 1 ? string.Empty : "s")} within your authorized Pulse scope.",
            ExecutiveSummary: "The result is calculated from distinct active task assignments after effective-date, project-status, assignment-closeout, task-active, and requester-scope filters.",
            ScopeAndFilters:
            [
                $"Person: {person.DisplayName}; identity resolution: {(person.MatchedVerifiedAlias ? "verified alias" : "exact active Pulse identity")}.",
                "Only active tasks on non-closed projects and current non-closed assignments are included.",
                "Projects and people outside the current effective user's scope are excluded."
            ],
            CurrentState: [$"Active task assignments: {total}.", $"Distinct visible active projects containing active task assignments: {totalProjects}.", $"Task detail rows returned: {rows.Count}{(total > rows.Count ? $" of {total}" : string.Empty)}.", "External providers called: none."],
            DetailedAnalysis: rows.Select(TaskRowDetail).ToArray(),
            ApiFindings: [], TroubleshootingFindings: [], RootCauseHypotheses: [], DiagnosticSteps: [],
            SourceEvidence: ["Source 1: active app_users identity plus an active verified identity alias when applicable.", "Source 2: project_tasks joined to the deduplicated current assignment authority, with active Work Register roster rows taking precedence over mirrored project_assignments rows."],
            KnownUnknownAndStaleValues: ["A zero means no qualifying current task assignment was recorded after all filters; historical or out-of-scope work is not represented."],
            Assumptions: [], Conflicts: [],
            Limitations: ["This answer reflects recorded assignments, not proof that work occurred.", "At most 100 task detail rows are displayed, while the numeric count remains complete."],
            RisksAndImplications: [],
            RecommendedActions: ["Open Module 001, Module 019, or Module 055C to review the cited task assignments."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#timesheet", "#project-workspace", "#work-register"],
            CitationIds: [1, 2],
            Confidence: 0.98m,
            ConfidenceExplanation: "High confidence because an exact authorized identity was resolved and the value is deterministically counted from current authoritative task-assignment records.",
            DataAsOf: now);
        return new AnswerOutcome("completed", answer, source, []);
    }

    private static async Task<AnswerOutcome> BuildWorkSummaryAnswerAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        CelarAiInternalDataQuery query,
        PersonCandidate person,
        CancellationToken cancellationToken)
    {
        var (projects, projectTotal) = await LoadPersonProjectsAsync(connection, effectiveUserId, access, person.UserId, cancellationToken);
        var (tasks, taskTotal, taskProjectTotal) = await LoadPersonTasksAsync(connection, effectiveUserId, access, person.UserId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var sources = new[]
        {
            Source(1, "authorized_person_directory", "Authorized Pulse person identity", "062", "internal:celar-ai/identity-resolution", now, "Exact active identity or verified alias within the effective user's authorized scope"),
            Source(2, "authorized_person_projects", "Authorized project, role, and assignment records", "019/053I/055C", "internal:celar-ai/person-projects", now, "Distinct current project relationships inside the effective user's scope"),
            Source(3, "authorized_person_tasks", "Authorized task assignment records", "001/019/055C", "internal:celar-ai/person-tasks", now, "Current deduplicated active task assignments inside the effective user's scope")
        };
        var details = projects.Select(ProjectRowDetail)
            .Concat(tasks.Select(TaskRowDetail))
            .Take(200)
            .ToArray();
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: $"{person.DisplayName} has {projectTotal} active project{(projectTotal == 1 ? string.Empty : "s")} and {taskTotal} active task assignment{(taskTotal == 1 ? string.Empty : "s")} within your authorized Pulse scope.",
            ExecutiveSummary: "This answers the combined project-and-task question in one deterministic database pass family instead of routing the internal fact to a model or a generic supporting service.",
            ScopeAndFilters:
            [
                $"Person: {person.DisplayName}; identity resolution: {(person.MatchedVerifiedAlias ? "verified alias" : "exact active Pulse identity")}.",
                "Active project count includes recorded PM, Account Executive, Solution Architect, and current resource relationships.",
                "Active task count includes current effective task assignments on non-closed projects after Work Register precedence and Module 001A closeout filters.",
                "All rows are permission-scoped to the current effective user."
            ],
            CurrentState:
            [
                $"Distinct active projects: {projectTotal}.",
                $"Active task assignments: {taskTotal}.",
                $"Distinct active projects containing those task assignments: {taskProjectTotal}.",
                $"Project detail rows returned: {projects.Count}; task detail rows returned: {tasks.Count}.",
                "External providers called: none."
            ],
            DetailedAnalysis: details,
            ApiFindings: [],
            TroubleshootingFindings: [],
            RootCauseHypotheses: [],
            DiagnosticSteps: [],
            SourceEvidence:
            [
                "Source 1: authorized active Pulse identity / verified alias.",
                "Source 2: current projects and project-role/resource relationships.",
                "Source 3: deduplicated current task-assignment authority."
            ],
            KnownUnknownAndStaleValues: ["Historical work and records outside the current effective user's authorization scope are intentionally excluded from the active counts."],
            Assumptions: [],
            Conflicts: [],
            Limitations: ["Counts represent recorded Pulse relationships at the data-as-of timestamp; they do not infer informal work."],
            RisksAndImplications: [],
            RecommendedActions: ["Open Module 019/055C for project detail or Module 001 for the current task assignments."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#project-workspace", "#work-register", "#timesheet"],
            CitationIds: [1, 2, 3],
            Confidence: 0.99m,
            ConfidenceExplanation: "Very high confidence because the person resolved exactly and both requested counts were computed deterministically from current permission-scoped Pulse records.",
            DataAsOf: now);
        return new AnswerOutcome("completed", answer, sources, []);
    }

    private static AnswerOutcome BuildProjectStakeholderAnswer(
        CelarAiInternalDataQuery query,
        ProjectCandidate project)
    {
        var now = DateTimeOffset.UtcNow;
        var (label, value, module) = query.RequestedProjectRole switch
        {
            "solution_architect" => ("Solution Architect", project.SolutionArchitectName, "019/053I/073"),
            "project_manager" => ("Project Manager", project.ProjectManagerName, "019"),
            _ => ("Account Executive / Sales owner", project.AccountExecutiveName, "019/053I/073")
        };
        var assigned = !string.IsNullOrWhiteSpace(value);
        var direct = assigned
            ? $"The recorded {label} for {project.ProjectCode} — {project.ProjectName} is {value}."
            : $"No active {label} is currently recorded on {project.ProjectCode} — {project.ProjectName}.";
        var sources = new[]
        {
            Source(1, "authorized_project_record", "Authorized project record", "019/055C", "internal:celar-ai/project", now, "Exact project code or name inside the effective user's authorized project scope"),
            Source(2, "authorized_project_stakeholder", $"Recorded {label}", module, "internal:celar-ai/project-stakeholders", now, "Current projects role foreign key joined to active app_users identity")
        };
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: direct,
            ExecutiveSummary: "The answer comes directly from the current project record and the referenced active Pulse user. No provider inferred the stakeholder.",
            ScopeAndFilters:
            [
                $"Project: {project.ProjectCode} — {project.ProjectName}; status {project.Status}.",
                "Project resolution is exact by normalized project code or project name and is restricted to the effective user's authorized project scope.",
                query.RequestedProjectRole == "account_executive"
                    ? "Natural-language terms such as sales person, sales rep, AE, and Account Executive resolve to the project's Account Executive / Sales owner field."
                    : $"Requested role: {label}."
            ],
            CurrentState: [assigned ? $"{label}: {value}." : $"{label}: not currently assigned.", "External providers called: none."],
            DetailedAnalysis: [],
            ApiFindings: [], TroubleshootingFindings: [], RootCauseHypotheses: [], DiagnosticSteps: [],
            SourceEvidence: ["Source 1: current authorized projects row.", $"Source 2: current {label} reference joined to active app_users."],
            KnownUnknownAndStaleValues: assigned ? [] : [$"The {label} field is currently empty or does not reference an active user; no person was guessed."],
            Assumptions: [], Conflicts: [],
            Limitations: ["This returns the stakeholder recorded on the current Pulse project record; it does not infer unofficial coverage outside Pulse."],
            RisksAndImplications: [],
            RecommendedActions: assigned ? ["Open the Project Workspace or Work Register to verify the project team record."] : ["Update the authoritative project record in the owning workflow if this role should be assigned."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#project-workspace", "#work-register"],
            CitationIds: [1, 2],
            Confidence: 0.99m,
            ConfidenceExplanation: assigned
                ? "Very high confidence because the project resolved exactly and the current stakeholder is a direct foreign-key relationship to one active Pulse user."
                : "Very high confidence that no active stakeholder is currently recorded in the authoritative field; no substitute identity was inferred.",
            DataAsOf: now);
        return new AnswerOutcome("completed", answer, sources, []);
    }

    private static async Task<AnswerOutcome> BuildProjectHistoryAnswerAsync(
        NpgsqlConnection connection,
        CelarAiInternalDataQuery query,
        ProjectCandidate project,
        CancellationToken cancellationToken)
    {
        var events = new List<ProjectHistoryRow>();
        var auditAvailable = false;
        var auditDiagnostic = string.Empty;
        try
        {
            await using (var exists = new NpgsqlCommand("SELECT to_regclass('public.work_lifecycle_audit_events') IS NOT NULL;", connection))
            {
                auditAvailable = Convert.ToBoolean(await exists.ExecuteScalarAsync(cancellationToken));
            }
            if (auditAvailable)
            {
                await using var command = new NpgsqlCommand(ProjectHistorySql, connection);
                command.Parameters.AddWithValue("project_id", project.ProjectId);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    events.Add(new ProjectHistoryRow(
                        reader.GetFieldValue<DateTimeOffset>(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetString(5),
                        reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7)));
                }
            }
        }
        catch (PostgresException postgres) when (postgres.SqlState is "42P01" or "42501")
        {
            auditAvailable = false;
            auditDiagnostic = postgres.SqlState == "42501" ? "audit_read_not_authorized" : "audit_relation_unavailable";
        }

        var now = DateTimeOffset.UtcNow;
        var sources = new List<PulseAiSystemSourceEvidence>
        {
            Source(1, "authorized_project_record", "Authorized project record", "019/055C", "internal:celar-ai/project", now, "Exact project inside the effective user's authorized project scope")
        };
        if (auditAvailable)
            sources.Add(Source(2, "work_lifecycle_audit_history", "Immutable work lifecycle audit history", "038/039/040/042/055C/055D", "internal:celar-ai/project-history", now, "Immutable project lifecycle events ordered by recorded event time"));

        var timeline = new List<string>
        {
            $"Project created: {project.CreatedAt:O}.",
            $"Project last updated: {project.UpdatedAt:O}.",
            $"Recorded schedule: {(project.StartDate?.ToString("yyyy-MM-dd") ?? "start date not recorded")} through {(project.EndDate?.ToString("yyyy-MM-dd") ?? "open/no end date recorded")}.",
            $"Current status: {project.Status}."
        };
        if (!string.IsNullOrWhiteSpace(project.Description))
            timeline.Add($"Recorded project description: {project.Description}");
        timeline.AddRange(events.Select(row =>
            $"{row.OccurredAt:O} — {row.ProcessArea}/{row.EventType}: {row.Summary}{(string.IsNullOrWhiteSpace(row.ActorName) ? string.Empty : $"; actor {row.ActorName}")}{(string.IsNullOrWhiteSpace(row.Reason) ? string.Empty : $"; reason {row.Reason}")}{(string.IsNullOrWhiteSpace(row.PriorState) && string.IsNullOrWhiteSpace(row.NewState) ? string.Empty : $"; state {row.PriorState} → {row.NewState}")}."));

        var status = auditAvailable ? "completed" : "partial";
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: auditAvailable
                ? $"{project.ProjectCode} — {project.ProjectName} is currently {project.Status}. Celar AI found {events.Count} immutable work-lifecycle event{(events.Count == 1 ? string.Empty : "s")} plus the authoritative project timestamps for historical context."
                : $"{project.ProjectCode} — {project.ProjectName} is currently {project.Status}. Core project timestamps are available, but immutable work-lifecycle audit evidence could not be read, so the historical context is incomplete.",
            ExecutiveSummary: auditAvailable
                ? "The history combines the current permission-scoped project record with immutable work-to-cash lifecycle audit evidence. It does not reconstruct or invent events that were never recorded."
                : "The request remains evidence-limited rather than substituting a generic project story. Only current project metadata and recorded timestamps are returned.",
            ScopeAndFilters:
            [
                $"Project: {project.ProjectCode} — {project.ProjectName}.",
                "Project access is restricted to the current effective user's authorized project scope.",
                "History source: immutable work_lifecycle_audit_events when present and readable.",
                "At most the 100 most recent lifecycle audit events are returned."
            ],
            CurrentState:
            [
                $"Current status: {project.Status}.",
                $"Project created: {project.CreatedAt:O}.",
                $"Project updated: {project.UpdatedAt:O}.",
                $"Lifecycle audit events returned: {events.Count}.",
                $"Lifecycle audit evidence available: {auditAvailable}.",
                "External providers called: none."
            ],
            DetailedAnalysis: timeline,
            ApiFindings: [], TroubleshootingFindings: auditAvailable ? [] : ["The immutable lifecycle audit adapter was unavailable or unreadable for this request."],
            RootCauseHypotheses: [], DiagnosticSteps: [],
            SourceEvidence: auditAvailable
                ? ["Source 1: current authorized projects row.", "Source 2: immutable work_lifecycle_audit_events rows for the resolved project."]
                : ["Source 1: current authorized projects row. No audit source was treated as successful."],
            KnownUnknownAndStaleValues: auditAvailable
                ? ["This source covers work-lifecycle events captured by the unified lifecycle audit. Other module-specific audit families may contain additional context and are not silently merged here."]
                : [$"Full change history remains unknown because the lifecycle audit source was unavailable{(auditDiagnostic.Length == 0 ? string.Empty : $" ({auditDiagnostic})")}."],
            Assumptions: [], Conflicts: [],
            Limitations:
            [
                "Historical context is limited to facts recorded by the authoritative project record and the available immutable lifecycle audit source.",
                "Celar AI does not infer missing events from conversation history or external model knowledge."
            ],
            RisksAndImplications: auditAvailable ? [] : ["Do not treat this partial history as a complete audit trail."],
            RecommendedActions: auditAvailable ? ["Open Audit History or the project workspace for module-specific detail when needed."] : ["Check Module 038/997 audit readiness and permissions, then retry the history question."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#project-workspace", "#audit-history", "#work-register"],
            CitationIds: auditAvailable ? [1, 2] : [1],
            Confidence: auditAvailable ? 0.95m : 0.55m,
            ConfidenceExplanation: auditAvailable
                ? "High confidence for the returned project and lifecycle events because both are direct permission-scoped database evidence."
                : "Moderate confidence in the current project metadata, but the requested historical context is incomplete without the immutable audit source.",
            DataAsOf: now);
        return new AnswerOutcome(status, answer, sources, auditAvailable ? [] : ["Project history is partial because immutable lifecycle audit evidence was unavailable."]);
    }

    private static async Task<(List<PersonProjectRow> Rows, long Total)> LoadPersonProjectsAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        Guid personUserId,
        CancellationToken cancellationToken)
    {
        var rows = new List<PersonProjectRow>();
        long total = 0;
        await using var command = new NpgsqlCommand(PersonProjectsSql, connection);
        AddScopeParameters(command, effectiveUserId, access);
        command.Parameters.AddWithValue("person_user_id", personUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(9);
            rows.Add(new PersonProjectRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetBoolean(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7),
                reader.GetInt64(8)));
        }
        return (rows, total);
    }

    private static async Task<(List<PersonTaskRow> Rows, long Total, long ProjectTotal)> LoadPersonTasksAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        Guid personUserId,
        CancellationToken cancellationToken)
    {
        var rows = new List<PersonTaskRow>();
        long total = 0;
        long totalProjects = 0;
        await using var command = new NpgsqlCommand(PersonTasksSql, connection);
        AddScopeParameters(command, effectiveUserId, access);
        command.Parameters.AddWithValue("person_user_id", personUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            total = reader.GetInt64(10);
            totalProjects = reader.GetInt64(11);
            rows.Add(new PersonTaskRow(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateOnly>(6),
                reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7),
                reader.GetDecimal(8),
                reader.GetString(9)));
        }
        return (rows, total, totalProjects);
    }

    private static string ProjectRowDetail(PersonProjectRow row)
    {
        var relationships = new List<string>();
        if (row.IsProjectManager) relationships.Add("Project Manager");
        if (row.IsAccountExecutive) relationships.Add("Account Executive / Sales owner");
        if (row.IsSolutionArchitect) relationships.Add("Solution Architect");
        if (row.IsAssignedResource) relationships.Add($"assigned resource ({row.ActiveTaskAssignmentCount} active task assignment{(row.ActiveTaskAssignmentCount == 1 ? string.Empty : "s")})");
        return $"{row.ProjectCode} — {row.ProjectName}; status {row.Status}; relationship: {(relationships.Count == 0 ? "recorded project relationship" : string.Join(" and ", relationships))}.";
    }

    private static string TaskRowDetail(PersonTaskRow row) =>
        $"{row.ProjectCode} — {row.ProjectName}; {row.TaskCode} — {row.TaskName}; assigned hours {row.AssignedHours:0.##}; effective {row.EffectiveStartDate:yyyy-MM-dd} through {(row.EffectiveEndDate?.ToString("yyyy-MM-dd") ?? "open")}; authority {row.SourceCode}.";

    private static AnswerOutcome BuildPersonResolutionAnswer(
        CelarAiInternalDataQuery query,
        PersonResolution resolution)
    {
        var now = DateTimeOffset.UtcNow;
        var countQuestion = query.Kind is CelarAiInternalDataQueryKind.PersonProjectCount
            or CelarAiInternalDataQueryKind.PersonTaskCount
            or CelarAiInternalDataQueryKind.PersonWorkSummary;
        var subject = countQuestion ? "the requested count" : "the requested list";
        var (status, direct, confidence, sourceStatus, sourceCode, warning) = resolution.Outcome switch
        {
            PersonResolutionOutcome.Ambiguous => (
                "partial",
                $"Celar AI found more than one authorized active person matching “{query.PersonReference}.” Use the person's exact email address or verified full name before {subject} can be calculated.",
                0.25m,
                409,
                "ambiguous_person_identity",
                "No project, task, or assignment query was executed because identity resolution was ambiguous."),
            _ => (
                "partial",
                $"Celar AI could not resolve “{query.PersonReference}” to one active identity within your authorized Pulse scope. Use the exact full name, email address, or a verified identity alias.",
                0.25m,
                404,
                "person_not_found",
                "No project, task, or assignment query was executed without an exact authorized identity.")
        };
        var suggestions = resolution.Suggestions.Count > 0
            ? resolution.Suggestions.Select(value => $"Authorized near match: {value}.").ToArray()
            : [];
        var source = new[]
        {
            new PulseAiSystemSourceEvidence(1, "governed_identity_resolution", sourceCode, "Authorized Pulse identity resolution", "062", "INTERNAL", "internal:celar-ai/identity-resolution", sourceStatus is >= 200 and < 300 ? "succeeded" : "not_resolved", sourceStatus, now, "current_request", "Active identity and verified-alias resolution inside the effective user's scope")
        };
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: direct,
            ExecutiveSummary: warning,
            ScopeAndFilters: ["Identity resolution is permission-scoped and fails closed before related records are queried."],
            CurrentState: suggestions,
            DetailedAnalysis: [], ApiFindings: [], TroubleshootingFindings: [], RootCauseHypotheses: [], DiagnosticSteps: [],
            SourceEvidence: ["Source 1: current active identity and verified-alias resolver; related record retrieval was not authorized until one identity resolved."],
            KnownUnknownAndStaleValues: ["The requested value remains unknown. No zero was inferred from a missing, ambiguous, or unauthorized identity."],
            Assumptions: [], Conflicts: [],
            Limitations: ["Celar AI never guesses a person from a near spelling match."],
            RisksAndImplications: ["Treating a missing or unauthorized identity as zero would create an incorrect workload conclusion."],
            RecommendedActions: ["Retry with the exact full name or email shown in the authorized Pulse directory, or ask an authorized manager/administrator if the person is outside your scope."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#project-workspace", "#user-administration"],
            CitationIds: [1], Confidence: confidence,
            ConfidenceExplanation: "The requested internal fact is not answered because one authorized identity was not resolved.",
            DataAsOf: now);
        return new AnswerOutcome(status, answer, source, [warning]);
    }

    private static AnswerOutcome BuildProjectResolutionAnswer(
        CelarAiInternalDataQuery query,
        ProjectResolution resolution)
    {
        var now = DateTimeOffset.UtcNow;
        var subject = query.Kind == CelarAiInternalDataQueryKind.ProjectHistory ? "project history" : "project stakeholder";
        var ambiguous = resolution.Outcome == ProjectResolutionOutcome.Ambiguous;
        var direct = ambiguous
            ? $"Celar AI found more than one authorized project matching “{query.ProjectReference}.” Use the exact project code before the {subject} can be returned."
            : $"Celar AI could not resolve “{query.ProjectReference}” to one project within your authorized Pulse scope. Use the exact project code or project name.";
        var suggestions = resolution.Suggestions.Count > 0
            ? resolution.Suggestions.Select(value => $"Authorized near match: {value}.").ToArray()
            : [];
        var source = new[]
        {
            new PulseAiSystemSourceEvidence(1, "governed_project_resolution", ambiguous ? "ambiguous_project_identity" : "project_not_found", "Authorized Pulse project resolution", "019/055C", "INTERNAL", "internal:celar-ai/project-resolution", "not_resolved", ambiguous ? 409 : 404, now, "current_request", "Exact project-code/project-name resolution inside the effective user's scope")
        };
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: direct,
            ExecutiveSummary: "No project-level role or history query was executed until one authorized project resolved exactly.",
            ScopeAndFilters: ["Project resolution is permission-scoped and fails closed before project facts are returned."],
            CurrentState: suggestions,
            DetailedAnalysis: [], ApiFindings: [], TroubleshootingFindings: [], RootCauseHypotheses: [], DiagnosticSteps: [],
            SourceEvidence: ["Source 1: current authorized project resolver."],
            KnownUnknownAndStaleValues: [$"The requested {subject} remains unknown; Celar AI did not guess a project."],
            Assumptions: [], Conflicts: [], Limitations: ["Near matches are suggestions only and are never treated as the requested project."],
            RisksAndImplications: [],
            RecommendedActions: ["Retry using the exact project code shown in the Project Workspace or Work Register."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#project-workspace", "#work-register"],
            CitationIds: [1], Confidence: 0.25m,
            ConfidenceExplanation: "The internal fact is withheld because exactly one authorized project did not resolve.",
            DataAsOf: now);
        return new AnswerOutcome("partial", answer, source, ["No project fact was inferred from an ambiguous, missing, or unauthorized project reference."]);
    }

    private static AnswerOutcome BuildSourceUnavailableAnswer(
        CelarAiInternalDataQuery query,
        string diagnostic)
    {
        var now = DateTimeOffset.UtcNow;
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: $"Celar AI could not reach the authoritative internal source required to answer this question ({diagnostic}).",
            ExecutiveSummary: "The request failed closed. It was not sent to Claude or OpenAI, and no value was inferred from conversation history or a generic template.",
            ScopeAndFilters: ["Internal question; permission-scoped Pulse source required."],
            CurrentState: ["Authoritative source status: unavailable.", $"Diagnostic: {diagnostic}.", "External providers called: none."],
            DetailedAnalysis: [], ApiFindings: [],
            TroubleshootingFindings: ["Use the displayed correlation ID to check Celar AI database and schema readiness."],
            RootCauseHypotheses: [], DiagnosticSteps: [], SourceEvidence: [],
            KnownUnknownAndStaleValues: ["The requested value remains unknown; unavailable data was not converted to zero."],
            Assumptions: [], Conflicts: [],
            Limitations: ["An internal factual answer requires a successful authoritative source."],
            RisksAndImplications: ["Do not make staffing or project decisions from this incomplete result."],
            RecommendedActions: ["Retry after checking Module 011/System Intelligence readiness and the migration ledger."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#celar-ai", "#service-control", "#system-diagnostics"],
            CitationIds: [], Confidence: 0m,
            ConfidenceExplanation: $"No authoritative result was available ({diagnostic}).",
            DataAsOf: now);
        return new AnswerOutcome("partial", answer, [], ["The internal-data source was unavailable; external fallback was prohibited for this Pulse fact."]);
    }

    private async Task<PersistenceContext> BeginPersistenceAsync(
        Guid actualUserId,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        PulseAiSystemQuestionRequest request,
        string detailLevel,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var mayPersist = actualUserId == effectiveUserId && access.CanViewConversations;
        var conversation = mayPersist
            ? await _repository.EnsureConversationAsync(request.ConversationId, actualUserId, effectiveUserId, request.Mode ?? "system_help", cancellationToken)
            : null;
        var conversationId = conversation?.ConversationId ?? request.ConversationId ?? Guid.NewGuid();
        var userMessageId = Guid.NewGuid();
        if (conversation is not null)
        {
            var saved = await _repository.AppendMessageAsync(
                conversationId, effectiveUserId, "user", "completed", request.Question ?? string.Empty,
                new { contractVersion = ContractVersion, enterpriseFactsVersion = EnterpriseFactsVersion, intentCode = IntentCode, previousConversationMessagesInjected = false, externalProviderEligible = false },
                null, null, correlationId, string.Empty, string.Empty, [], new { }, DateTimeOffset.UtcNow, cancellationToken);
            if (saved.MessageId != Guid.Empty) userMessageId = saved.MessageId;
        }
        var runId = conversation is not null
            ? await _repository.CreateInquiryRunAsync(conversationId, userMessageId, actualUserId, effectiveUserId, IntentCode, detailLevel, Sha256(request.Question ?? string.Empty), correlationId, cancellationToken)
            : Guid.NewGuid();
        return new PersistenceContext(conversationId, userMessageId, runId, effectiveUserId, conversation is not null);
    }

    private async Task<PulseAiSystemQuestionResult> FinishAsync(
        PersistenceContext persistence,
        PulseAiSystemDetailedAnswer answer,
        IReadOnlyList<PulseAiSystemSourceEvidence> sources,
        string status,
        string detailLevel,
        string correlationId,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var assistantId = Guid.NewGuid();
        const string provider = "celar_ai_governed_internal_data";
        const string model = "Celar AI deterministic internal-data resolver v1";
        if (persistence.Persisted)
        {
            var saved = await _repository.AppendMessageAsync(
                persistence.ConversationId, persistence.EffectiveUserId, "assistant", status, answer.DirectConclusion,
                new { status, intentCode = IntentCode, detailLevel, answer, sources, modelProvider = provider, modelName = model, correlationId, warnings, externalProviderCalled = false },
                persistence.InquiryRunId, null, correlationId, provider, model, ["celar_ai_internal_data"],
                new { totalSources = sources.Count, successfulSources = sources.Count(source => source.StatusCode is >= 200 and < 300), externalProviderCalled = false },
                answer.DataAsOf, cancellationToken);
            if (saved.MessageId != Guid.Empty) assistantId = saved.MessageId;
            await _repository.CompleteInquiryRunAsync(persistence.InquiryRunId, assistantId, status, [], [], 0, answer.Confidence, status == "completed" ? string.Empty : "internal_data_not_resolved", cancellationToken);
        }

        return new PulseAiSystemQuestionResult(
            persistence.ConversationId, persistence.UserMessageId, assistantId, persistence.InquiryRunId,
            status, IntentCode, detailLevel, answer, sources, [], [], provider, model, correlationId,
            warnings, persistence.Persisted, [], [], [], string.Empty, []);
    }

    private static void AddScopeParameters(
        NpgsqlCommand command,
        Guid effectiveUserId,
        PulseAiSystemAccess access)
    {
        var broad = access.IsSuperAdministrator || HasRole(access, "PROJECT_TEAM_COORDINATOR", "EXECUTIVE");
        var managed = broad || HasRole(access,
            "PROJECT_MANAGEMENT", "PROJECT_MANAGER", "PROJECT_MANAGEMENT_LEAD",
            "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD");
        var team = broad || HasRole(access,
            "MANAGER", "ENGINEERING_MANAGER", "ENGINEERING_LEAD", "ENGINEERING_TEAM_LEAD",
            "PROJECT_MANAGEMENT_LEAD", "PROJECT_MANAGEMENT_TEAM_LEAD", "PM_TEAM_LEAD");
        command.Parameters.AddWithValue("effective_user_id", effectiveUserId);
        command.Parameters.AddWithValue("is_broad_scope", broad);
        command.Parameters.AddWithValue("can_view_managed_projects", managed);
        command.Parameters.AddWithValue("can_view_team_scope", team);
    }

    private static bool HasRole(PulseAiSystemAccess access, params string[] roles) =>
        roles.Any(role => access.RoleCodes.Contains(role));

    private static PulseAiSystemSourceEvidence Source(
        int id,
        string code,
        string name,
        string module,
        string path,
        DateTimeOffset observedAt,
        string scope) =>
        new(id, "governed_internal_database", code, name, module, "INTERNAL", path, "succeeded", 200, observedAt, "current_request", scope);

    private static string CleanPersonReference(string value) =>
        Regex.Replace(value.Trim().Trim('?', '.', '!', ',', ';', ':'), @"\s+", " ", Options);

    private static string CleanProjectReference(string value) =>
        Regex.Replace(value.Trim().Trim('?', '.', '!', ',', ';', ':', '"', '\''), @"\s+", " ", Options);

    private static string NormalizeProjectRole(string value)
    {
        var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), @"\s+", " ", Options);
        if (normalized is "solution architect" or "sa") return "solution_architect";
        if (normalized is "project manager" or "pm") return "project_manager";
        if (normalized is "account executive" or "ae" || normalized.StartsWith("sales", StringComparison.Ordinal)) return "account_executive";
        return string.Empty;
    }

    private static string NormalizeIdentity(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", string.Empty, Options);

    private static string ExactNamePartReference(string reference)
    {
        if (reference.Contains('@')) return string.Empty;
        var referenceParts = Regex.Matches(reference.ToLowerInvariant(), "[a-z0-9]+", Options)
            .Cast<Match>()
            .Select(match => match.Value)
            .Where(value => value.Length >= 2)
            .ToArray();
        return referenceParts.Length == 1 ? referenceParts[0] : string.Empty;
    }

    private static bool IsExactNamePartReference(string reference, string displayName)
    {
        var exactNamePart = ExactNamePartReference(reference);
        if (exactNamePart.Length == 0) return false;
        return Regex.Matches(displayName.ToLowerInvariant(), "[a-z0-9]+", Options)
            .Cast<Match>()
            .Select(match => match.Value)
            .Any(value => string.Equals(value, exactNamePart, StringComparison.Ordinal));
    }

    private static string DetailLevel(string? value) =>
        PulseAiSystemIntelligencePolicy.DetailLevels.Contains(value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            ? value!.ToLowerInvariant()
            : "comprehensive";

    private static string CorrelationId(HttpContext context)
    {
        var header = context.Request.Headers.TryGetValue("X-Correlation-Id", out var value)
            ? value.ToString().Trim()
            : string.Empty;
        var selected = header.Length > 0 ? header : context.TraceIdentifier;
        return selected[..Math.Min(selected.Length, 160)];
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static int EditDistance(string left, string right)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private static string Diagnostic(Exception exception) => exception switch
    {
        CelarAiInternalDataSourceException source => $"database_schema_not_ready_{source.Diagnostic}",
        PostgresException postgres => $"postgres_{postgres.SqlState}",
        NpgsqlException => "database_transport_failure",
        TimeoutException => "timeout",
        _ => "internal_data_query_failure"
    };

    private enum PersonResolutionOutcome { Resolved, NotFound, Ambiguous }
    private enum ProjectResolutionOutcome { Resolved, NotFound, Ambiguous }

    private sealed class CelarAiInternalDataSourceException(string diagnostic) : Exception(diagnostic)
    {
        public string Diagnostic { get; } = diagnostic;
    }

    private sealed record PersonCandidate(Guid UserId, string DisplayName, string Email, bool MatchedVerifiedAlias);
    private sealed record PersonResolution(PersonResolutionOutcome Outcome, PersonCandidate? Person, IReadOnlyList<string> Suggestions);
    private sealed record ProjectCandidate(
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        string Status,
        string Description,
        DateOnly? StartDate,
        DateOnly? EndDate,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        string? ProjectManagerName,
        string? AccountExecutiveName,
        string? SolutionArchitectName);
    private sealed record ProjectResolution(ProjectResolutionOutcome Outcome, ProjectCandidate? Project, IReadOnlyList<string> Suggestions);
    private sealed record PersonProjectRow(
        Guid ProjectId,
        string ProjectCode,
        string ProjectName,
        string Status,
        bool IsProjectManager,
        bool IsAccountExecutive,
        bool IsSolutionArchitect,
        bool IsAssignedResource,
        long ActiveTaskAssignmentCount);
    private sealed record PersonTaskRow(Guid TaskId, Guid ProjectId, string ProjectCode, string ProjectName, string TaskCode, string TaskName, DateOnly EffectiveStartDate, DateOnly? EffectiveEndDate, decimal AssignedHours, string SourceCode);
    private sealed record ProjectHistoryRow(DateTimeOffset OccurredAt, string ProcessArea, string EventType, string PriorState, string NewState, string Summary, string Reason, string? ActorName);
    private sealed record AnswerOutcome(string Status, PulseAiSystemDetailedAnswer Answer, IReadOnlyList<PulseAiSystemSourceEvidence> Sources, IReadOnlyList<string> Warnings);
    private sealed record PersistenceContext(Guid ConversationId, Guid UserMessageId, Guid InquiryRunId, Guid EffectiveUserId, bool Persisted);
}
