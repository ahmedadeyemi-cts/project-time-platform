from pathlib import Path
import re

ROOT = Path.cwd()


def replace_once(path, old, new):
    file = ROOT / path
    text = file.read_text(encoding="utf-8")
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected 1 match, found {count}")
    file.write_text(text.replace(old, new, 1), encoding="utf-8")


def regex_once(path, pattern, replacement):
    file = ROOT / path
    text = file.read_text(encoding="utf-8")
    updated, count = re.subn(pattern, replacement, text, count=1, flags=re.S)
    if count != 1:
        raise RuntimeError(f"{path}: expected 1 regex match, found {count}")
    file.write_text(updated, encoding="utf-8")


backend = "src/backend/ProjectTime.Api/Modules/OneAssistRoutingDirectoryModule.cs"
replace_once(backend,
'''/// PINs are intentionally visible identifiers, not authentication secrets.
/// Everyone can read them; only the confirmed manager, administrator, and PTC
/// roles can edit them.
''',
'''/// PINs are visible routing identifiers, not authentication secrets.
/// They are available only after Pulse authentication. Managers, Project Team
/// Coordinators, Engineering Leads, and platform administrators can edit them.
''')
replace_once(backend,
'''        app.MapGet(
            "/api/public/v1/oneassist/routes",
            (Func<HttpContext, Task<IResult>>)GetPublicRoutesAsync);
        app.MapGet(
            "/api/public/v1/oneassist/resolve",
            (Func<string?, HttpContext, Task<IResult>>)ResolvePublicPinAsync);
''',
'')
replace_once(backend,
'''            dataClassification = new
            {
                pinClassification = "public_routing_identifier",
                masked = false,
                visibleToAllAuthenticatedUsers = true,
                publicApiEnabled = true,
                authenticationCredential = false
            },
''',
'''            dataClassification = new
            {
                pinClassification = "internal_routing_identifier",
                masked = false,
                visibleToAllAuthenticatedUsers = true,
                publicApiEnabled = false,
                authenticationRequired = true,
                authenticationCredential = false
            },
''')
replace_once(backend,
'''                view = "everyone",
                manage = new[]
                {
                    "MANAGER",
                    "ADMINISTRATOR",
                    "SUPER_ADMINISTRATOR",
                    "PROJECT_TEAM_COORDINATOR"
                },
''',
'''                view = "all authenticated Pulse users",
                manage = new[]
                {
                    "MANAGER",
                    "ENGINEERING_LEAD",
                    "ENGINEERING_TEAM_LEAD",
                    "PROJECT_TEAM_COORDINATOR",
                    "ADMINISTRATOR",
                    "SUPER_ADMINISTRATOR"
                },
''')
replace_once(backend,
'''            publicApi = new[]
            {
                "/api/public/v1/oneassist/routes",
                "/api/public/v1/oneassist/resolve?pin=12345"
            },
''',
'''            authenticatedApi = new[]
            {
                "/api/oneassist/routes"
            },
''')
replace_once(backend,
    '            pinVisibility = "visible_unmasked",\n',
    '            pinVisibility = "authenticated_visible_unmasked",\n')
regex_once(backend,
    r'\n    private static async Task<IResult> GetPublicRoutesAsync\(HttpContext context\).*?\n    private static JsonArray NormalizeRoutes',
    '\n    private static JsonArray NormalizeRoutes')
replace_once(backend,
'''            var canManage = roles.Overlaps(new[]
            {
                "MANAGER",
                "ADMINISTRATOR",
                "SUPER_ADMINISTRATOR",
                "PROJECT_TEAM_COORDINATOR"
            });
''',
'''            var canManage = !IsViewAs(context) && roles.Overlaps(new[]
            {
                "MANAGER",
                "ENGINEERING_LEAD",
                "ENGINEERING_TEAM_LEAD",
                "PROJECT_TEAM_COORDINATOR",
                "ADMINISTRATOR",
                "SUPER_ADMINISTRATOR"
            });
''')
replace_once(backend,
    '                    message = "Only Super Administrators, Administrators, Managers, and Project Team Coordinators can edit OneAssist routing PINs."\n',
    '                    message = "Only Managers, Project Team Coordinators, Engineering Leads, and platform administrators can edit OneAssist routing PINs."\n')
