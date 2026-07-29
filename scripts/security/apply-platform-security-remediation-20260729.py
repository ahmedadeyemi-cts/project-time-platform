#!/usr/bin/env python3
"""Apply the reviewed 2026-07-29 platform security remediation set.

The script is intentionally deterministic and fail-closed: every replacement must
match exactly once unless the remediated marker is already present. It is executed
by the branch-only security workflow and leaves the canonical source reviewable.
"""

from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def write(relative: str, text: str) -> None:
    (ROOT / relative).write_text(text, encoding="utf-8")


def replace_once(relative: str, old: str, new: str, marker: str | None = None) -> None:
    text = read(relative)
    if marker and marker in text:
        return
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{relative}: expected exactly one match, found {count}: {old[:120]!r}")
    write(relative, text.replace(old, new, 1))


def replace_all(relative: str, old: str, new: str, minimum: int = 1) -> None:
    text = read(relative)
    count = text.count(old)
    if count < minimum:
        raise RuntimeError(f"{relative}: expected at least {minimum} matches, found {count}: {old!r}")
    write(relative, text.replace(old, new))


def require(relative: str, required: tuple[str, ...] = (), forbidden: tuple[str, ...] = ()) -> None:
    text = read(relative)
    for value in required:
        if value not in text:
            raise RuntimeError(f"{relative}: required security marker missing: {value!r}")
    for value in forbidden:
        if value in text:
            raise RuntimeError(f"{relative}: forbidden vulnerable pattern remains: {value!r}")


PROGRAM = "src/backend/ProjectTime.Api/Program.cs"
SECURITY_MODULE = "src/backend/ProjectTime.Api/Modules/SecurityHardeningModule.cs"

# ---------------------------------------------------------------------------
# Canonical API source hardening
# ---------------------------------------------------------------------------
replace_once(
    PROGRAM,
    "app.UseWorkRegisterAuthorization();",
    "app.UseProjectPulseSecurityHardening();\napp.UseWorkRegisterAuthorization();",
    marker="app.UseProjectPulseSecurityHardening();",
)

replace_once(
    PROGRAM,
    '''    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase))''',
    '''    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))''',
    marker="/* SECURITY_20260729_VIEW_AS_ALL_WRITES_BLOCKED */",
)
replace_once(
    PROGRAM,
    '''    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
    {''',
    '''    /* SECURITY_20260729_VIEW_AS_ALL_WRITES_BLOCKED */
    if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
    {''',
    marker="/* SECURITY_20260729_VIEW_AS_ALL_WRITES_BLOCKED */",
)

replace_once(
    PROGRAM,
    'var email = ProjectPulseJsonString(payload, "email") ?? preferredUsername ?? requestedEmail ?? "";',
    'var email = ProjectPulseJsonString(payload, "email") ?? preferredUsername ?? ""; // SECURITY_20260729_NO_LOGIN_HINT_IDENTITY_FALLBACK',
    marker="SECURITY_20260729_NO_LOGIN_HINT_IDENTITY_FALLBACK",
)

replace_once(
    PROGRAM,
    'var storedFileName = $"{documentType}_{Guid.NewGuid():N}{Path.GetExtension(safeOriginalFileName)}";',
    'var storedFileName = $"{documentCategory}_{Guid.NewGuid():N}{Path.GetExtension(safeOriginalFileName)}"; // SECURITY_20260729_SAFE_DOCUMENT_PATH_COMPONENT',
    marker="SECURITY_20260729_SAFE_DOCUMENT_PATH_COMPONENT",
)

