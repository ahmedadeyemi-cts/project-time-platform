#!/usr/bin/env python3
"""Build a guarded Celar AI internal-data compiler copy.

The canonical service remains the reviewable source. This generator adds two
runtime-hardening layers that are intentionally local to the private internal-data
resolver:

1. explicit current-question context can satisfy person/project references for
   deterministic questions without forcing users to repeat the selected name;
2. project stakeholder/history queries use a smaller project-fact authorization
   scope and readiness contract so unrelated workload sources do not take down
   otherwise authoritative project facts.

Every replacement is anchor-checked and fails closed if the canonical source
shape changes.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import sys


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one source anchor, found {count}")
    return text.replace(old, new, 1)


PROJECT_FACTS_SQL = r'''    private const string ProjectFactsScopeCte = """
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
              )
        ),
        current_project_people AS (
            SELECT DISTINCT assignment.project_id, assignment.user_id
            FROM project_assignments assignment
            WHERE assignment.effective_start_date <= CURRENT_DATE
              AND (assignment.effective_end_date IS NULL OR assignment.effective_end_date >= CURRENT_DATE)
              AND COALESCE(assignment.module001a_closeout_status, 'active') = 'active'
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
        )
        """;

    private const string ProjectFactsReadinessSql = """
        WITH required_table(table_name) AS (
            VALUES
                ('app_users'),
                ('projects'),
                ('project_assignments')
        ),
        required_column(table_name, column_name) AS (
            VALUES
                ('app_users', 'user_id'),
                ('app_users', 'display_name'),
                ('app_users', 'team_name'),
                ('app_users', 'department_name'),
                ('app_users', 'department'),
                ('app_users', 'is_active'),
                ('projects', 'project_id'),
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
                ('project_assignments', 'project_id'),
                ('project_assignments', 'user_id'),
                ('project_assignments', 'effective_start_date'),
                ('project_assignments', 'effective_end_date'),
                ('project_assignments', 'module001a_closeout_status')
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

'''

CONTEXT_HELPERS = r'''    private static CelarAiInternalDataQuery ApplyExplicitContext(
        CelarAiInternalDataQuery query,
        ExplicitQuestionContext context)
    {
        var personReference = query.PersonReference;
        var projectReference = query.ProjectReference;
        var kind = query.Kind;
        var contextualPerson = context.PersonOrTeam.Length > 0 && IsContextualPersonReference(personReference);
        if (contextualPerson)
        {
            personReference = context.PersonOrTeam;
            if (kind == CelarAiInternalDataQueryKind.PersonTaskList
                && !Regex.IsMatch(context.Question, @"\btasks?\b", Options))
            {
                kind = CelarAiInternalDataQueryKind.PersonWorkSummary;
            }
        }
        if (context.ProjectReference.Length > 0 && IsContextualProjectReference(projectReference))
            projectReference = context.ProjectReference;
        return query with { Kind = kind, PersonReference = personReference, ProjectReference = projectReference };
    }

    private static bool IsContextualPersonReference(string value)
    {
        var normalized = NormalizeIdentity(value);
        return normalized is "thisperson" or "thisuser" or "thisengineer" or "selectedperson"
            or "they" or "them" or "he" or "she" or "him" or "her";
    }

    private static bool IsContextualProjectReference(string value)
    {
        var normalized = NormalizeIdentity(value);
        return normalized is "thisproject" or "theproject" or "selectedproject" or "currentproject" or "ourproject";
    }

    private static CelarAiInternalDataQuery? MatchContextualQuestion(
        string question,
        ExplicitQuestionContext context)
    {
        if (question.Length == 0) return null;

        if (context.ProjectReference.Length > 0)
        {
            var stakeholder = Regex.Match(
                question,
                @"^\s*(?:who|what)\s+(?:is\s+)?(?:the\s+)?(?<role>account\s+executive|ae|sales\s*person|sales\s+person|sales\s+rep(?:resentative)?|solution\s+architect|sa|project\s+manager|pm)(?:\s+assigned)?(?:\s+(?:for|on)\s+(?:this\s+)?project)?\s*[?.!]*\s*$",
                Options);
            if (stakeholder.Success)
            {
                var role = NormalizeProjectRole(stakeholder.Groups["role"].Value);
                if (role.Length > 0)
                {
                    return new CelarAiInternalDataQuery(
                        CelarAiInternalDataQueryKind.ProjectStakeholderLookup,
                        string.Empty,
                        false,
                        context.ProjectReference,
                        role);
                }
            }

            if (Regex.IsMatch(
                question,
                @"^\s*(?:(?:show|give|tell)(?:\s+me)?\s+(?:the\s+)?(?:project\s+)?(?:history|historical\s+context|timeline)|what\s+(?:is|was)\s+(?:the\s+)?(?:project\s+)?(?:history|historical\s+context|timeline)|what\s+happened\s+(?:with|to)\s+(?:this\s+)?project)\s*[?.!]*\s*$",
                Options))
            {
                return new CelarAiInternalDataQuery(
                    CelarAiInternalDataQueryKind.ProjectHistory,
                    string.Empty,
                    false,
                    context.ProjectReference);
            }
        }

        if (context.PersonOrTeam.Length > 0)
        {
            var person = CleanPersonReference(context.PersonOrTeam);
            if (person.Length is < 2 or > 255) return null;
            var normalized = Regex.Replace(question.Trim().ToLowerInvariant(), @"\s+", " ", Options);
            var hasProjects = Regex.IsMatch(normalized, @"\bprojects?\b", Options);
            var hasTasks = Regex.IsMatch(normalized, @"\btasks?\b", Options);

            if (normalized.StartsWith("how many", StringComparison.Ordinal) && hasProjects && hasTasks)
                return new CelarAiInternalDataQuery(CelarAiInternalDataQueryKind.PersonWorkSummary, person, true);
            if (normalized.StartsWith("how many", StringComparison.Ordinal) && hasProjects)
                return new CelarAiInternalDataQuery(CelarAiInternalDataQueryKind.PersonProjectCount, person, true);
            if (normalized.StartsWith("how many", StringComparison.Ordinal) && hasTasks)
                return new CelarAiInternalDataQuery(CelarAiInternalDataQueryKind.PersonTaskCount, person, true);
            if (hasProjects && Regex.IsMatch(normalized, @"^(?:which|what|list|show)\b", Options))
                return new CelarAiInternalDataQuery(CelarAiInternalDataQueryKind.PersonProjectList, person, false);
            if (hasTasks && Regex.IsMatch(normalized, @"^(?:which|what|list|show)\b", Options))
                return new CelarAiInternalDataQuery(CelarAiInternalDataQueryKind.PersonTaskList, person, false);
            if (Regex.IsMatch(normalized, @"\b(?:working\s+on|work\s+on|doing|assigned)\b", Options))
                return new CelarAiInternalDataQuery(CelarAiInternalDataQueryKind.PersonWorkSummary, person, false);
        }

        return null;
    }

    private static ExplicitQuestionContext ParseExplicitQuestionContext(string? question)
    {
        var raw = question?.Trim() ?? string.Empty;
        if (raw.Length == 0) return new ExplicitQuestionContext(string.Empty, string.Empty, string.Empty);

        const string marker = "Explicit current-question context:";
        var markerIndex = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return new ExplicitQuestionContext(raw, string.Empty, string.Empty);

        var coreQuestion = raw[..markerIndex].Trim();
        var projectCode = string.Empty;
        var projectName = string.Empty;
        var personOrTeam = string.Empty;
        var contextText = raw[(markerIndex + marker.Length)..];
        foreach (var rawLine in contextText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("-", StringComparison.Ordinal)) line = line[1..].Trim();
            if (line.StartsWith("Project code:", StringComparison.OrdinalIgnoreCase))
                projectCode = CleanProjectReference(line["Project code:".Length..]);
            else if (line.StartsWith("Project name:", StringComparison.OrdinalIgnoreCase))
                projectName = CleanProjectReference(line["Project name:".Length..]);
            else if (line.StartsWith("Person or team:", StringComparison.OrdinalIgnoreCase))
                personOrTeam = CleanPersonReference(line["Person or team:".Length..]);
        }

        var projectReference = projectCode.Length > 0 ? projectCode : projectName;
        return new ExplicitQuestionContext(coreQuestion, projectReference, personOrTeam);
    }