regex_once(backend,
    r'\n    private static void SetPublicHeaders\(HttpContext context\)\n    \{.*?\n    \}\n',
    '\n')

frontend = "src/frontend/project-time-web/src/OneAssistRoutingDirectoryCenter.jsx"
replace_once(frontend, 'data-pin-visibility="public-unmasked"', 'data-pin-visibility="authenticated-unmasked"')
replace_once(frontend,
    'Visible five-digit customer routing identifiers for engineers, coordinators, integrations, and public routing clients.',
    'Visible five-digit customer routing identifiers for authenticated engineers, coordinators, and approved integrations.')
replace_once(frontend,
    "{canManage ? 'Super Administrator / Administrator / Manager / PTC' : 'Everyone can view routing PINs'}",
    "{canManage ? 'Manager / PTC / Engineering Lead / Platform Administrator' : 'All authenticated Pulse users can view routing PINs'}")
replace_once(frontend,
    'OneAssist PINs are public routing identifiers and are intentionally displayed without masking. They must never be accepted as proof of identity.',
    'OneAssist PINs are internal routing identifiers displayed without masking only after Pulse authentication. They must never be accepted as proof of identity.')
replace_once(frontend, '>Public API</button>', '>Authenticated API</button>')
replace_once(frontend, 'Public routing data', 'Authenticated routing data')
replace_once(frontend,
    'Only platform administrators, Managers, and Project Team Coordinators can import directory changes.',
    'Only Managers, Project Team Coordinators, Engineering Leads, and platform administrators can import directory changes.')
replace_once(frontend,
'''          <div className="oneassist-card-head"><div><p className="oneassist-eyebrow">Versioned read-only contract</p><h2>Public routing API</h2></div><span className="oneassist-live">Version 1</span></div>
          <code>GET /api/public/v1/oneassist/routes</code>
          <code>GET /api/public/v1/oneassist/resolve?pin=12345</code>
          <p>The public API intentionally returns visible routing PINs and customer routing identities. It exposes no add, edit, delete, import, or save operation.</p>
''',
'''          <div className="oneassist-card-head"><div><p className="oneassist-eyebrow">Session-protected read contract</p><h2>Authenticated routing API</h2></div><span className="oneassist-live">Version 1</span></div>
          <code>GET /api/oneassist/routes</code>
          <p>The authenticated API returns visible routing PINs only to signed-in Pulse users. It exposes no unauthenticated PIN route; add, edit, delete, import, and save remain limited to approved editors.</p>
''')

validator = "src/frontend/project-time-web/scripts/validate-module-072-oneassist-routing-directory.mjs"
replace_once(validator,
    "check('PUBLIC_APIS', backend.includes('/api/public/v1/oneassist/routes') && backend.includes('/api/public/v1/oneassist/resolve'), 'public read APIs preserved');",
    "check('AUTHENTICATED_READ_ONLY', !backend.includes('/api/public/v1/oneassist/routes') && !backend.includes('/api/public/v1/oneassist/resolve') && backend.includes('authenticationRequired = true'), 'anonymous PIN APIs removed');")
replace_once(validator,
    "check('PLATFORM_ADMIN_ROLES', backend.includes('SUPER_ADMINISTRATOR') && backend.includes('ADMINISTRATOR') && backend.includes('MANAGER') && backend.includes('PROJECT_TEAM_COORDINATOR'), 'approved management roles');",
    "check('MANAGEMENT_ROLES', backend.includes('SUPER_ADMINISTRATOR') && backend.includes('MANAGER') && backend.includes('ENGINEERING_LEAD') && backend.includes('ENGINEERING_TEAM_LEAD') && backend.includes('PROJECT_TEAM_COORDINATOR'), 'Manager, PTC, Engineering Lead, compatibility, and platform administrator roles');")
replace_once(validator,
    "check('FRONTEND_NATIVE', frontend.includes('data-persistence=\"projectpulse-postgresql\"') && frontend.includes('ProjectPulse PostgreSQL application database'), 'native storage visible');",
    "check('FRONTEND_NATIVE', frontend.includes('data-persistence=\"projectpulse-postgresql\"') && frontend.includes('ProjectPulse PostgreSQL application database') && frontend.includes('authenticated-unmasked'), 'native authenticated storage visible');")

print("PR698_MODULE072_PATCH=PASS")