replace_once(
    PROGRAM,
    '''    var actionUrl = root.TryGetProperty("actionUrl", out var actionElement) ? actionElement.GetString() ?? "" : "";

    var targetRoleCodes = new List<string>();''',
    '''    var actionUrl = root.TryGetProperty("actionUrl", out var actionElement) ? actionElement.GetString() ?? "" : "";

    /* SECURITY_20260729_NOTIFICATION_URL_ALLOWLIST */
    if (!SecurityHardeningModule.IsSafeActionUrl(actionUrl))
    {
        return Results.Json(new
        {
            status = "unsafe_url_rejected",
            message = "Only same-origin relative routes or explicit HTTPS destinations are allowed."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    var targetRoleCodes = new List<string>();''',
    marker="SECURITY_20260729_NOTIFICATION_URL_ALLOWLIST",
)

replace_once(
    PROGRAM,
    '''    var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Add("X-ProjectPulse-Session", httpContext.Request.Headers["X-ProjectPulse-Session"].FirstOrDefault() ?? "");

    var previewJson = await client.GetStringAsync(baseUrl + previewUrl);''',
    '''    /* SECURITY_20260729_FIXED_INTERNAL_API_ORIGIN */
    var internalApiBaseUrl = Environment.GetEnvironmentVariable("PROJECTPULSE_INTERNAL_API_BASE_URL")
        ?? "http://127.0.0.1:5080";

    if (!Uri.TryCreate(internalApiBaseUrl, UriKind.Absolute, out var internalApiBaseUri)
        || (internalApiBaseUri.Scheme != Uri.UriSchemeHttp && internalApiBaseUri.Scheme != Uri.UriSchemeHttps)
        || !string.IsNullOrEmpty(internalApiBaseUri.UserInfo))
    {
        return Results.Json(new
        {
            status = "internal_api_origin_invalid",
            message = "The configured internal API origin is invalid."
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    using var client = new HttpClient();
    client.DefaultRequestHeaders.Add("X-ProjectPulse-Session", httpContext.Request.Headers["X-ProjectPulse-Session"].FirstOrDefault() ?? "");

    var previewJson = await client.GetStringAsync(new Uri(internalApiBaseUri, previewUrl));''',
    marker="SECURITY_20260729_FIXED_INTERNAL_API_ORIGIN",
)

replace_once(
    PROGRAM,
    '''static string ProjectPulse041ABuildRawEmail(
    System.Collections.Generic.List<(string Role, string Name, string Email)> recipients,''',
    '''static string ProjectPulse041ASanitizeHeaderValue(string? value)
{
    return System.Text.RegularExpressions.Regex
        .Replace(value ?? string.Empty, @"[\\r\\n]+", " ")
        .Trim();
}

static string ProjectPulse041ABuildRawEmail(
    System.Collections.Generic.List<(string Role, string Name, string Email)> recipients,''',
    marker="static string ProjectPulse041ASanitizeHeaderValue",
)

for old, new in (
    ('builder.AppendLine($"From: {from}");', 'builder.AppendLine($"From: {ProjectPulse041ASanitizeHeaderValue(from)}");'),
    (
        'builder.AppendLine($"To: {string.Join(", ", recipients.Select(recipient => recipient.Email))}");',
        'builder.AppendLine($"To: {string.Join(", ", recipients.Select(recipient => ProjectPulse041ASanitizeHeaderValue(recipient.Email)))}");',
    ),
    (
        'builder.AppendLine($"Cc: {string.Join(", ", ccRecipients.Select(recipient => recipient.Email))}");',
        'builder.AppendLine($"Cc: {string.Join(", ", ccRecipients.Select(recipient => ProjectPulse041ASanitizeHeaderValue(recipient.Email)))}");',
    ),
    ('builder.AppendLine($"Subject: {subject}");', 'builder.AppendLine($"Subject: {ProjectPulse041ASanitizeHeaderValue(subject)}");'),
):
    replace_once(PROGRAM, old, new, marker=new)

