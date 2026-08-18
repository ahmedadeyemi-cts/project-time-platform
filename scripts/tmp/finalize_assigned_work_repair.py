from pathlib import Path

root = Path.cwd()


def load(path):
    return (root / path).read_text(encoding='utf-8')


def save(path, text):
    (root / path).write_text(text, encoding='utf-8')


def one(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f'{label}: expected 1 marker, found {count}')
    return text.replace(old, new, 1)

# Module 001 available tasks.
p = 'src/backend/ProjectTime.Api/Program.cs'
s = load(p)
s = one(s,
'''    while (start.DayOfWeek != DayOfWeek.Monday)
    {
        start = start.AddDays(-1);
    }
''',
'''    while (start.DayOfWeek != DayOfWeek.Sunday)
    {
        start = start.AddDays(-1);
    }
''', 'Sunday week authority')
s = one(s,
'''            COALESCE(NULLIF(to_jsonb(pt)->>'service_request_number', ''), '') AS service_request_number,
            pt.billable AS billable,
            COALESCE(pt.utilization_bucket, CASE WHEN pt.billable THEN 'billable' ELSE 'non_billable' END) AS utilization_bucket,
            COALESCE(pm.display_name, 'No PM assigned') AS project_manager_name,
            COALESCE(NULLIF(p.work_type, ''), 'Project') AS work_type,
            CASE
                WHEN lower(COALESCE(NULLIF(p.work_type, ''), 'Project')) IN ('project', 'iqs')
                    THEN 'regular'
                ELSE 'requests'
            END AS time_entry_section,
''',
'''            COALESCE(
                NULLIF(to_jsonb(pt)->>'service_request_number', ''),
                CASE WHEN p.project_code ~* '^SR-' THEN p.project_code ELSE '' END
            ) AS service_request_number,
            pt.billable AS billable,
            COALESCE(pt.utilization_bucket, CASE WHEN pt.billable THEN 'billable' ELSE 'non_billable' END) AS utilization_bucket,
            COALESCE(pm.display_name, 'No PM assigned') AS project_manager_name,
            COALESCE(NULLIF(p.work_type, ''), 'Project') AS work_type,
            CASE
                WHEN p.project_code ~* '^(SR|PRES|INT)-'
                  OR regexp_replace(lower(COALESCE(p.work_type, '')), '[^a-z0-9]+', '', 'g') IN (
                      'servicerequest', 'sr', 'presales', 'presale', 'pres',
                      'internal', 'internalproject', 'internaltask'
                  )
                  OR NULLIF(to_jsonb(pt)->>'service_request_number', '') IS NOT NULL
                    THEN 'requests'
                ELSE 'regular'
            END AS time_entry_section,
''', 'request-family SQL')
s = one(s,
'''        var workTaskCategory = reader.GetString(O("work_task_category"));
        var serviceRequestNumber = reader.GetString(O("service_request_number"));
        var isServiceRequest = string.Equals(
                workTaskCategory.Trim(),
                "service_request_task",
                StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(serviceRequestNumber);

        tasks.Add(new
        {
''',
'''        var workTaskCategory = reader.GetString(O("work_task_category"));
        var serviceRequestNumber = reader.GetString(O("service_request_number"));
        var projectCode = reader.GetString(O("project_code"));
        var timeEntrySection = reader.GetString(O("time_entry_section"));
        var isRequestFamily = string.Equals(
            timeEntrySection,
            "requests",
            StringComparison.OrdinalIgnoreCase);

        tasks.Add(new
        {
''', 'request-family response authority')
s = one(s, '            projectCode = reader.GetString(O("project_code")),\n', '            projectCode,\n', 'project code reuse')
s = one(s,
'''            rowType = isServiceRequest ? "service_request" : "projectTask",
            workTaskCategory,
            serviceRequestNumber,
''',
'''            rowType = isRequestFamily ? "service_request" : "projectTask",
            workTaskCategory,
            requestNumber = isRequestFamily ? projectCode : string.Empty,
            serviceRequestNumber,
''', 'normalized request response')
s = one(s, '            timeEntrySection = reader.GetString(O("time_entry_section")),\n', '            timeEntrySection,\n', 'section reuse')
s = one(s,
'''        weekStart = start,
        weekEnd = end,
        count = tasks.Count,
        tasks
    });
});


app.MapGet("/api/non-project-time-categories"''',
'''        weekStart = start,
        weekEnd = end,
        count = tasks.Count,
        authoritativeSource = "project_assignments",
        activityClassification = "durable_project_code_and_work_type",
        tasks
    });
});


app.MapGet("/api/non-project-time-categories"''', 'available-task metadata')
save(p, s)

