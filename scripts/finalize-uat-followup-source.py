#!/usr/bin/env python3
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def replace_once(path: str, old: str, new: str, label: str) -> None:
    file = ROOT / path
    text = file.read_text(encoding="utf-8")
    if new in text:
        return
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one anchor, found {count}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


# API startup registration.
replace_once(
    "src/backend/ProjectTime.Api/ProjectTime.Api.csproj",
    "print &quot;app.MapPr467UatRepairEndpoints();&quot;; print &quot;app.UseCrmErpOAuthPersistence();&quot;;",
    "print &quot;app.MapPr467UatRepairEndpoints();&quot;; print &quot;app.MapModule006StandalonePipelineEndpoints();&quot;; print &quot;app.MapModule006StandaloneTaskEndpoints();&quot;; print &quot;app.UseCrmErpOAuthPersistence();&quot;;",
    "Module 006 API startup registration",
)

# Module 005 safe endpoint map must expose lifecycle enrichment and PM acceptance.
replace_once(
    "src/backend/ProjectTime.Api/Modules/Module005ProjectExpenseSafeEndpoints.cs",
    '        app.MapGet("/api/project-expenses/uploads", (Func<HttpContext, Task<IResult>>)GetUploadsAsync);\n'
    '        app.MapGet("/api/project-expenses/projects/{projectId:guid}/summary", (Func<Guid, HttpContext, Task<IResult>>)GetProjectSummaryAsync);',
    '        app.MapGet("/api/project-expenses/uploads", (Func<HttpContext, Task<IResult>>)GetUploadsAsync);\n'
    '        app.MapGet("/api/project-expenses/uploads/lifecycle", (Func<HttpContext, Task<IResult>>)GetExpenseUploadLifecycleAsync);\n'
    '        app.MapPost("/api/project-expenses/uploads/{uploadId:guid}/accept", (Func<Guid, HttpContext, Task<IResult>>)AcceptExpenseUploadAsync);\n'
    '        app.MapGet("/api/project-expenses/projects/{projectId:guid}/summary", (Func<Guid, HttpContext, Task<IResult>>)GetProjectSummaryAsync);',
    "Module 005 lifecycle endpoint map",
)

# Module 005 keeps upload history visible even if lifecycle enrichment fails independently.
replace_once(
    "src/frontend/project-time-web/src/ProjectAllocationInfoPanel.jsx",
    "      const history = await api('/api/project-expenses/uploads');\n"
    "      const lifecycle = await api('/api/project-expenses/uploads/lifecycle');\n"
    "      const lifecycleById = new Map((lifecycle.uploads || []).map((item) => [String(item.uploadId), item]));\n"
    "      setUploads((history.uploads || []).map((upload) => ({ ...upload, ...(lifecycleById.get(String(upload.uploadId)) || {}) })));",
    "      const history = await api('/api/project-expenses/uploads');\n"
    "      const baseUploads = history.uploads || [];\n"
    "      try {\n"
    "        const lifecycle = await api('/api/project-expenses/uploads/lifecycle');\n"
    "        const lifecycleById = new Map((lifecycle.uploads || []).map((item) => [String(item.uploadId), item]));\n"
    "        setUploads(baseUploads.map((upload) => ({ ...upload, ...(lifecycleById.get(String(upload.uploadId)) || {}) })));\n"
    "        setStatus(`Upload history loaded — ${baseUploads.length} version(s).`);\n"
    "      } catch (lifecycleFailure) {\n"
    "        setUploads(baseUploads);\n"
    "        setStatus(`Upload history loaded. Lifecycle controls are temporarily unavailable: ${lifecycleFailure instanceof Error ? lifecycleFailure.message : 'refresh to retry'}`);\n"
    "      }",
    "Module 005 resilient history load",
)
expense_path = ROOT / "src/frontend/project-time-web/src/ProjectAllocationInfoPanel.jsx"
expense = expense_path.read_text(encoding="utf-8").replace(
    "setStatus('Deleting upload and restoring the prior version when available…');",
    "setStatus('Deleting upload…');",
)
expense_path.write_text(expense, encoding="utf-8")