replace_once(
    PROGRAM,
    '''    text = text.Replace("\\r", " ").Replace("\\n", " ").Replace("\\\"", "\\\"\\\"");
    return $"\\\"{text}\\\"";''',
    '''    text = text.Replace("\\r", " ").Replace("\\n", " ").Replace("\\\"", "\\\"\\\"");

    /* SECURITY_20260729_CSV_FORMULA_NEUTRALIZATION */
    var formulaCandidate = text.TrimStart();
    if (formulaCandidate.StartsWith("=", StringComparison.Ordinal)
        || formulaCandidate.StartsWith("+", StringComparison.Ordinal)
        || formulaCandidate.StartsWith("-", StringComparison.Ordinal)
        || formulaCandidate.StartsWith("@", StringComparison.Ordinal))
    {
        text = "'" + text;
    }

    return $"\\\"{text}\\\"";''',
    marker="SECURITY_20260729_CSV_FORMULA_NEUTRALIZATION",
)

# Explicit source-level authorization at the highest-risk legacy endpoints.
replace_once(
    PROGRAM,
    '''app.MapGet("/api/admin/users", async () =>
{
    var config = DatabaseConfig.FromEnvironment();''',
    '''app.MapGet("/api/admin/users", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();''',
    marker='app.MapGet("/api/admin/users", async (HttpContext httpContext)',
)
replace_once(
    PROGRAM,
    '''app.MapGet("/api/admin/users", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var users = new List<object>();''',
    '''app.MapGet("/api/admin/users", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    /* SECURITY_20260729_ADMIN_USER_DIRECTORY */
    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new
        {
            status = "admin_required",
            message = "The user directory is restricted to Administrators and Super Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var users = new List<object>();''',
    marker="SECURITY_20260729_ADMIN_USER_DIRECTORY",
)

replace_once(
    PROGRAM,
    '''app.MapGet("/api/auth/local-accounts", async () =>
{
    var config = DatabaseConfig.FromEnvironment();''',
    '''app.MapGet("/api/auth/local-accounts", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();''',
    marker='app.MapGet("/api/auth/local-accounts", async (HttpContext httpContext)',
)
replace_once(
    PROGRAM,
    '''app.MapGet("/api/auth/local-accounts", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    var accounts = new List<object>();''',
    '''app.MapGet("/api/auth/local-accounts", async (HttpContext httpContext) =>
{
    var config = DatabaseConfig.FromEnvironment();
    var missingResult = ValidateConfig(config);
    if (missingResult is not null) return missingResult;

    await using var connection = new NpgsqlConnection(config.ConnectionString);
    await connection.OpenAsync();

    /* SECURITY_20260729_ADMIN_LOCAL_ACCOUNT_DIRECTORY */
    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new
        {
            status = "admin_required",
            message = "Local account administration is restricted to Administrators and Super Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var accounts = new List<object>();''',
    marker="SECURITY_20260729_ADMIN_LOCAL_ACCOUNT_DIRECTORY",
)

replace_once(
    PROGRAM,
    '''        var adminUserId = sessionAdminUserId.Value;
        Guid targetUserId;''',
    '''        var adminUserId = sessionAdminUserId.Value;

        /* SECURITY_20260729_LEGACY_ROLE_ASSIGNMENT_AUTHORITY */
        if (!await RequestUserIsAdministratorAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "admin_required",
                message = "Role assignment is restricted to Administrators and Super Administrators."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (roleCodes.Contains("SUPER_ADMINISTRATOR", StringComparer.OrdinalIgnoreCase)
            && !await ProjectPulseViewAsUserHasRoleAsync(connection, adminUserId, "SUPER_ADMINISTRATOR"))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "super_administrator_required",
                message = "Only a Super Administrator can grant the Super Administrator role."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        await using (var roleValidationCommand = new NpgsqlCommand("""
            SELECT COUNT(*)
            FROM app_roles
            WHERE is_active = TRUE
              AND role_code = ANY(@role_codes);
            """, connection, transaction))
        {
            roleValidationCommand.Parameters.AddWithValue("role_codes", roleCodes);
            var knownRoleCount = Convert.ToInt32(await roleValidationCommand.ExecuteScalarAsync() ?? 0);
            if (knownRoleCount != roleCodes.Length)
            {
                await transaction.RollbackAsync();
                return Results.BadRequest(new
                {
                    status = "unknown_role_code",
                    message = "One or more supplied role codes are unknown or inactive."
                });
            }
        }

        Guid targetUserId;''',
    marker="SECURITY_20260729_LEGACY_ROLE_ASSIGNMENT_AUTHORITY",
)

