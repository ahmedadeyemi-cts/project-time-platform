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
    PersonTaskList
}

public sealed record CelarAiInternalDataQuery(
    CelarAiInternalDataQueryKind Kind,
    string PersonReference,
    bool CountRequested);

/// <summary>
/// Resolves structured Pulse facts locally from the authoritative database.
/// Every query is read-only, permission-scoped to the effective user, and
/// deterministic. No question text, identity, row, or result is sent to an
/// external model.
/// </summary>
public sealed class CelarAiInternalDataService
{
    public const string ContractVersion = "celar-ai-internal-data-v1-20260807";
    public const string IntentCode = "internal_data";

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
        new(@"^\s*(?:list|show)\s+(?<person>.+?)(?:'s|’s)\s+(?:active\s+)?projects?\s*[?.!]*\s*$", Options)
    ];

    private static readonly Regex[] PersonTaskCountPatterns =
    [
        new(@"^\s*how\s+many\s+(?:active\s+)?tasks?\s+(?:does|do)\s+(?<person>.+?)\s+(?:have|has)(?:\s+assigned(?:\s+to\s+(?:him|her|them))?)?\s*[?.!]*\s*$", Options),
        new(@"^\s*how\s+many\s+(?:active\s+)?tasks?\s+(?:are\s+)?assigned\s+to\s+(?<person>.+?)\s*[?.!]*\s*$", Options)
    ];

    private static readonly Regex[] PersonTaskListPatterns =
    [
        new(@"^\s*(?:which|what)\s+(?:active\s+)?tasks?\s+(?:does|do)\s+(?<person>.+?)\s+(?:have|work\s+on)\s*[?.!]*\s*$", Options),
        new(@"^\s*(?:which|what|list|show(?:\s+me)?)\s+(?:active\s+)?tasks?\s+(?:are\s+)?(?:assigned\s+to|for)\s+(?<person>.+?)\s*[?.!]*\s*$", Options)
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
        scoped_projects AS (
            SELECT DISTINCT project.*
            FROM projects project
            CROSS JOIN requester
            WHERE LOWER(COALESCE(project.status, '')) NOT IN ('closed', 'completed', 'cancelled', 'canceled', 'archived')
              AND (
               @is_broad_scope = TRUE
               OR (@can_view_managed_projects = TRUE AND project.project_manager_user_id = @effective_user_id)
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
        authorized_people AS (
            SELECT user_id FROM requester
            UNION
            SELECT project_manager_user_id
            FROM scoped_projects
            WHERE project_manager_user_id IS NOT NULL
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

    private static readonly string PersonProjectsSql = ScopeCte + """
        , person_projects AS (
            SELECT
                project.project_id,
                project.project_code,
                project.project_name,
                project.status,
                project.project_manager_user_id = @person_user_id AS is_project_manager,
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
                ('projects', 'project_manager_user_id'),
                ('projects', 'status'),
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

        var parsed = Match(value, PersonProjectCountPatterns, CelarAiInternalDataQueryKind.PersonProjectCount, true)
            ?? Match(value, PersonProjectListPatterns, CelarAiInternalDataQueryKind.PersonProjectList, false)
            ?? Match(value, PersonTaskCountPatterns, CelarAiInternalDataQueryKind.PersonTaskCount, true)
            ?? Match(value, PersonTaskListPatterns, CelarAiInternalDataQueryKind.PersonTaskList, false);
        return parsed;
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

            var resolution = await ResolvePersonAsync(
                connection,
                effectiveUserId,
                access,
                query.PersonReference,
                cancellationToken);
            if (resolution.Outcome != PersonResolutionOutcome.Resolved || resolution.Person is null)
            {
                var partial = BuildResolutionAnswer(query, resolution);
                return await FinishAsync(
                    persistence,
                    partial.Answer,
                    partial.Sources,
                    partial.Status,
                    detailLevel,
                    correlationId,
                    partial.Warnings,
                    cancellationToken);
            }

            var completed = query.Kind is CelarAiInternalDataQueryKind.PersonProjectCount
                or CelarAiInternalDataQueryKind.PersonProjectList
                ? await BuildProjectAnswerAsync(connection, effectiveUserId, access, query, resolution.Person, cancellationToken)
                : await BuildTaskAnswerAsync(connection, effectiveUserId, access, query, resolution.Person, cancellationToken);

            return await FinishAsync(
                persistence,
                completed.Answer,
                completed.Sources,
                completed.Status,
                detailLevel,
                correlationId,
                completed.Warnings,
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

    private static CelarAiInternalDataQuery? Match(
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

    private static async Task<AnswerOutcome> BuildProjectAnswerAsync(
        NpgsqlConnection connection,
        Guid effectiveUserId,
        PulseAiSystemAccess access,
        CelarAiInternalDataQuery query,
        PersonCandidate person,
        CancellationToken cancellationToken)
    {
        var rows = new List<PersonProjectRow>();
        long total = 0;
        await using (var command = new NpgsqlCommand(PersonProjectsSql, connection))
        {
            AddScopeParameters(command, effectiveUserId, access);
            command.Parameters.AddWithValue("person_user_id", person.UserId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                total = reader.GetInt64(7);
                rows.Add(new PersonProjectRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetInt64(6)));
            }
        }

        var now = DateTimeOffset.UtcNow;
        var source = new[]
        {
            Source(1, "authorized_person_directory", "Authorized Pulse person identity", "062", "internal:celar-ai/identity-resolution", now, "Exact active identity or verified alias within the effective user's authorized scope"),
            Source(2, "authorized_person_projects", "Authorized project and assignment records", "019/055C", "internal:celar-ai/person-projects", now, "Distinct current projects after project, role, team, assignment-date, closeout, and status scope")
        };
        var plural = total == 1 ? "project" : "projects";
        var details = rows.Select(row =>
        {
            var relationships = new List<string>();
            if (row.IsProjectManager) relationships.Add("Project Manager");
            if (row.IsAssignedResource) relationships.Add($"assigned resource ({row.ActiveTaskAssignmentCount} active task assignment{(row.ActiveTaskAssignmentCount == 1 ? string.Empty : "s")})");
            return $"{row.ProjectCode} — {row.ProjectName}; status {row.Status}; relationship: {string.Join(" and ", relationships)}.";
        }).ToArray();
        var conclusion = $"{person.DisplayName} has {total} active {plural} assigned within your authorized Pulse scope.";
        var answer = new PulseAiSystemDetailedAnswer(
            DirectConclusion: conclusion,
            ExecutiveSummary: total == 0
                ? "The person was resolved to one active authorized Pulse identity, but no current Project Manager or active task-assignment relationship remained after the governed filters were applied."
                : "The result is a deterministic distinct-project count. It combines Project Manager ownership and active task assignments without double-counting a project that contains multiple tasks.",
            ScopeAndFilters:
            [
                $"Person: {person.DisplayName}; identity resolution: {(person.MatchedVerifiedAlias ? "verified alias" : "exact active Pulse identity") }.",
                "Project scope: current effective user's authorized project, PM, team, or assignment scope.",
                "Project statuses excluded: closed, completed, cancelled/canceled, and archived.",
                "Assignment filters: effective start date reached, effective end date not passed, and Module 001A closeout status active.",
                "Count rule: distinct project IDs across Project Manager ownership and active resource assignments."
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
                "Source 2: projects, project manager ownership, project_tasks, and current project_assignments inside the requester's server-authorized scope."
            ],
            KnownUnknownAndStaleValues:
            [
                "Known: the distinct current project count and the returned project relationships at the data-as-of timestamp.",
                "Excluded: projects outside the requester's authorization scope, inactive people, ended/closed assignments, inactive tasks, and closed/completed/cancelled/archived projects.",
                "A zero means no qualifying assignment was found after these filters; it does not mean the person has never worked on a project."
            ],
            Assumptions: [],
            Conflicts: [],
            Limitations:
            [
                "This answer reflects recorded Pulse ownership and assignments, not informal work or activity outside Pulse.",
                "At most 100 project detail rows are displayed, while the numeric count remains the complete distinct count."
            ],
            RisksAndImplications: [],
            RecommendedActions: total > 0
                ? ["Open Module 019 or Module 055C to review the cited project and task-assignment records."]
                : ["Confirm the person's identity and review Module 019/055C if an assignment was expected."],
            FutureEnhancementBlueprint: null,
            NavigationTargets: ["#project-workspace", "#work-register"],
            CitationIds: [1, 2],
            Confidence: 0.98m,
            ConfidenceExplanation: "High confidence because an exact authorized identity was resolved and the value is a deterministic distinct count from current authoritative project and assignment records.",
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
        var rows = new List<PersonTaskRow>();
        long total = 0;
        long totalProjects = 0;
        await using (var command = new NpgsqlCommand(PersonTasksSql, connection))
        {
            AddScopeParameters(command, effectiveUserId, access);
            command.Parameters.AddWithValue("person_user_id", person.UserId);
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
        }

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
                $"Person: {person.DisplayName}; identity resolution: {(person.MatchedVerifiedAlias ? "verified alias" : "exact active Pulse identity") }.",
                "Only active tasks on non-closed projects and current non-closed assignments are included.",
                "Projects and people outside the current effective user's scope are excluded."
            ],
            CurrentState: [$"Active task assignments: {total}.", $"Distinct visible active projects: {totalProjects}.", $"Task detail rows returned: {rows.Count}{(total > rows.Count ? $" of {total}" : string.Empty)}.", "External providers called: none."],
            DetailedAnalysis: rows.Select(row => $"{row.ProjectCode} — {row.ProjectName}; {row.TaskCode} — {row.TaskName}; assigned hours {row.AssignedHours:0.##}; effective {row.EffectiveStartDate:yyyy-MM-dd} through {(row.EffectiveEndDate?.ToString("yyyy-MM-dd") ?? "open")}; authority {row.SourceCode}.").ToArray(),
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

    private static AnswerOutcome BuildResolutionAnswer(
        CelarAiInternalDataQuery query,
        PersonResolution resolution)
    {
        var now = DateTimeOffset.UtcNow;
        var countQuestion = query.Kind is CelarAiInternalDataQueryKind.PersonProjectCount
            or CelarAiInternalDataQueryKind.PersonTaskCount;
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
                new { contractVersion = ContractVersion, intentCode = IntentCode, previousConversationMessagesInjected = false, externalProviderEligible = false },
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

    private static string NormalizeIdentity(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", string.Empty, Options);

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
    private sealed class CelarAiInternalDataSourceException(string diagnostic) : Exception(diagnostic)
    {
        public string Diagnostic { get; } = diagnostic;
    }
    private sealed record PersonCandidate(Guid UserId, string DisplayName, string Email, bool MatchedVerifiedAlias);
    private sealed record PersonResolution(PersonResolutionOutcome Outcome, PersonCandidate? Person, IReadOnlyList<string> Suggestions);
    private sealed record PersonProjectRow(Guid ProjectId, string ProjectCode, string ProjectName, string Status, bool IsProjectManager, bool IsAssignedResource, long ActiveTaskAssignmentCount);
    private sealed record PersonTaskRow(Guid TaskId, Guid ProjectId, string ProjectCode, string ProjectName, string TaskCode, string TaskName, DateOnly EffectiveStartDate, DateOnly? EffectiveEndDate, decimal AssignedHours, string SourceCode);
    private sealed record AnswerOutcome(string Status, PulseAiSystemDetailedAnswer Answer, IReadOnlyList<PulseAiSystemSourceEvidence> Sources, IReadOnlyList<string> Warnings);
    private sealed record PersistenceContext(Guid ConversationId, Guid UserMessageId, Guid InquiryRunId, Guid EffectiveUserId, bool Persisted);
}
