#!/usr/bin/env python3
"""Permanent source-contract regression gate for the 2026-07-29 security findings."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def source(relative: str) -> str:
    path = ROOT / relative
    if not path.is_file():
        raise AssertionError(f"required source file is missing: {relative}")
    return path.read_text(encoding="utf-8")


def require(name: str, relative: str, *tokens: str) -> None:
    text = source(relative)
    missing = [token for token in tokens if token not in text]
    if missing:
        raise AssertionError(f"{name}: {relative} is missing {missing}")


def forbid(name: str, relative: str, *tokens: str) -> None:
    text = source(relative)
    present = [token for token in tokens if token in text]
    if present:
        raise AssertionError(f"{name}: {relative} still contains {present}")


def check(identifier: str, title: str, callback) -> None:
    callback()
    print(f"PASS {identifier}: {title}")


PROGRAM = "src/backend/ProjectTime.Api/Program.cs"
SECURITY = "src/backend/ProjectTime.Api/Modules/SecurityHardeningModule.cs"
WORK_AUTH = "src/backend/ProjectTime.Api/Modules/WorkRegisterAuthorization.cs"
APPROVAL = "src/backend/ProjectTime.Api/Modules/ApprovalCenterModule.cs"
INDEX = "src/frontend/project-time-web/index.html"
WORK_REGISTER = "src/frontend/project-time-web/src/WorkRegisterCenter.jsx"
SMTP = "ops/projectpulse/scripts/projectpulse-send-backup-email.py"
SYNC = "ops/projectpulse/scripts/projectpulse-sync-status-export.sh"
RESTORE = "ops/projectpulse/scripts/projectpulse-restore-validation-export.sh"
HOLIDAY = "deployment/rocky-linux/import-company-holidays.py"
RESTRICTED = "deployment/rocky-linux/serve-frontend-public-restricted.py"
SMOKE = "scripts/021-release-smoke.sh"
BL_BU = "scripts/validate-019m-bl-through-bu-production-hardening.sh"
AZ_BJ = "scripts/validate-019m-az-through-bj.sh"
CF_GUARD = "scripts/validate-019m-cf-production-wording-compatibility-guard.sh"


checks = [
    ("2622748", "legacy role assignment requires Administrator authority and transactional audit", lambda: (
        require("legacy role assignment", PROGRAM,
                "SECURITY_20260729_LEGACY_ROLE_ASSIGNMENT_AUTHORITY",
                "user_roles_updated_legacy",
                "InsertProjectPulseRoleAuditAsync",
                "SECURITY_20260729_TRANSACTIONAL_ROLE_AUDIT_HELPERS"),
        require("central role boundary", SECURITY,
                "IsRoleMutationPath",
                "ValidateRoleMutationAsync",
                "TargetsExistingSuperAdministratorAsync")
    )),
    ("2666052", "SMTP credentials require certificate-validated TLS", lambda: (
        require("SMTP TLS", SMTP,
                "ssl.create_default_context()",
                "smtp.starttls(context=tls_context)",
                "Credentialed SMTP requires verified TLS"),
        forbid("SMTP TLS", SMTP, "ssl._create_unverified_context", "CERT_NONE")
    )),
    ("2622852", "Work Register project mutations require role and project scope", lambda: require(
        "Work Register mutation", WORK_AUTH,
        "/api/work-register/projects/update",
        "IsAssignedProjectManagerAsync",
        "CanEditAll",
        "StatusCodes.Status403Forbidden")),
    ("2622854", "time-compliance endpoints require organization-level permission", lambda: require(
        "time compliance", SECURITY,
        "SECURITY_20260729_TIME_COMPLIANCE_ORG_SCOPE",
        "SecurityPolicy.TimeCompliance",
        "CanViewTimeCompliance")),
    ("2622855", "Project Intake downloads require parent/document scope", lambda: require(
        "intake document scope", SECURITY,
        "CanAccessIntakeDocumentAsync",
        "requested_by_user_id",
        "engineering_visible",
        "document_access_denied")),
    ("2622862", "notification action URLs are validated on write and render", lambda: (
        require("notification write URL", PROGRAM, "SECURITY_20260729_NOTIFICATION_URL_ALLOWLIST"),
        require("notification renderer URL", INDEX,
                "projectPulse022DSafeActionUrl",
                "projectPulse022DTopbarSafeActionUrl",
                "rel=\"noopener noreferrer\""),
        forbid("notification renderer URL", INDEX, 'href="${escapeHtml(item.actionUrl)}"')
    )),
    ("2622749", "single, bulk, and local-user role writes protect Super Administrator", lambda: require(
        "role write parity", PROGRAM,
        "user_roles_updated",
        "user_roles_bulk_updated",
        "local_user_created_with_roles",
        "super_administrator_target_protected",
        "Bulk role updates are restricted to Administrators and Super Administrators.")),
    ("2622750", "break-glass password mutation is blocked", lambda: require(
        "break-glass password", PROGRAM,
        "SECURITY_20260729_LOCAL_PASSWORD_ADMIN_AND_BREAK_GLASS",
        "break_glass_password_protected")),
    ("2622751", "user directory requires Administrator authority", lambda: require(
        "admin user directory", PROGRAM,
        "SECURITY_20260729_ADMIN_USER_DIRECTORY",
        "The user directory is restricted to Administrators and Super Administrators.")),
    ("2622752", "upload storage names do not contain user-controlled path components", lambda: (
        require("upload path", PROGRAM,
                "SECURITY_20260729_SAFE_DOCUMENT_PATH_COMPONENT",
                'var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(safeOriginalFileName)}";'),
        forbid("upload path", PROGRAM,
               'var storedFileName = $"{documentType}_',
               'var storedFileName = $"{documentCategory}_')
    )),
    ("2622753", "Work Register document downloads enforce project and visibility scope", lambda: require(
        "Work Register document scope", SECURITY,
        "CanAccessWorkRegisterDocumentAsync",
        "CanManageAllWorkRegisterDocuments",
        "pm_ptc_admin",
        "ptc_admin_only")),
    ("2622754", "intake-package creation is protected by Work Register authorization", lambda: require(
        "intake package", WORK_AUTH,
        "/api/work-register/intake/packages",
        "access.CanCreate",
        "Only a Project Team Coordinator, Administrator, or Super Administrator can create")),
    ("2622755", "reporting routes require reporting permission", lambda: require(
        "reporting", SECURITY,
        "/api/reports/030/",
        "SecurityPolicy.Reporting",
        "CanViewReporting")),
    ("2622756", "password-reset approval routes require Administrator authority", lambda: require(
        "password reset approval", SECURITY,
        "/api/auth/password-reset/",
        "SecurityPolicy.Administrator")),
    ("2622757", "local-account inventory requires Administrator authority", lambda: require(
        "local account inventory", PROGRAM,
        "SECURITY_20260729_ADMIN_LOCAL_ACCOUNT_DIRECTORY",
        "Local account administration is restricted to Administrators and Super Administrators.")),
    ("2622758", "manager approvals preserve direct-report and managed-project scope", lambda: require(
        "manager approval scope", APPROVAL,
        "@can_view_all = TRUE",
        "@is_manager = TRUE",
        "lower(COALESCE(u.manager_email, '')) = lower(@actor_email)",
        "@is_project_manager = TRUE")),
    ("2622760", "email headers reject or sanitize CR/LF", lambda: (
        require("API email headers", PROGRAM,
                "ProjectPulse041ASanitizeHeaderValue",
                '@"[\\r\\n]+"'),
        require("backup email headers", SMTP, "reject_header_injection")
    )),
    ("2622761", "replication peer reachability does not invoke a shell", lambda: (
        require("replication peer", SYNC, "socket.create_connection((peer_host, 22)"),
        forbid("replication peer", SYNC, "/dev/tcp/{peer_host}", "bash -c")
    )),
    ("2622763", "SSO state is bound to the initiating browser", lambda: require(
        "SSO state", SECURITY,
        "ProjectPulseSsoState",
        "ValidateAndConsumeSsoStateCookie",
        "CryptographicOperations.FixedTimeEquals",
        "HttpOnly = true")),
    ("2622764", "holiday imports require authorized administration", lambda: require(
        "holiday import", PROGRAM,
        "SECURITY_20260729_HOLIDAY_IMPORT_AUTHORITY",
        "Holiday imports are restricted to Administrators and Project Team Coordinators.")),
    ("2622765", "Work Register document references use an allowlisted URL", lambda: (
        require("document reference API", SECURITY, "documentReference", "unsafe_url_rejected"),
        require("document reference UI", WORK_REGISTER,
                "Blocked an unsafe document reference",
                "parsed.protocol === 'https:'"),
        forbid("document reference UI", WORK_REGISTER, "window.open(reference, '_blank'")
    )),
    ("2622730", "Project Intake mutations require capability and parent-object scope", lambda: require(
        "Project Intake mutations", SECURITY,
        "IntakeRequestMutationPath",
        "CanAccessIntakeRequestAsync",
        "SecurityPolicy.ProjectIntake",
        "SecurityPolicy.ProjectAssignment")),
    ("2666056", "View-As denies every unsafe method", lambda: (
        require("View-As", PROGRAM, "SECURITY_20260729_VIEW_AS_ALL_WRITES_BLOCKED"),
        require("View-As middleware", SECURITY, "view_as_read_only", "IsUnsafeMethod(method)")
    )),
    ("2666058", "temporary files use unpredictable private paths", lambda: (
        require("release smoke temp", SMOKE, "mktemp"),
        require("BL-BU temp", BL_BU, "projectpulse-019m-bl-bu.XXXXXX"),
        require("AZ-BJ temp", AZ_BJ, "projectpulse-019m-az-bj.XXXXXX"),
        require("CF temp", CF_GUARD, "projectpulse-019m-cf.XXXXXX"),
        forbid("fixed temp files", SMOKE, "/tmp/projectpulse-021-smoke-body.txt")
    )),
    ("2666037", "restore validation rejects unsafe archive members before extraction", lambda: require(
        "archive extraction", RESTORE,
        "def unsafe_members(tar, destination):",
        "member.issym()",
        "member.islnk()",
        "member.isdev()",
        "was not extracted")),
    ("2622824", "CSV export neutralizes spreadsheet formulas", lambda: require(
        "CSV formula", PROGRAM,
        "SECURITY_20260729_CSV_FORMULA_NEUTRALIZATION",
        'formulaCandidate.StartsWith("=", StringComparison.Ordinal)',
        'formulaCandidate.StartsWith("@", StringComparison.Ordinal)')),
    ("2622762", "SSO identity never falls back to the login hint", lambda: (
        require("SSO identity", PROGRAM, "SECURITY_20260729_NO_LOGIN_HINT_IDENTITY_FALLBACK"),
        forbid("SSO identity", PROGRAM, "preferredUsername ?? requestedEmail")
    )),
    ("2622766", "local authentication and reset responses resist account enumeration", lambda: require(
        "account enumeration", SECURITY,
        "invalid_local_credentials",
        "password_reset_request_received",
        "TryHandleGenericLocalLoginRouteAsync")),
    ("2622767", "internal API calls use a configured fixed origin rather than Host", lambda: (
        require("internal origin", PROGRAM,
                "SECURITY_20260729_FIXED_INTERNAL_API_ORIGIN",
                "PROJECTPULSE_INTERNAL_API_BASE_URL",
                "http://127.0.0.1:5080"),
        forbid("internal origin", PROGRAM,
               'var baseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}"')
    )),
    ("2622768", "holiday numeric values are parsed and bounded before SQL generation", lambda: (
        require("holiday numeric validation", HOLIDAY,
                "Decimal(raw_hours)",
                "validated_hours.is_finite()",
                "validated_hours < 0",
                "validated_hours > 24",
                "_validated_auto_populate_hours"),
        forbid("holiday numeric validation", HOLIDAY,
               "row.get('auto_populate_hours') or '8.00'}, holiday_upload_batch_id")
    )),
    ("2622769", "HEAD requests enforce the same public IP allowlist", lambda: require(
        "HEAD allowlist", RESTRICTED,
        "def do_HEAD(self):",
        "if not self.client_is_allowed():",
        "self.reject_client()")),
    ("2622740", "production diagnostics require Administrator authority", lambda: require(
        "diagnostics", SECURITY,
        "IsDiagnosticPath",
        "/api/production-data-readiness",
        "/api/db-config-check",
        "/api/schema/tables",
        "SecurityPolicy.Administrator")),
]


for identifier, title, callback in checks:
    check(identifier, title, callback)

if len(checks) != 32:
    raise AssertionError(f"expected 32 finding checks, found {len(checks)}")

print("PLATFORM_SECURITY_REMEDIATION_VALIDATION=PASSED")
print(f"PLATFORM_SECURITY_REMEDIATION_FINDINGS_VALIDATED={len(checks)}")