replace_once(
    PROGRAM,
    '''    if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
    {
        return Results.Json(new { status = "access_denied", message = "User Administration is restricted to administrators and project/team coordinators." }, statusCode: StatusCodes.Status403Forbidden);
    }

    var passwordHash = HashProjectPulsePassword(request.TemporaryPassword);''',
    '''    /* SECURITY_20260729_LOCAL_PASSWORD_ADMIN_AND_BREAK_GLASS */
    if (!await RequestUserIsAdministratorAsync(httpContext, connection))
    {
        return Results.Json(new
        {
            status = "admin_required",
            message = "Local password changes are restricted to Administrators and Super Administrators."
        }, statusCode: StatusCodes.Status403Forbidden);
    }

    var breakGlassAccount = Environment.GetEnvironmentVariable("PROJECTPULSE_BREAK_GLASS_ACCOUNT")
        ?? "ahmed.adeyemi@ussignal.local";

    await using (var breakGlassCommand = new NpgsqlCommand("""
        SELECT email
        FROM app_users
        WHERE user_id = @user_id
        LIMIT 1;
        """, connection))
    {
        breakGlassCommand.Parameters.AddWithValue("user_id", request.UserId);
        var targetEmail = (await breakGlassCommand.ExecuteScalarAsync())?.ToString() ?? string.Empty;

        if (targetEmail.Equals(breakGlassAccount, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(new
            {
                status = "break_glass_password_protected",
                message = "The break-glass account password can only be changed through the approved offline recovery procedure."
            }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    var passwordHash = HashProjectPulsePassword(request.TemporaryPassword);''',
    marker="SECURITY_20260729_LOCAL_PASSWORD_ADMIN_AND_BREAK_GLASS",
)

replace_once(
    PROGRAM,
    '''        var userId = sessionUserId.Value;
        Guid batchId;''',
    '''        var userId = sessionUserId.Value;

        /* SECURITY_20260729_HOLIDAY_IMPORT_AUTHORITY */
        if (!await RequestUserCanAccessUserAdministrationAsync(httpContext, connection))
        {
            await transaction.RollbackAsync();
            return Results.Json(new
            {
                status = "access_denied",
                message = "Holiday imports are restricted to Administrators and Project Team Coordinators."
            }, statusCode: StatusCodes.Status403Forbidden);
        }

        Guid batchId;''',
    marker="SECURITY_20260729_HOLIDAY_IMPORT_AUTHORITY",
)

# Refine the centralized policy so organization-wide PII and intake-document access
# are not granted merely because a user has a manager or intake-facing role.
replace_once(
    SECURITY_MODULE,
    '''            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Roles.Contains("MANAGER")
            || Permissions.Overlaps(new[]''',
    '''            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Permissions.Overlaps(new[]''',
    marker="SECURITY_20260729_TIME_COMPLIANCE_ORG_SCOPE",
)
replace_once(
    SECURITY_MODULE,
    '''        public bool CanViewTimeCompliance =>
            IsAdministrator''',
    '''        // SECURITY_20260729_TIME_COMPLIANCE_ORG_SCOPE
        public bool CanViewTimeCompliance =>
            IsAdministrator''',
    marker="SECURITY_20260729_TIME_COMPLIANCE_ORG_SCOPE",
)
replace_once(
    SECURITY_MODULE,
    '''        if (access.CanUseProjectIntake)
        {
            return true;
        }''',
    '''        if (access.HasOrganizationIntakeScope)
        {
            return true;
        }''',
    marker="HasOrganizationIntakeScope",
)
replace_once(
    SECURITY_MODULE,
    '''        public bool HasOrganizationProjectScope =>
            IsAdministrator''',
    '''        public bool HasOrganizationIntakeScope =>
            IsAdministrator
            || Roles.Contains("PROJECT_TEAM_COORDINATOR")
            || Permissions.Overlaps(new[]
            {
                "MANAGE_PROJECT_INTAKE",
                "MANAGE_PROJECT_DOCUMENTS",
                "SYSTEM_ADMINISTRATION",
                "MANAGE_ALL"
            });

        public bool HasOrganizationProjectScope =>
            IsAdministrator''',
    marker="public bool HasOrganizationIntakeScope",
)

