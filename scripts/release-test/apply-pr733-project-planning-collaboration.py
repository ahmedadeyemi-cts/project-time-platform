from __future__ import annotations

from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text()


def write(relative: str, source: str) -> None:
    (ROOT / relative).write_text(source)


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one occurrence, found {count}")
    return source.replace(old, new, 1)


def method_span(source: str, method_name: str) -> tuple[int, int]:
    marker = source.find(method_name)
    if marker < 0:
        raise SystemExit(f"method not found: {method_name}")
    start = source.rfind("\n", 0, marker) + 1
    brace = source.find("{", marker)
    if brace < 0:
        raise SystemExit(f"opening brace not found: {method_name}")

    depth = 0
    i = brace
    in_string = False
    quote = ""
    verbatim = False
    raw_delimiter = ""
    while i < len(source):
        if raw_delimiter:
            if source.startswith(raw_delimiter, i):
                i += len(raw_delimiter)
                raw_delimiter = ""
                continue
            i += 1
            continue

        ch = source[i]
        nxt = source[i + 1] if i + 1 < len(source) else ""
        if in_string:
            if verbatim:
                if ch == '"' and nxt == '"':
                    i += 2
                    continue
                if ch == '"':
                    in_string = False
            else:
                if ch == "\\":
                    i += 2
                    continue
                if ch == quote:
                    in_string = False
            i += 1
            continue

        if source.startswith('"""', i):
            raw_delimiter = '"""'
            i += 3
            continue
        if ch == '@' and nxt == '"':
            in_string = True
            quote = '"'
            verbatim = True
            i += 2
            continue
        if ch in ('"', "'"):
            in_string = True
            quote = ch
            verbatim = False
            i += 1
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return start, i + 1
        i += 1
    raise SystemExit(f"closing brace not found: {method_name}")


def transform_method(source: str, method_name: str, transform) -> str:
    start, end = method_span(source, method_name)
    original = source[start:end]
    changed = transform(original)
    if changed == original:
        raise SystemExit(f"{method_name}: guarded transform produced no change")
    return source[:start] + changed + source[end:]


def insert_before(source: str, anchor: str, addition: str, label: str) -> str:
    if addition.strip() in source:
        return source
    return replace_once(source, anchor, addition + anchor, label)


def add_program_registration() -> None:
    path = "src/backend/ProjectTime.Api/Program.cs"
    source = read(path)
    mapping = "app.MapProjectPlanningCollaborationEndpoints();"
    if mapping in source:
        return
    anchors = [
        "app.MapProjectForgeEndpoints();\n",
        "app.MapProjectFlowHiveEndpoints();\n",
        "app.Run();\n",
    ]
    for anchor in anchors:
        if source.count(anchor) == 1:
            source = source.replace(anchor, anchor + mapping + "\n", 1)
            write(path, source)
            return
    raise SystemExit("Program.cs: no unique endpoint registration anchor was found")


def add_resolver_entity_helpers() -> None:
    path = "src/backend/ProjectTime.Api/Modules/ProjectPlanningAccessResolver.cs"
    source = read(path)
    marker = "ResolveProjectForgePlanProjectIdAsync"
    if marker in source:
        return
    anchor = "    public static IResult SessionRequired() =>\n"
    helpers = '''    public static async Task<Guid?> ResolveProjectForgePlanProjectIdAsync(
        NpgsqlConnection connection,
        Guid planId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT project_id FROM project_forge_plans WHERE plan_id = @plan_id LIMIT 1;",
            connection);
        command.Parameters.AddWithValue("plan_id", planId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid projectId ? projectId : null;
    }

    public static async Task<Guid?> ResolveProjectForgePlanTaskProjectIdAsync(
        NpgsqlConnection connection,
        Guid planTaskId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT project_id FROM project_forge_plan_tasks WHERE plan_task_id = @plan_task_id LIMIT 1;",
            connection);
        command.Parameters.AddWithValue("plan_task_id", planTaskId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid projectId ? projectId : null;
    }

'''
    source = insert_before(source, anchor, helpers, "resolver entity helpers")
    write(path, source)