'''

READINESS_METHOD = r'''    private static async Task ValidateSourceReadinessAsync(
        NpgsqlConnection connection,
        CelarAiInternalDataQueryKind kind,
        CancellationToken cancellationToken)
    {
        var readinessSql = IsPersonQuery(kind) ? SourceReadinessSql : ProjectFactsReadinessSql;
        await using var command = new NpgsqlCommand(readinessSql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var problems = value as string[] ?? [];
        if (problems.Length > 0)
            throw new CelarAiInternalDataSourceException(string.Join("+", problems.Take(8)));
    }
'''


def transform(source: str) -> str:
    source = replace_once(
        source,
        '    private static readonly string ExactProjectSql = ScopeCte + """\n',
        PROJECT_FACTS_SQL + '    private static readonly string ExactProjectSql = ProjectFactsScopeCte + """\n',
        'project-fact scope insertion',
    )
    source = replace_once(
        source,
        '    private static readonly string AuthorizedProjectsSql = ScopeCte + """\n',
        '    private static readonly string AuthorizedProjectsSql = ProjectFactsScopeCte + """\n',
        'project suggestion scope',
    )
    source = replace_once(
        source,
        '        var value = question?.Trim() ?? string.Empty;\n',
        '        var explicitContext = ParseExplicitQuestionContext(question);\n        var value = explicitContext.Question.Trim();\n',
        'context-aware parse entry',
    )
    source = replace_once(
        source,
        '        return MatchPerson(value, PersonWorkSummaryPatterns, CelarAiInternalDataQueryKind.PersonWorkSummary, true)\n',
        '        var parsed = MatchPerson(value, PersonWorkSummaryPatterns, CelarAiInternalDataQueryKind.PersonWorkSummary, true)\n',
        'context-aware parse result variable',
    )
    source = replace_once(
        source,
        '            ?? MatchProject(value, ProjectHistoryPatterns, CelarAiInternalDataQueryKind.ProjectHistory);\n',
        '            ?? MatchProject(value, ProjectHistoryPatterns, CelarAiInternalDataQueryKind.ProjectHistory);\n        if (parsed is not null) return ApplyExplicitContext(parsed, explicitContext);\n        return MatchContextualQuestion(value, explicitContext);\n',
        'contextual parser fallback',
    )
    source = replace_once(
        source,
        '            await ValidateSourceReadinessAsync(connection, cancellationToken);\n',
        '            await ValidateSourceReadinessAsync(connection, query.Kind, cancellationToken);\n',
        'query-specific readiness call',
    )
    source = replace_once(
        source,
        '    private static async Task ValidateSourceReadinessAsync(\n',
        CONTEXT_HELPERS + '    private static async Task ValidateSourceReadinessAsync(\n',
        'context helper insertion',
    )
    old_readiness = r'''    private static async Task ValidateSourceReadinessAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SourceReadinessSql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var problems = value as string[] ?? [];
        if (problems.Length > 0)
            throw new CelarAiInternalDataSourceException(string.Join("+", problems.Take(8)));
    }