# ---------------------------------------------------------------------------
# Browser URL safety
# ---------------------------------------------------------------------------
INDEX_HTML = "src/frontend/project-time-web/index.html"
replace_once(
    INDEX_HTML,
    '''  const renderBody = () => {''',
    '''  const projectPulse022DSafeActionUrl = (value) => {
    const candidate = String(value || "").trim();
    if (!candidate) return "";

    if (candidate.startsWith("/") && !candidate.startsWith("//") && !candidate.includes("\\\\") && !/[\\u0000-\\u001f]/.test(candidate)) {
      return candidate;
    }

    try {
      const parsed = new URL(candidate);
      if (parsed.protocol === "https:" && !parsed.username && !parsed.password) {
        return parsed.href;
      }
    } catch (_) {
      return "";
    }

    return "";
  };

  const renderBody = () => {''',
    marker="projectPulse022DSafeActionUrl",
)
replace_once(
    INDEX_HTML,
    '${item.actionUrl ? `<a class="projectpulse-022d-button" href="${escapeHtml(item.actionUrl)}">Open related page</a>` : ""}',
    '${projectPulse022DSafeActionUrl(item.actionUrl) ? `<a class="projectpulse-022d-button" href="${escapeHtml(projectPulse022DSafeActionUrl(item.actionUrl))}" rel="noopener noreferrer">Open related page</a>` : ""}',
    marker='href="${escapeHtml(projectPulse022DSafeActionUrl(item.actionUrl))}"',
)

WORK_REGISTER = "src/frontend/project-time-web/src/WorkRegisterCenter.jsx"
replace_once(
    WORK_REGISTER,
    '''    if (reference) {
      window.open(reference, '_blank', 'noopener,noreferrer');
    }''',
    '''    if (reference) {
      let safeReference = '';
      const candidate = String(reference).trim();

      if (candidate.startsWith('/') && !candidate.startsWith('//') && !candidate.includes('\\\\') && !/[\\u0000-\\u001f]/.test(candidate)) {
        safeReference = candidate;
      } else {
        try {
          const parsed = new URL(candidate);
          if (parsed.protocol === 'https:' && !parsed.username && !parsed.password) {
            safeReference = parsed.href;
          }
        } catch (_) {
          safeReference = '';
        }
      }

      if (!safeReference) {
        setDocumentStatus('Blocked an unsafe document reference. Only relative Project Pulse routes and HTTPS links are allowed.');
        return;
      }

      window.open(safeReference, '_blank', 'noopener,noreferrer');
    }''',
    marker="Blocked an unsafe document reference",
)