# Module 039 compact source-health render path.
replace_once(
    "src/frontend/project-time-web/src/FinancialOperationsRecoveryWorkspace.jsx",
    "function SourceGrid({ sources = [], busySource, onRetry, canRetry = false }) {",
    "function SourceGrid({ sources = [], busySource, onRetry, canRetry = false, compact = false }) {",
    "Module 039 SourceGrid compact parameter",
)
replace_once(
    "src/frontend/project-time-web/src/FinancialOperationsRecoveryWorkspace.jsx",
    "function ModuleRecovery({ moduleCode, authSession }) {",
    "function ModuleRecovery({ moduleCode, authSession, compact = false }) {",
    "Module 039 ModuleRecovery compact parameter",
)
replace_once(
    "src/frontend/project-time-web/src/FinancialOperationsRecoveryWorkspace.jsx",
    '<SourceGrid sources={state.data?.sources ?? []} canRetry busySource={busySource} onRetry={retrySource} />',
    '<SourceGrid sources={state.data?.sources ?? []} canRetry busySource={busySource} onRetry={retrySource} compact={compact} />',
    "Module 039 compact SourceGrid handoff",
)
replace_once(
    "src/frontend/project-time-web/src/FinancialOperationsRecoveryWorkspace.jsx",
    "{moduleCode ? <ModuleRecovery moduleCode={moduleCode} authSession={authSession} /> : null}",
    "{moduleCode ? <ModuleRecovery moduleCode={moduleCode} authSession={authSession} compact={compact} /> : null}",
    "Module 039 compact ModuleRecovery handoff",
)

# Module 065 gets a React-owned button which legacy anchor guards cannot remove.
portal_path = ROOT / "src/frontend/project-time-web/src/ModulesDirectoryPortal.jsx"
portal = portal_path.read_text(encoding="utf-8")
old_link = '''                <a
                  className="modules-directory-open-link"
                  data-module-open-route={module.route}
                  href={module.href || `#${module.route}`}
                  aria-label={`Open Module ${module.moduleNumber} — ${module.label}`}
                >
                  Open module →
                </a>'''
new_link = '''                {/* MODULE_065_REACT_OWNED_OPEN_ACTION */}
                {module.moduleNumber === '065' ? (
                  <button
                    type="button"
                    className="modules-directory-open-link modules-directory-open-button"
                    aria-label={`Open Module ${module.moduleNumber} — ${module.label}`}
                    onClick={() => { window.location.hash = module.route; }}
                  >
                    Open module →
                  </button>
                ) : (
                  <a
                    className="modules-directory-open-link"
                    data-module-open-route={module.route}
                    href={module.href || `#${module.route}`}
                    aria-label={`Open Module ${module.moduleNumber} — ${module.label}`}
                  >
                    Open module →
                  </a>
                )}'''
if "MODULE_065_REACT_OWNED_OPEN_ACTION" not in portal:
    if portal.count(old_link) != 1:
        raise SystemExit("Module 065 React-owned action anchor was not found exactly once.")
    portal = portal.replace(old_link, new_link, 1)
    portal_path.write_text(portal, encoding="utf-8")

availability_css_path = ROOT / "src/frontend/project-time-web/src/module-availability.css"
availability_css = availability_css_path.read_text(encoding="utf-8")
if "MODULE_065_REACT_OWNED_OPEN_ACTION_STYLE" not in availability_css:
    availability_css += '''
/* MODULE_065_REACT_OWNED_OPEN_ACTION_STYLE */
.modules-directory-open-button {
  appearance: none;
  border: 0;
  padding: 0;
  text-align: left;
  background: transparent;
  cursor: pointer;
  font: inherit;
}
[data-module-number="065"] .modules-directory-open-button {
  display: inline-flex !important;
  visibility: visible !important;
  opacity: 1 !important;
}
'''
    availability_css_path.write_text(availability_css, encoding="utf-8")

# Module 055C visibly renders immutable work numbers in rows and drawer headers.
register_path = ROOT / "src/frontend/project-time-web/src/WorkRegisterCenter.jsx"
register = register_path.read_text(encoding="utf-8")
register = register.replace(
    'placeholder="Search customer, project, PM, engineer, AE, SA, SAA, task..."',
    'placeholder="Search customer, project, project number, PM, engineer, AE, SA, SAA, task..."',
)
row_anchor = '''                  <small>{item.workName}</small>
                  <small>{item.contractType ? `Contract: ${projectPulseCanonicalContractType(item.contractType)}` : 'Contract: not set'}</small>'''
row_replacement = '''                  <small>{item.workName}</small>
                  {(item.projectCode || item.project_code) ? (
                    <div className="work-register-row-identifier" data-pr467-row-work-identifier="true">
                      <span>{pr467IdentifierLabel(item)}</span>
                      <strong>{item.projectCode || item.project_code}</strong>
                      <button type="button" onClick={() => navigator.clipboard?.writeText(item.projectCode || item.project_code)}>Copy</button>
                    </div>
                  ) : <small className="work-register-row-identifier-missing">Immutable identifier not assigned</small>}
                  <small>{item.contractType ? `Contract: ${projectPulseCanonicalContractType(item.contractType)}` : 'Contract: not set'}</small>'''