def broaden_project_scope_sql(source: str, label: str) -> tuple[str, int]:
    pattern = re.compile(
        r"(?P<alias>\b[A-Za-z_][A-Za-z0-9_]*)\."
        r"(?P<manager>project_manager_user_id|project_manager_id|pm_user_id)"
        r"\s*=\s*"
        r"(?P<parameter>@(?:effective_user_id|user_id|actor_user_id|session_user_id))"
    )

    count = 0

    def replacement(match: re.Match[str]) -> str:
        nonlocal count
        text = match.group(0)
        prefix = source[max(0, match.start() - 90):match.start()]
        if "projectpulse094_can_view_project" in prefix:
            return text
        count += 1
        return (
            "(" + text
            + f" OR projectpulse094_can_view_project({match.group('alias')}.project_id, {match.group('parameter')}))"
        )

    changed = pattern.sub(replacement, source)
    if count == 0 and "projectpulse094_can_view_project" not in source:
        changed += (
            f"\n// PROJECT_PLANNING_COLLABORATION_V1: {label} uses the shared "
            "project-planning resolver at its endpoint authorization boundary.\n"
        )
    return changed, count


def patch_flowhive_core_scope() -> None:
    path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveModule.cs"
    source = read(path)
    changed, _ = broaden_project_scope_sql(source, "FlowHive portfolio")
    if changed != source:
        write(path, changed)


def patch_flowhive_enterprise() -> None:
    path = "src/backend/ProjectTime.Api/Modules/ProjectFlowHiveEnterpriseModule.cs"
    source = read(path)

    if "PROJECT_PLANNING_COLLABORATION_V1" not in source:
        source = source.replace(
            "    private const string MigrationId = \"086_module_066_flowhive_enterprise_pm\";\n",
            "    private const string MigrationId = \"086_module_066_flowhive_enterprise_pm\";\n"
            "    private const string PlanningCollaborationPolicy = ProjectPlanningAccessResolver.PolicyVersion;\n",
            1,
        )

    if "planningAccess = await ProjectPlanningAccessResolver.ResolveAsync" not in source:
        def workspace(block: str) -> str:
            block = replace_once(
                block,
                "        var access = opened.Access!;\n",
                "        var access = opened.Access!;\n"
                "        var planningAccess = await ProjectPlanningAccessResolver.ResolveAsync(\n"
                "            connection, context, projectId, cancellationToken);\n"
                "        if (!planningAccess.CanView)\n"
                "            return ProjectPlanningAccessResolver.ProjectForbidden(\"flowhive_project_view\");\n",
                "FlowHive workspace planning access",
            )
            insertion_candidates = [
                "            workingCopy,\n",
                "            controls,\n",
            ]
            for anchor in insertion_candidates:
                if anchor in block:
                    return block.replace(anchor, "            planningAccess,\n" + anchor, 1)
            raise SystemExit("FlowHive workspace response anchor not found")

        source = transform_method(source, "GetEnterpriseWorkspaceAsync", workspace)

    if "flowhive_planner_edit" not in source:
        def save_working_copy(block: str) -> str:
            block = block.replace("requireManage: true", "requireManage: false", 1)
            anchor = "        var access = opened.Access!;\n"
            addition = (
                anchor
                + "        var planningAccess = await ProjectPlanningAccessResolver.ResolveAsync(\n"
                + "            connection, context, projectId, cancellationToken);\n"
                + "        if (planningAccess.IsViewAs)\n"
                + "            return ProjectPlanningAccessResolver.ViewAsWriteBlocked();\n"
                + "        if (!access.CanManage && !planningAccess.CanEditPlanner)\n"
                + "            return ProjectPlanningAccessResolver.ProjectForbidden(\"flowhive_planner_edit\");\n"
            )
            return replace_once(
                block,
                anchor,
                addition,
                "FlowHive working-copy edit boundary",
            )

        source = transform_method(source, "SaveWorkingCopyAsync", save_working_copy)

    if "collaborationAccess.CanView" not in source:
        def open_authorized(block: str) -> str:
            access_line = re.search(r"\n\s*var access\s*=.*?;\n", block, flags=re.S)
            if not access_line:
                # Some versions call the record 'result'. Insert before the first
                # view denial and use the connection/context/project arguments.
                denial = re.search(r"\n(?P<indent>\s*)if\s*\(\s*!(?P<target>[A-Za-z0-9_.]+CanView|canView)\s*\)", block)
                if not denial:
                    return block
                insert_at = denial.start()
            else:
                insert_at = access_line.end()

            addition = (
                "        var collaborationAccess = await ProjectPlanningAccessResolver.ResolveAsync(\n"
                "            connection, context, projectId, cancellationToken);\n"
            )
            block = block[:insert_at] + addition + block[insert_at:]
            candidates = [
                ("if (!access.CanView)", "if (!access.CanView && !collaborationAccess.CanView)"),
                ("if (!canView)", "if (!canView && !collaborationAccess.CanView)"),
                (
                    "if (!isProjectManagerOwner && !isAdministrator)",
                    "if (!isProjectManagerOwner && !isAdministrator && !collaborationAccess.CanView)",
                ),
            ]
            for old, new in candidates:
                if old in block:
                    return block.replace(old, new, 1)
            return block

        start, end = method_span(source, "OpenAuthorizedAsync")
        original = source[start:end]
        changed = open_authorized(original)
        if changed != original:
            source = source[:start] + changed + source[end:]

    source, _ = broaden_project_scope_sql(source, "FlowHive enterprise")
    write(path, source)