# ---------------------------------------------------------------------------
# Operations and deployment hardening
# ---------------------------------------------------------------------------
SYNC_EXPORT = "ops/projectpulse/scripts/projectpulse-sync-status-export.sh"
replace_once(
    SYNC_EXPORT,
    '''if peer_host:
    ping = run(["bash", "-lc", f"timeout 5 bash -c '</dev/tcp/{peer_host}/22'"], timeout=7)
    peer["status"] = "ready" if ping["ok"] else "warning"
    peer["detail"] = "Peer host is reachable on TCP/22." if ping["ok"] else "Peer host is configured but not reachable on TCP/22."''',
    '''if peer_host:
    try:
        with socket.create_connection((peer_host, 22), timeout=5):
            peer_reachable = True
    except (OSError, ValueError):
        peer_reachable = False

    peer["status"] = "ready" if peer_reachable else "warning"
    peer["detail"] = "Peer host is reachable on TCP/22." if peer_reachable else "Peer host is configured but not reachable on TCP/22."''',
    marker="socket.create_connection((peer_host, 22)",
)

RESTORE_EXPORT = "ops/projectpulse/scripts/projectpulse-restore-validation-export.sh"
replace_once(
    RESTORE_EXPORT,
    '''def safe_members(tar):
    unsafe = []
    for member in tar.getmembers():
        name = member.name
        if name.startswith("/") or ".." in Path(name).parts:
            unsafe.append(name)
    return unsafe''',
    '''def unsafe_members(tar, destination):
    root = destination.resolve()
    unsafe = []

    for member in tar.getmembers():
        name = member.name
        member_path = Path(name)

        if (
            member_path.is_absolute()
            or ".." in member_path.parts
            or member.issym()
            or member.islnk()
            or member.isdev()
            or member.isfifo()
        ):
            unsafe.append(name)
            continue

        target = (root / member_path).resolve()
        if target != root and root not in target.parents:
            unsafe.append(name)

    return unsafe''',
    marker="def unsafe_members(tar, destination):",
)
replace_once(
    RESTORE_EXPORT,
    "unsafe = safe_members(tar)",
    "unsafe = unsafe_members(tar, tmp_path)",
    marker="unsafe = unsafe_members(tar, tmp_path)",
)
replace_once(
    RESTORE_EXPORT,
    "                tar.extractall(tmp_path)",
    '''                if unsafe:
                    raise ValueError("Backup archive contains unsafe members and was not extracted.")

                tar.extractall(tmp_path, members=tar.getmembers())''',
    marker="Backup archive contains unsafe members and was not extracted.",
)

HOLIDAY_IMPORTER = "deployment/rocky-linux/import-company-holidays.py"
replace_once(
    HOLIDAY_IMPORTER,
    "import sys\nfrom pathlib import Path",
    "import sys\nfrom datetime import date\nfrom decimal import Decimal, InvalidOperation\nfrom pathlib import Path",
    marker="from decimal import Decimal, InvalidOperation",
)
replace_once(
    HOLIDAY_IMPORTER,
    '''            if not row["holiday_date"].startswith(f"{year}-"):
                print(f"Skipping {row['holiday_date']} because it is not in {year}")
                continue
            rows.append(row)''',
    '''            try:
                holiday_date = date.fromisoformat(row["holiday_date"].strip())
            except ValueError as exc:
                raise SystemExit(f"Invalid holiday_date: {row['holiday_date']}") from exc

            if holiday_date.year != year:
                print(f"Skipping {holiday_date.isoformat()} because it is not in {year}")
                continue

            raw_hours = (row.get("auto_populate_hours") or "8.00").strip()
            try:
                validated_hours = Decimal(raw_hours)
            except InvalidOperation as exc:
                raise SystemExit(f"Invalid auto_populate_hours: {raw_hours}") from exc

            if not validated_hours.is_finite() or validated_hours < 0 or validated_hours > 24:
                raise SystemExit(f"auto_populate_hours must be between 0 and 24: {raw_hours}")

            row["holiday_date"] = holiday_date.isoformat()
            row["_validated_auto_populate_hours"] = format(validated_hours, "f")
            rows.append(row)''',
    marker='row["_validated_auto_populate_hours"]',
)
replace_once(
    HOLIDAY_IMPORTER,
    "{row.get('auto_populate_hours') or '8.00'}, holiday_upload_batch_id FROM batch ",
    "{row['_validated_auto_populate_hours']}, holiday_upload_batch_id FROM batch ",
    marker="row['_validated_auto_populate_hours']",
)