# Module 001A empty-200 repair.
p = 'src/backend/ProjectTime.Api/Modules/Module001AEngineerTaskCloseoutModule.cs'
s = load(p)
s = one(s,
'        app.MapGet("/api/engineer-task-closeout/overview", Module001AOverviewAsync);\n',
'''        app.MapGet(
            "/api/engineer-task-closeout/overview",
            (Func<HttpContext, Task<IResult>>)Module001AOverviewAsync);
''', 'Module 001A explicit IResult')
save(p, s)

# Timesheet request section classification.
p = 'src/frontend/project-time-web/src/App.jsx'
s = load(p)
s = one(s,
'''function projectPulseTaskTimeEntrySection(task = {}) {
  const explicitSection = String(task.timeEntrySection || task.time_entry_section || '').trim().toLowerCase();
  if (explicitSection === 'requests') return 'requests';
  if (explicitSection === 'regular') return 'regular';

  const workType = String(task.workType || task.work_type || 'Project')
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '');

  return workType === 'project' || workType === 'iqs' ? 'regular' : 'requests';
}
''',
'''function projectPulseTaskTimeEntrySection(task = {}) {
  const explicitSection = String(task.timeEntrySection || task.time_entry_section || '').trim().toLowerCase();
  const projectCode = String(task.projectCode || task.project_code || '').trim().toUpperCase();
  const serviceRequestNumber = String(
    task.serviceRequestNumber || task.service_request_number || ''
  ).trim();
  const requestNumber = String(task.requestNumber || task.request_number || '').trim();
  const workType = String(task.workType || task.work_type || 'Project')
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '');
  const requestWorkTypes = new Set([
    'servicerequest', 'sr', 'presales', 'presale', 'pres',
    'internal', 'internalproject', 'internaltask'
  ]);
  const isDurableRequestFamily = /^(SR|PRES|INT)-/.test(projectCode)
    || serviceRequestNumber.length > 0
    || requestNumber.length > 0
    || requestWorkTypes.has(workType);

  if (isDurableRequestFamily || explicitSection === 'requests') return 'requests';
  if (explicitSection === 'regular') return 'regular';
  return workType === 'project' || workType === 'iqs' ? 'regular' : 'requests';
}
''', 'Module 001 request classification')
save(p, s)

# Add compact regression coverage to the existing focused validator.
p = 'src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs'
s = load(p)
checks = r'''
const availableTaskProgram = read('src/backend/ProjectTime.Api/Program.cs');
const availableTaskStart = availableTaskProgram.indexOf('app.MapGet("/api/assignments/available-tasks"');
const availableTaskEnd = availableTaskProgram.indexOf('app.MapGet("/api/non-project-time-categories"', availableTaskStart);
const availableTaskEndpoint = availableTaskStart >= 0 && availableTaskEnd > availableTaskStart
  ? availableTaskProgram.slice(availableTaskStart, availableTaskEnd)
  : '';
[
  'DayOfWeek.Sunday',
  "p.project_code ~* '^(SR|PRES|INT)-'",
  'requestNumber = isRequestFamily ? projectCode : string.Empty',
  'authoritativeSource = "project_assignments"',
  'activityClassification = "durable_project_code_and_work_type"'
].forEach((contract) => requireText(availableTaskEndpoint, contract, 'Module 001 assigned-work endpoint'));
rejectText(availableTaskEndpoint, 'DayOfWeek.Monday', 'Module 001 Sunday week authority');

const timesheetUi = read('src/frontend/project-time-web/src/App.jsx');
[
  "const isDurableRequestFamily = /^(SR|PRES|INT)-/.test(projectCode)",
  "requestWorkTypes.has(workType)",
  "if (isDurableRequestFamily || explicitSection === 'requests') return 'requests';"
].forEach((contract) => requireText(timesheetUi, contract, 'Module 001 request-family UI'));

requireText(
  closeout,
  '(Func<HttpContext, Task<IResult>>)Module001AOverviewAsync',
  'Module 001A explicit IResult execution'
);
const workspaceUi = read('src/frontend/project-time-web/src/ProjectWorkspaceCenter.jsx');
requireText(workspaceUi, 'assignments.map((assignment)', 'Module 019 assignment rendering');
requireText(workspaceUi, '{assignment.projectCode}', 'Module 019 durable identifier rendering');

const assignedWorkUat = read('scripts/release-test/run-assigned-work-protected-test-uat.sh');
[
  'ASSIGNED_WORK_PROTECTED_TEST_UAT=PASS',
  'SR-8C81ACA3',
  '/api/assignments/available-tasks?weekStart=',
  '/api/timesheet/work-queue?weekStart=',
  '/api/engineer-task-closeout/overview',
  '/api/project-workspace/overview',
  'mutation:false',
  'productionMutation:false'
].forEach((contract) => requireText(assignedWorkUat, contract, 'assigned-work protected-Test UAT'));

const protectedTestController = read('.github/workflows/projectpulse-deploy-test.yml');
[
  'scripts/release-test/run-assigned-work-protected-test-uat.sh',
  'Run protected-Test assigned-work visibility UAT',
  'assignedWorkUat:true'
].forEach((contract) => requireText(protectedTestController, contract, 'assigned-work deployment controller'));
'''
s = one(s, '\nif (failures.length) {\n', '\n' + checks + '\nif (failures.length) {\n', 'focused validator insertion')
s = one(s,
"console.log('modules_019_001a_001_shared_assignment_authority=true');\n",
"console.log('modules_019_001a_001_shared_assignment_authority=true');\nconsole.log('timesheet_week_authority=sunday_through_saturday');\nconsole.log('request_family_classification=sr_pres_int_durable_identifiers');\nconsole.log('module001a_response_body=explicit_iresult');\nconsole.log('authenticated_assigned_work_uat=registered');\n",
'focused validator facts')
save(p, s)