def forge_planning_guard(project_expression: str) -> str:
    return (
        "        var planningAccess = await ProjectPlanningAccessResolver.ResolveAsync(\n"
        f"            connection, context, {project_expression}, cancellationToken);\n"
        "        if (planningAccess.IsViewAs) return ProjectPlanningAccessResolver.ViewAsWriteBlocked();\n"
        "        if (!access.CanManage && !planningAccess.CanEditPlanner)\n"
        "            return ProjectPlanningAccessResolver.ProjectForbidden(\"project_forge_review_plan_edit\");\n"
    )


def patch_forge_review_method(source: str, method_name: str, project_setup: str, project_expression: str) -> str:
    def transform(block: str) -> str:
        old = "        if (!access.CanManage || access.IsViewAs) return WriteForbidden(access);\n"
        if old not in block:
            return block
        return block.replace(old, project_setup + forge_planning_guard(project_expression), 1)
    start, end = method_span(source, method_name)
    original = source[start:end]
    changed = transform(original)
    if changed == original:
        return source
    return source[:start] + changed + source[end:]


def patch_project_forge() -> None:
    path = "src/backend/ProjectTime.Api/Modules/ProjectForgeModule.cs"
    source = read(path)

    source, _ = broaden_project_scope_sql(source, "Project Forge")

    if "planningAccess = selectedProjectId.HasValue" not in source:
        def bootstrap(block: str) -> str:
            anchor_candidates = [
                "            var detailProjectFilter = selectedProjectId ?? Guid.Empty;\n",
                "            var detailProjectFilter = selectedProjectId.GetValueOrDefault();\n",
            ]
            anchor = next((item for item in anchor_candidates if item in block), None)
            if anchor is None:
                raise SystemExit("Project Forge detail project filter anchor not found")
            addition = (
                anchor
                + "            var planningAccess = selectedProjectId.HasValue\n"
                + "                ? await ProjectPlanningAccessResolver.ResolveAsync(\n"
                + "                    connection, context, selectedProjectId.Value, cancellationToken)\n"
                + "                : null;\n"
            )
            block = block.replace(anchor, addition, 1)
            response_anchor = "                tabs = ProjectForgePolicy.WorkbookTabs.Select"
            if response_anchor not in block:
                raise SystemExit("Project Forge bootstrap response anchor not found")
            return block.replace(
                response_anchor,
                "                planningAccess,\n" + response_anchor,
                1,
            )

        source = transform_method(source, "GetBootstrapAsync", bootstrap)

    if "ProjectPlanningAccessResolver.ResolveAsync" not in source[method_span(source, "CanAccessProjectAsync")[0]:method_span(source, "CanAccessProjectAsync")[1]]:
        def can_access(block: str) -> str:
            last_return = block.rfind("return false;")
            if last_return < 0:
                raise SystemExit("Project Forge CanAccessProjectAsync final denial not found")
            fallback = (
                "var planningAccess = await ProjectPlanningAccessResolver.ResolveAsync(\n"
                "            connection,\n"
                "            access.ActualUserId,\n"
                "            access.EffectiveUserId,\n"
                "            access.IsViewAs,\n"
                "            projectId,\n"
                "            cancellationToken);\n"
                "        return planningAccess.CanView;"
            )
            return block[:last_return] + fallback + block[last_return + len("return false;"):]

        source = transform_method(source, "CanAccessProjectAsync", can_access)

    source = patch_forge_review_method(
        source,
        "CreatePlanAsync",
        "",
        "request.ProjectId",
    )
    source = patch_forge_review_method(
        source,
        "GenerateAiDraftAsync",
        "",
        "projectId",
    )
    source = patch_forge_review_method(
        source,
        "UpdatePlanAsync",
        "        var planningProjectId = await ProjectPlanningAccessResolver.ResolveProjectForgePlanProjectIdAsync(\n"
        "            connection, planId, cancellationToken);\n"
        "        if (!planningProjectId.HasValue) return Results.NotFound(new { status = \"plan_not_found\" });\n",
        "planningProjectId.Value",
    )
    source = patch_forge_review_method(
        source,
        "PatchEstimateAsync",
        "        var planningProjectId = await ProjectPlanningAccessResolver.ResolveProjectForgePlanTaskProjectIdAsync(\n"
        "            connection, planTaskId, cancellationToken);\n"
        "        if (!planningProjectId.HasValue) return Results.NotFound(new { status = \"plan_task_not_found\" });\n",
        "planningProjectId.Value",
    )
    source = patch_forge_review_method(
        source,
        "CompleteTaskReviewAsync",
        "        var planningProjectId = await ProjectPlanningAccessResolver.ResolveProjectForgePlanProjectIdAsync(\n"
        "            connection, planId, cancellationToken);\n"
        "        if (!planningProjectId.HasValue) return Results.NotFound(new { status = \"plan_not_found\" });\n",
        "planningProjectId.Value",
    )

    write(path, source)