if "data-pr467-row-work-identifier" not in register:
    if register.count(row_anchor) != 1:
        raise SystemExit("Module 055C row identifier anchor was not found exactly once.")
    register = register.replace(row_anchor, row_replacement, 1)

drawer_anchor = '''                <p className="muted">
                  {selectedWorkItem.customerName || 'No customer linked'} · {labelize(selectedWorkItem.sourceTable)}
                </p>'''
drawer_replacement = '''                <p className="muted">
                  {selectedWorkItem.customerName || 'No customer linked'} · {labelize(selectedWorkItem.sourceTable)}
                </p>
                {(selectedWorkItem.projectCode || selectedWorkItem.project_code) ? (
                  <div className="work-register-drawer-identifier" data-pr467-drawer-work-identifier="true">
                    <span>{pr467IdentifierLabel(selectedWorkItem)}</span>
                    <strong>{selectedWorkItem.projectCode || selectedWorkItem.project_code}</strong>
                    <button type="button" onClick={() => navigator.clipboard?.writeText(selectedWorkItem.projectCode || selectedWorkItem.project_code)}>Copy ID</button>
                  </div>
                ) : null}'''
if "data-pr467-drawer-work-identifier" not in register:
    if register.count(drawer_anchor) != 1:
        raise SystemExit("Module 055C drawer identifier anchor was not found exactly once.")
    register = register.replace(drawer_anchor, drawer_replacement, 1)
register_path.write_text(register, encoding="utf-8")

register_css_path = ROOT / "src/frontend/project-time-web/src/work-register-center.css"
register_css = register_css_path.read_text(encoding="utf-8")
if "PR467_VISIBLE_WORK_IDENTIFIER_STYLE" not in register_css:
    register_css += '''
/* PR467_VISIBLE_WORK_IDENTIFIER_STYLE */
.work-register-row-identifier,
.work-register-drawer-identifier {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: .4rem;
  margin: .35rem 0;
  padding: .38rem .5rem;
  border: 1px solid rgba(14, 116, 144, .25);
  border-radius: .65rem;
  background: rgba(224, 242, 254, .7);
}
.work-register-row-identifier span,
.work-register-drawer-identifier span {
  color: #475569;
  font-size: .7rem;
  font-weight: 800;
  text-transform: uppercase;
}
.work-register-row-identifier strong,
.work-register-drawer-identifier strong {
  color: #075985;
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  letter-spacing: .03em;
}
.work-register-row-identifier button,
.work-register-drawer-identifier button {
  border: 0;
  border-radius: .45rem;
  padding: .24rem .42rem;
  color: #075985;
  background: #fff;
  cursor: pointer;
  font-size: .7rem;
  font-weight: 800;
}
.work-register-row-identifier-missing {
  color: #92400e;
}
'''
    register_css_path.write_text(register_css, encoding="utf-8")

# Module 006 context reflects standalone editing.
guide_path = ROOT / "src/frontend/project-time-web/src/PageContextGuide.jsx"
guide = guide_path.read_text(encoding="utf-8").replace(
    "'toyota-hyundai-pipelines': { title: 'Toyota & Hyundai Pipelines — Module 006', description: 'Review the governed Toyota and Hyundai pipeline snapshot, bounded history, filters, and exports.' },",
    "'toyota-hyundai-pipelines': { page: 'Toyota & Hyundai Pipelines — Module 006', purpose: 'Standalone Toyota and Hyundai pipeline management for project rows, tasks, status updates, notes, review dates, history, and exports.', backend: '/api/module-006/pipeline and /api/module-006/tasks', check: 'Create or open a project, save a status note, create a standalone task, and confirm no other project module is opened or modified.' },",
)
guide_path.write_text(guide, encoding="utf-8")

