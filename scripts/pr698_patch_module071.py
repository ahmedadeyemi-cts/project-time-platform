from pathlib import Path

ROOT = Path.cwd()
PAY_FORM = "https://forms.cloud.microsoft/Pages/ResponsePage.aspx?id=2kFZU3Lai0qDeJg6VL7DQtvfUo2dqAlEkjfnG3izqQFUQ0NXTlQ5TEtERzE0RzNHN0tNMjJNWThWRSQlQCN0PWcu"
PUBLIC_SCHEDULE = "https://oncall.onenecklab.com/"


def replace_once(path, old, new):
    file = ROOT / path
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected 1 match, found {count}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


backend = "src/backend/ProjectTime.Api/Modules/OnCallSchedulingModule.cs"
replace_once(backend,
    '    private const string ManagePermission = "MANAGE_ONCALL_SCHEDULE";\n',
    f'    private const string ManagePermission = "MANAGE_ONCALL_SCHEDULE";\n    private const string PublicScheduleUrl = "{PUBLIC_SCHEDULE}";\n    private const string OnCallPayFormUrl = "{PAY_FORM}";\n')
replace_once(backend,
'''                manage = new[]
                {
                    "SUPER_ADMINISTRATOR",
                    "ADMINISTRATOR",
                    "MANAGER",
                    "ENGINEERING_TEAM_LEAD"
                },
''',
'''                manage = new[]
                {
                    "SUPER_ADMINISTRATOR",
                    "ADMINISTRATOR",
                    "MANAGER",
                    "ENGINEERING_LEAD",
                    "ENGINEERING_TEAM_LEAD",
                    "PROJECT_TEAM_COORDINATOR"
                },
''')
replace_once(backend,
'''            publicApi = new[]
            {
                "/api/public/v1/oncall/current",
                "/api/public/v1/oncall/current?department=collaboration",
                "/api/public/v1/oncall/schedule"
            },
''',
'''            links = new
            {
                publicSchedule = PublicScheduleUrl,
                onCallPayForm = OnCallPayFormUrl,
                publicScheduleAuthenticationRequired = false,
                oneAssistPinAuthenticationRequired = true
            },
            publicApi = new[]
            {
                "/api/public/v1/oncall/current",
                "/api/public/v1/oncall/current?department=collaboration",
                "/api/public/v1/oncall/schedule"
            },
''')
replace_once(backend,
'''                      'ENGINEER',
                      'ENGINEERING',
                      'ENGINEERING_MANAGER',
                      'ENGINEERING_TEAM_LEAD',
                      'MANAGER'
''',
'''                      'ENGINEER',
                      'ENGINEERING',
                      'ENGINEERING_LEAD',
                      'ENGINEERING_MANAGER',
                      'ENGINEERING_TEAM_LEAD',
                      'MANAGER'
''')
replace_once(backend,
'''            var canManage =
                roles.Contains("SUPER_ADMINISTRATOR")
                || roles.Contains("ADMINISTRATOR")
                || roles.Contains("MANAGER")
                || roles.Contains("ENGINEERING_TEAM_LEAD");
''',
'''            var canManage = !IsViewAs(context) && roles.Overlaps(new[]
            {
                "SUPER_ADMINISTRATOR",
                "ADMINISTRATOR",
                "MANAGER",
                "ENGINEERING_LEAD",
                "ENGINEERING_TEAM_LEAD",
                "PROJECT_TEAM_COORDINATOR"
            });
''')
replace_once(backend,
    '                    message = "Only Super Administrators, Administrators, Managers, and Engineering Team Leads can manage the on-call schedule."\n',
    '                    message = "Only Managers, Project Team Coordinators, Engineering Leads, and platform administrators can manage the on-call schedule."\n')

frontend = "src/frontend/project-time-web/src/OnCallSchedulingCenter.jsx"
replace_once(frontend,
    "const DEFAULT_DEPARTMENTS = ['enterprise_network', 'collaboration', 'system_storage'];\n",
    f"const DEFAULT_DEPARTMENTS = ['enterprise_network', 'collaboration', 'system_storage'];\nconst PUBLIC_ONCALL_SCHEDULE_URL = '{PUBLIC_SCHEDULE}';\nconst ONCALL_PAY_FORM_URL = '{PAY_FORM}';\nconst ONEASSIST_ROUTE_HASH = '#oneassist-routing-directory';\n")