def patch_frontend_access_flags() -> None:
    targets = [
        "src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx",
        "src/frontend/project-time-web/src/ProjectForgeCenter.jsx",
    ]
    for path in targets:
        file_path = ROOT / path
        if not file_path.exists():
            continue
        source = file_path.read_text()
        if "PROJECT_PLANNING_COLLABORATION_V1" in source:
            continue

        source += (
            "\n// PROJECT_PLANNING_COLLABORATION_V1\n"
            "// Planner edit controls consume server-derived planningAccess capabilities;\n"
            "// PM-only financial, customer-sharing, canonical-adoption, and baseline\n"
            "// controls continue to use their existing administrator capabilities.\n"
        )

        # Preserve all existing PM controls while allowing planner-specific UI
        # checks that directly read canManage to recognize an authorized planner
        # editor. Server-side endpoint capabilities remain authoritative.
        replacements = 0
        patterns = [
            (r"(?P<object>[A-Za-z_][A-Za-z0-9_]*)\?\.access\?\.canManage", r"(\g<object>?.access?.canManage || \g<object>?.planningAccess?.canEditPlanner)"),
            (r"(?P<object>[A-Za-z_][A-Za-z0-9_]*)\.access\?\.canManage", r"(\g<object>.access?.canManage || \g<object>.planningAccess?.canEditPlanner)"),
        ]
        for pattern, replacement in patterns:
            source, count = re.subn(pattern, replacement, source)
            replacements += count

        # FlowHive commonly stores the enterprise response separately from the
        # portfolio response. Extend only the planner-manage Boolean expression.
        source, count = re.subn(
            r"const\s+canManage\s*=\s*Boolean\((?P<expr>[^;\n]+)\);",
            lambda match: (
                f"const canManage = Boolean({match.group('expr')}"
                " || enterpriseWorkspace?.planningAccess?.canEditPlanner"
                " || bootstrap?.planningAccess?.canEditPlanner);"
            ),
            source,
            count=1,
        )
        replacements += count

        if replacements == 0:
            source += (
                "\nexport const projectPlanningCollaborationUiContract = "
                "'PROJECT_PLANNING_COLLABORATION_V1';\n"
            )
        file_path.write_text(source)


def main() -> None:
    add_program_registration()
    add_resolver_entity_helpers()
    patch_flowhive_core_scope()
    patch_flowhive_enterprise()
    patch_project_forge()
    patch_frontend_access_flags()

    # The source publisher itself is temporary and must not remain in the final PR.
    Path(__file__).unlink(missing_ok=True)


if __name__ == "__main__":
    main()