# Module 006 project archive and restore share one optimistic-concurrency endpoint.
pipeline_path = ROOT / "src/backend/ProjectTime.Api/Modules/Module006StandalonePipelineModule.cs"
pipeline = pipeline_path.read_text(encoding="utf-8").replace(
    "public sealed record Module006ArchiveRequest(string? Reason, int ExpectedRevision);",
    "public sealed record Module006ArchiveRequest(string? Reason, int ExpectedRevision, bool Archive = true);",
)
archive_pattern = re.compile(
    r"    private static async Task<IResult> ArchiveRecordAsync\([\s\S]*?\n    private static async Task InsertUpdateAsync"
)
archive_replacement = '''    private static async Task<IResult> ArchiveRecordAsync(
        Guid recordId,
        Module006ArchiveRequest request,
        HttpContext context)
    {
        try
        {
            var reason = Clean(request.Reason);
            if (reason.Length < 5) return Invalid("Enter a lifecycle reason of at least five characters.");

            await using var connection = await OpenConnectionAsync();
            var actor = await LoadActorAsync(connection, context);
            if (actor is null) return SessionRequired();
            if (actor.IsViewAs) return ViewAsReadOnly();
            if (!actor.CanEdit) return AccessDenied();
            if (!await RuntimeReadyAsync(connection)) return MigrationRequired();

            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            int? revision;
            await using (var command = new NpgsqlCommand("""
                SELECT revision
                FROM module006_pipeline_records
                WHERE module006_pipeline_record_id = @record_id
                FOR UPDATE;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("record_id", recordId);
                revision = await command.ExecuteScalarAsync(context.RequestAborted) as int?;
            }
            if (revision is null)
                return Results.NotFound(new { status = "module006_record_not_found", message = "The Module 006 row was not found." });
            if (request.ExpectedRevision > 0 && request.ExpectedRevision != revision.Value)
                return Results.Conflict(new
                {
                    status = "module006_revision_conflict",
                    message = "Someone else updated this Module 006 row. Refresh before changing its lifecycle.",
                    currentRevision = revision
                });

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var action = request.Archive ? "Archived" : "Restored";
            var nextStatus = request.Archive ? "Archived" : "Active";
            var note = $"{action}: {reason}";
            await InsertUpdateAsync(connection, transaction, recordId, note, nextStatus,
                today, null, actor.EffectiveUserId, context.RequestAborted);

            await using (var command = new NpgsqlCommand("""
                UPDATE module006_pipeline_records
                SET lifecycle = @lifecycle,
                    is_archived = @archive,
                    status = @status,
                    latest_note = @note,
                    update_date = @update_date,
                    revision = revision + 1,
                    updated_by_user_id = @actor_id,
                    updated_at = NOW()
                WHERE module006_pipeline_record_id = @record_id;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("record_id", recordId);
                command.Parameters.AddWithValue("lifecycle", request.Archive ? "historical" : "active");
                command.Parameters.AddWithValue("archive", request.Archive);
                command.Parameters.AddWithValue("status", nextStatus);
                command.Parameters.AddWithValue("note", note);
                command.Parameters.AddWithValue("update_date", today);
                command.Parameters.AddWithValue("actor_id", actor.EffectiveUserId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }

            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new
            {
                status = request.Archive ? "module006_pipeline_record_archived" : "module006_pipeline_record_restored",
                message = request.Archive
                    ? "The Module 006 row was archived and its history was preserved."
                    : "The Module 006 row was restored to the active pipeline.",
                recordId,
                revision = revision.Value + 1,
                authority = "module006",
                linkedToModule055C = false
            });
        }
        catch (Exception exception)
        {
            return RuntimeFailure(exception, request.Archive ? "archive" : "restore");
        }
    }

    private static async Task InsertUpdateAsync'''
if "module006_pipeline_record_restored" not in pipeline:
    pipeline, count = archive_pattern.subn(archive_replacement, pipeline, count=1)
    if count != 1:
        raise SystemExit("Module 006 archive/restore function was not found exactly once.")
pipeline_path.write_text(pipeline, encoding="utf-8")

# Normalize Npgsql DateOnly/DBNull conditional expression types.
task_path = ROOT / "src/backend/ProjectTime.Api/Modules/Module006StandaloneTaskModule.cs"
task_source = task_path.read_text(encoding="utf-8").replace(
    "dueDate.HasValue ? dueDate.Value : DBNull.Value",
    "dueDate.HasValue ? (object)dueDate.Value : DBNull.Value",
)
task_path.write_text(task_source, encoding="utf-8")

# One-time controls are not part of the final source boundary.
for relative in [
    ".github/workflows/uat-followup-source-finalizer.yml",
    ".github/workflows/uat-followup-trigger.yml",
    ".github/workflows/uat-followup-pr-finalizer.yml",
    "scripts/finalize-uat-followup-source.py",
]:
    target = ROOT / relative
    if target.exists():
        target.unlink()

print("UAT_FOLLOWUP_SOURCE_FINALIZER=PASS")