RESTRICTED_SERVER = "deployment/rocky-linux/serve-frontend-public-restricted.py"
replace_once(
    RESTRICTED_SERVER,
    '''    def do_GET(self):
        if not self.client_is_allowed():''',
    '''    def do_HEAD(self):
        if not self.client_is_allowed():
            self.reject_client()
            return

        super().do_HEAD()

    def do_GET(self):
        if not self.client_is_allowed():''',
    marker="def do_HEAD(self):",
)

# Predictable temporary-file remediation.
SMOKE = "scripts/021-release-smoke.sh"
replace_once(
    SMOKE,
    '  local body_file="/tmp/projectpulse-021-smoke-body.txt"',
    '''  local body_file
  body_file="$(mktemp "${TMPDIR:-/tmp}/projectpulse-021-smoke-body.XXXXXX")"
  trap 'rm -f "$body_file"' RETURN''',
    marker="projectpulse-021-smoke-body.XXXXXX",
)

BL_BU = "scripts/validate-019m-bl-through-bu-production-hardening.sh"
replace_once(
    BL_BU,
    "cd /opt/project-time-platform/app/project-time-platform\n",
    '''cd /opt/project-time-platform/app/project-time-platform

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/projectpulse-019m-bl-bu.XXXXXX")"
trap 'rm -rf "$TMP_ROOT"' EXIT
''',
    marker="projectpulse-019m-bl-bu.XXXXXX",
)
for old, new in (
    ("/tmp/019m-bl-bu-endpoint.json", "$TMP_ROOT/endpoint.json"),
    ("/tmp/019m-bl-bu-preflight-run.json", "$TMP_ROOT/preflight-run.json"),
    ("/tmp/019m-bl-bu-engineer.json", "$TMP_ROOT/engineer.json"),
):
    replace_all(BL_BU, old, new)

AZ_BJ = "scripts/validate-019m-az-through-bj.sh"
replace_once(
    AZ_BJ,
    "cd /opt/project-time-platform/app/project-time-platform\n",
    '''cd /opt/project-time-platform/app/project-time-platform

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/projectpulse-019m-az-bj.XXXXXX")"
trap 'rm -rf "$TMP_ROOT"' EXIT
''',
    marker="projectpulse-019m-az-bj.XXXXXX",
)
for old, new in (
    ("/tmp/019m-az-bj-endpoint.json", "$TMP_ROOT/endpoint.json"),
    ("/tmp/019m-az-bj-dry-run.json", "$TMP_ROOT/dry-run.json"),
    ("/tmp/019m-az-bj-engineer.json", "$TMP_ROOT/engineer.json"),
):
    replace_all(AZ_BJ, old, new)

CF_GUARD = "scripts/validate-019m-cf-production-wording-compatibility-guard.sh"
replace_once(
    CF_GUARD,
    '''REPO_ROOT="/opt/project-time-platform/app/project-time-platform"
cd "$REPO_ROOT"
''',
    '''REPO_ROOT="/opt/project-time-platform/app/project-time-platform"
cd "$REPO_ROOT"

TMP_ROOT="$(mktemp -d "${TMPDIR:-/tmp}/projectpulse-019m-cf.XXXXXX")"
trap 'rm -rf "$TMP_ROOT"' EXIT
''',
    marker="projectpulse-019m-cf.XXXXXX",
)
for old, new in (
    ("mkdir -p /tmp/projectpulse-019m-cf-api", 'mkdir -p "$TMP_ROOT/api"'),
    ('out="/tmp/projectpulse-019m-cf-api/${safe_name}.json"', 'out="$TMP_ROOT/api/${safe_name}.json"'),
    ('PREVIEW_JSON="/tmp/projectpulse-019m-cf-api/_api_time-compliance_preview.json"', 'PREVIEW_JSON="$TMP_ROOT/api/_api_time-compliance_preview.json"'),
    ('find /tmp/projectpulse-019m-cf-api', 'find "$TMP_ROOT/api"'),
    ("/tmp/projectpulse-019m-cf-registry.json", "$TMP_ROOT/registry.json"),
    ("/tmp/projectpulse-019m-cf-engineer-denial.json", "$TMP_ROOT/engineer-denial.json"),
):
    replace_all(CF_GUARD, old, new)