# Focused CI paths and syntax gate.
p = '.github/workflows/module-loading-assignment-propagation-ci.yml'
s = load(p)
s = one(s,
"      - 'src/backend/ProjectTime.Api/Modules/Module001TimesheetEnhancementModule.cs'\n",
"      - 'src/backend/ProjectTime.Api/Program.cs'\n      - 'src/backend/ProjectTime.Api/Modules/Module001TimesheetEnhancementModule.cs'\n",
'focused Program path')
s = one(s,
"      - 'src/frontend/project-time-web/src/module-directory-authority.js'\n",
"      - 'src/frontend/project-time-web/src/App.jsx'\n      - 'src/frontend/project-time-web/src/ProjectWorkspaceCenter.jsx'\n      - 'src/frontend/project-time-web/src/module-directory-authority.js'\n",
'focused UI paths')
s = one(s,
"      - 'src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs'\n      - '.github/workflows/module-loading-assignment-propagation-ci.yml'\n",
"      - 'src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs'\n      - 'scripts/release-test/run-assigned-work-protected-test-uat.sh'\n      - '.github/workflows/projectpulse-deploy-test.yml'\n      - '.github/workflows/module-loading-assignment-propagation-ci.yml'\n",
'focused UAT paths')
s = one(s,
'''      - name: Validate focused source contracts
        run: node src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs

''',
'''      - name: Validate focused source contracts
        run: node src/frontend/project-time-web/scripts/validate-module-loading-assignment-propagation.mjs

      - name: Validate protected-Test assigned-work UAT syntax
        run: bash -n scripts/release-test/run-assigned-work-protected-test-uat.sh

''', 'focused UAT syntax')
save(p, s)

# Governed protected-Test controller wiring.
p = '.github/workflows/projectpulse-deploy-test.yml'
s = load(p)
s = one(s,
"      - 'scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh'\n",
"      - 'scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh'\n      - 'scripts/release-test/run-assigned-work-protected-test-uat.sh'\n",
'controller trigger')
s = one(s,
'            scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh \\\n',
'            scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh \\\n            scripts/release-test/run-assigned-work-protected-test-uat.sh \\\n',
'controller required file')
s = one(s,
'          bash -n scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh\n',
'          bash -n scripts/release-test/run-utilization-role-scoping-protected-test-uat.sh\n          bash -n scripts/release-test/run-assigned-work-protected-test-uat.sh\n',
'controller UAT syntax')
s = one(s,
'              utilizationRoleScopingUat:true,\n              applicationOnlyAfterMigration:true,\n',
'              utilizationRoleScopingUat:true,\n              assignedWorkUat:true,\n              applicationOnlyAfterMigration:true,\n',
'controller release boundary')
s = one(s,
'      - name: Run protected-Test utilization role-scoping UAT\n',
'''      - name: Run protected-Test assigned-work visibility UAT
        id: assigned_work_uat
        shell: bash
        working-directory: release
        env:
          TEST_LOGIN_PASSWORD: ${{ secrets.PROJECTPULSE_M087_PASSWORD }}
          BASE: ${{ steps.contract.outputs.base }}
          EVIDENCE_DIR: /tmp/systemwide-enterprise-reliability-test-evidence
        run: |
          set -Eeuo pipefail
          bash scripts/release-test/run-assigned-work-protected-test-uat.sh

      - name: Run protected-Test utilization role-scoping UAT
''', 'controller UAT step')
save(p, s)

print('ASSIGNED_WORK_FINALIZER_PATCH=PASS')