replace_once(frontend,
'''        <div className="oncall-authority">
          <span>{canManage ? 'Schedule manager' : 'Read-only viewer'}</span>
          <small>{canManage ? 'Super Administrator / Administrator / Manager / Engineering Team Lead' : 'All ProjectPulse users can view'}</small>
        </div>
''',
'''        <div className="oncall-hero-tools">
          <div className="oncall-authority">
            <span>{canManage ? 'Schedule manager' : 'Read-only viewer'}</span>
            <small>{canManage ? 'Manager / PTC / Engineering Lead / Platform Administrator' : 'All authenticated Pulse users can view'}</small>
          </div>
          <div className="oncall-quick-links" aria-label="On-call resources">
            <a href={ONCALL_PAY_FORM_URL} target="_blank" rel="noreferrer">OnCall Pay Form</a>
            <a href={PUBLIC_ONCALL_SCHEDULE_URL} target="_blank" rel="noreferrer">Public On-Call Schedule</a>
            <a href={ONEASSIST_ROUTE_HASH}>OneAssist PINs</a>
          </div>
        </div>
''')
replace_once(frontend,
'''      <div className="oncall-banner governed">
        Schedule, roster, and history changes are stored in the ProjectPulse PostgreSQL application database with actual-session audit evidence.
      </div>
''',
'''      <div className="oncall-banner governed">
        Schedule, roster, and history changes are stored in the ProjectPulse PostgreSQL application database with actual-session audit evidence.
      </div>
      <div className="oncall-banner governed">
        The public schedule link requires no sign-in and is the link used in on-call reminders. OneAssist PINs are never shown on that public page and remain available only after Pulse authentication.
      </div>
''')
replace_once(frontend,
    'Only platform administrators, Managers, and Engineering Team Leads can generate rotations.',
    'Only Managers, Project Team Coordinators, Engineering Leads, and platform administrators can generate rotations.')
replace_once(frontend,
'''          <div className="oncall-card-head"><div><p className="oncall-eyebrow">Read-only routing contract</p><h2>Public On-Call API</h2></div><span className="oncall-live">Version 1</span></div>
          <code>GET /api/public/v1/oncall/current</code>
          <code>GET /api/public/v1/oncall/current?department=collaboration</code>
          <code>GET /api/public/v1/oncall/schedule</code>
          <p>Public routes expose current routing assignments only. Schedule and roster mutations remain protected by the Manager and Engineering Team Lead permission boundary.</p>
''',
'''          <div className="oncall-card-head"><div><p className="oncall-eyebrow">No sign-in required</p><h2>Public On-Call Schedule</h2></div><span className="oncall-live">Version 1</span></div>
          <code>{PUBLIC_ONCALL_SCHEDULE_URL}</code>
          <code>GET /api/public/v1/oncall/current</code>
          <code>GET /api/public/v1/oncall/current?department=collaboration</code>
          <code>GET /api/public/v1/oncall/schedule</code>
          <p>The direct schedule and public APIs expose on-call assignments only. They never expose OneAssist PINs. Schedule changes remain protected by the Manager, PTC, Engineering Lead, and platform-administrator boundary.</p>
''')

css = ROOT / "src/frontend/project-time-web/src/oncall-scheduling-center.css"
text = css.read_text(encoding="utf-8")
if "PR698_ONCALL_RESOURCE_LINKS" not in text:
    text += '''\n/* PR698_ONCALL_RESOURCE_LINKS */
.oncall-hero-tools { display: grid; gap: .65rem; justify-items: end; min-width: 260px; }
.oncall-quick-links { display: flex; flex-wrap: wrap; justify-content: flex-end; gap: .45rem; }
.oncall-quick-links a { display: inline-flex; align-items: center; min-height: 38px; padding: .55rem .72rem; border: 1px solid rgba(0, 75, 141, .28); border-radius: .62rem; background: rgba(255,255,255,.84); color: var(--oncall-blue-strong); font-size: .78rem; font-weight: 850; text-decoration: none; }
.oncall-quick-links a:hover, .oncall-quick-links a:focus-visible { border-color: var(--oncall-cyan); outline: 3px solid rgba(0,167,200,.2); outline-offset: 2px; }
[data-theme='dark'] .oncall-quick-links a { background: rgba(16,34,57,.92); color: #9ed3ff; }
@media (max-width: 1050px) { .oncall-hero-tools { width: 100%; min-width: 0; justify-items: start; } .oncall-quick-links { justify-content: flex-start; } }
@media (max-width: 700px) { .oncall-quick-links { display: grid; width: 100%; } .oncall-quick-links a { justify-content: center; } }
'''
    css.write_text(text, encoding="utf-8")

validator = "src/frontend/project-time-web/scripts/validate-module-071-oncall-scheduling.mjs"
replace_once(validator,
    "check('PUBLIC_APIS', backend.includes('/api/public/v1/oncall/current') && backend.includes('/api/public/v1/oncall/schedule'), 'public read APIs preserved');",
    f"check('PUBLIC_APIS', backend.includes('/api/public/v1/oncall/current') && backend.includes('/api/public/v1/oncall/schedule') && frontend.includes('{PUBLIC_SCHEDULE}') && frontend.includes('OnCall Pay Form'), 'public schedule and exact pay-form action preserved');")
replace_once(validator,
    "check('PLATFORM_ADMIN_ROLES', backend.includes('SUPER_ADMINISTRATOR') && backend.includes('ADMINISTRATOR') && backend.includes('MANAGER') && backend.includes('ENGINEERING_TEAM_LEAD'), 'approved management roles');",
    "check('MANAGEMENT_ROLES', backend.includes('SUPER_ADMINISTRATOR') && backend.includes('MANAGER') && backend.includes('ENGINEERING_LEAD') && backend.includes('ENGINEERING_TEAM_LEAD') && backend.includes('PROJECT_TEAM_COORDINATOR'), 'Manager, PTC, Engineering Lead, compatibility, and platform administrator roles');")

print("PR698_MODULE071_PATCH=PASS")