# ---------------------------------------------------------------------------
# Deterministic postconditions
# ---------------------------------------------------------------------------
require(
    PROGRAM,
    required=(
        "app.UseProjectPulseSecurityHardening();",
        "SECURITY_20260729_LEGACY_ROLE_ASSIGNMENT_AUTHORITY",
        "SECURITY_20260729_ADMIN_USER_DIRECTORY",
        "SECURITY_20260729_ADMIN_LOCAL_ACCOUNT_DIRECTORY",
        "SECURITY_20260729_LOCAL_PASSWORD_ADMIN_AND_BREAK_GLASS",
        "SECURITY_20260729_NOTIFICATION_URL_ALLOWLIST",
        "SECURITY_20260729_FIXED_INTERNAL_API_ORIGIN",
        "SECURITY_20260729_CSV_FORMULA_NEUTRALIZATION",
        "SECURITY_20260729_NO_LOGIN_HINT_IDENTITY_FALLBACK",
        "SECURITY_20260729_SAFE_DOCUMENT_PATH_COMPONENT",
        "SECURITY_20260729_HOLIDAY_IMPORT_AUTHORITY",
    ),
    forbidden=(
        'preferredUsername ?? requestedEmail',
        'var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}"',
        'var storedFileName = $"{documentType}_{Guid.NewGuid():N}{Path.GetExtension(safeOriginalFileName)}";',
    ),
)
require(
    SECURITY_MODULE,
    required=(
        "HasOrganizationIntakeScope",
        "SECURITY_20260729_TIME_COMPLIANCE_ORG_SCOPE",
        "ValidateRoleMutationAsync",
        "ValidateObjectScopeAsync",
    ),
)
require(
    INDEX_HTML,
    required=("projectPulse022DSafeActionUrl",),
    forbidden=(),
)
require(
    WORK_REGISTER,
    required=("Blocked an unsafe document reference",),
    forbidden=("window.open(reference, '_blank'",),
)
require(SYNC_EXPORT, required=("socket.create_connection((peer_host, 22)",), forbidden=("/dev/tcp/{peer_host}",))
require(RESTORE_EXPORT, required=("def unsafe_members(tar, destination):", "was not extracted"), forbidden=("def safe_members(tar):",))
require(HOLIDAY_IMPORTER, required=("_validated_auto_populate_hours",), forbidden=("row.get('auto_populate_hours') or '8.00'}, holiday_upload_batch_id",))
require(RESTRICTED_SERVER, required=("def do_HEAD(self):",))
require(SMOKE, required=("mktemp",), forbidden=("/tmp/projectpulse-021-smoke-body.txt",))
require(BL_BU, required=("projectpulse-019m-bl-bu.XXXXXX",), forbidden=("/tmp/019m-bl-bu-",))
require(AZ_BJ, required=("projectpulse-019m-az-bj.XXXXXX",), forbidden=("/tmp/019m-az-bj-",))
require(CF_GUARD, required=("projectpulse-019m-cf.XXXXXX",), forbidden=("/tmp/projectpulse-019m-cf-api", "/tmp/projectpulse-019m-cf-registry.json", "/tmp/projectpulse-019m-cf-engineer-denial.json"))

print("SECURITY_REMEDIATION_SOURCE_APPLY=PASSED")