'''
    source = replace_once(source, old_readiness, READINESS_METHOD, 'query-specific readiness method')
    source = replace_once(
        source,
        '    private enum PersonResolutionOutcome { Resolved, NotFound, Ambiguous }\n',
        '    private sealed record ExplicitQuestionContext(string Question, string ProjectReference, string PersonOrTeam);\n\n    private enum PersonResolutionOutcome { Resolved, NotFound, Ambiguous }\n',
        'context record insertion',
    )

    required = [
        'ProjectFactsScopeCte',
        'ProjectFactsReadinessSql',
        'ApplyExplicitContext',
        'MatchContextualQuestion',
        'ParseExplicitQuestionContext',
        'Explicit current-question context:',
        'ValidateSourceReadinessAsync(connection, query.Kind, cancellationToken)',
        'ExactProjectSql = ProjectFactsScopeCte',
        'AuthorizedProjectsSql = ProjectFactsScopeCte',
    ]
    missing = [marker for marker in required if marker not in source]
    if missing:
        raise RuntimeError('generated source missing markers: ' + ', '.join(missing))
    return source


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument('--input', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()

    input_path = Path(args.input)
    output_path = Path(args.output)
    try:
        generated = transform(input_path.read_text(encoding='utf-8'))
    except Exception as exc:  # fail closed for build/release validation
        print(f'CELAR_AI_INTERNAL_CONTEXT_RESILIENCE_GENERATION=FAIL {exc}', file=sys.stderr)
        return 42

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(generated, encoding='utf-8')
    print('CELAR_AI_INTERNAL_CONTEXT_RESILIENCE_GENERATION=PASS')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
