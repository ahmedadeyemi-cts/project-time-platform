namespace ProjectTime.Api.Modules;

public static class ScopedRolePolicyRules
{
    public static readonly HashSet<string> NonBypassableActions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "TIME_DELETE_PERMANENT",
            "USER_IMPERSONATE",
            "SYSTEM_CONFIGURE",
            "AUDIT_BYPASS",
            "APPROVAL_DELETE_PERMANENT",
            "APPROVAL_HISTORY_EDIT",
            "APPROVAL_SYSTEM_CONFIGURE",
            "UTILIZATION_EDIT",
            "NON_BYPASSABLE_SAFETY_BYPASS",
            "SECRET_READBACK",
            "PRODUCTION_DEPLOY",
            "DESTRUCTIVE_DATABASE_CHANGE",
            "SECURITY_CONTROL_BYPASS",
            "AUDIT_DELETE"
        };

    private static readonly HashSet<string> WriteActions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "RECORD_CREATE","RECORD_EDIT","RECORD_ASSIGN","RECORD_REOPEN",
            "WORKFLOW_MANAGE","APPROVAL_APPROVE","APPROVAL_REJECT",
            "MODULE_CONFIGURE","POLICY_DELEGATE","AUDIT_RECORD",
            "DELEGATED_ACTION","TIME_EDIT_OWN","TIME_SUBMIT",
            "TIME_REASSIGN","TIME_CORRECT_ON_BEHALF","TIME_REOPEN",
            "TIME_APPROVE","TIME_REJECT","APPROVAL_APPROVE_MANAGER",
            "APPROVAL_REJECT_MANAGER","APPROVAL_APPROVE_PROJECT_MANAGER",
            "APPROVAL_REJECT_PROJECT_MANAGER","APPROVAL_APPROVE_PTC_FINAL",
            "APPROVAL_REJECT_PTC_FINAL","APPROVAL_DELEGATE_MANAGER",
            "APPROVAL_DELEGATE_PROJECT_MANAGER",
            "APPROVAL_RETURN_FOR_CORRECTION","POLICY_VALIDATE",
            "POLICY_PUBLISH","POLICY_RESTORE","UTILIZATION_EDIT"
        };

    public static bool IsWriteAction(string? actionCode) =>
        WriteActions.Contains(actionCode ?? string.Empty)
        || NonBypassableActions.Contains(actionCode ?? string.Empty);

    public static ScopedRouteContract? RouteContract(string path, string method)
    {
        var normalized = (path ?? string.Empty).ToLowerInvariant();
        var isWrite = !HttpMethods.IsGet(method)
            && !HttpMethods.IsHead(method)
            && !HttpMethods.IsOptions(method);

        if (normalized.StartsWith("/api/timesheet")
            || normalized.StartsWith("/api/timesheets")
            || normalized.StartsWith("/api/time-entries"))
        {
            var action = !isWrite
                ? "TIME_VIEW"
                : normalized.Contains("submit")
                    ? "TIME_SUBMIT"
                    : normalized.Contains("reopen") || normalized.Contains("unlock")
                        ? "TIME_REOPEN"
                        : "TIME_EDIT_OWN";
            return new ScopedRouteContract("001", action, isWrite);
        }

        if (normalized.StartsWith("/api/manager/approvals")
            || normalized.StartsWith("/api/approval-center"))
        {
            return new ScopedRouteContract(
                "002",
                isWrite ? "APPROVAL_APPROVE" : "APPROVAL_VIEW",
                isWrite);
        }

        if (normalized.StartsWith("/api/utilization"))
        {
            return new ScopedRouteContract(
                "003",
                isWrite ? "UTILIZATION_EDIT" : "UTILIZATION_VIEW",
                isWrite);
        }

        return null;
    }

    public static string ResolveApprovalAction(
        IEnumerable<string> roleCodes,
        string path)
    {
        var roles = roleCodes
            .Select(ScopedRolePolicyModule.CanonicalRole)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isReject = path.Contains("decline", StringComparison.OrdinalIgnoreCase)
            || path.Contains("reject", StringComparison.OrdinalIgnoreCase)
            || path.Contains("unlock", StringComparison.OrdinalIgnoreCase)
            || path.Contains("correction", StringComparison.OrdinalIgnoreCase);

        if (roles.Contains("PROJECT_TEAM_COORDINATOR")
            || roles.Contains("SUPER_ADMINISTRATOR"))
        {
            return path.Contains("unlock", StringComparison.OrdinalIgnoreCase)
                ? "APPROVAL_RETURN_FOR_CORRECTION"
                : "APPROVAL_DELEGATE_MANAGER";
        }
        if (roles.Contains("PROJECT_MANAGEMENT")
            || roles.Contains("PROJECT_MANAGEMENT_LEAD"))
        {
            return isReject
                ? "APPROVAL_REJECT_PROJECT_MANAGER"
                : "APPROVAL_APPROVE_PROJECT_MANAGER";
        }
        return isReject
            ? "APPROVAL_REJECT_MANAGER"
            : "APPROVAL_APPROVE_MANAGER";
    }

    public sealed record ScopedRouteContract(
        string ModuleCode,
        string ActionCode,
        bool IsWrite);
}

public sealed record ScopedAuthorizationDecision(
    bool Allowed,
    bool ExplicitDeny,
    bool LegacyFallback,
    bool IsViewAs,
    string ModuleCode,
    string ActionCode,
    string ScopeCode,
    int? PolicyVersion,
    bool ReasonRequired,
    bool AuditRequired,
    bool DelegatedAuthority,
    string Explanation)
{
    public static ScopedAuthorizationDecision Denied(
        string moduleCode,
        string actionCode,
        bool isViewAs,
        string explanation) =>
        new(
            false,
            true,
            false,
            isViewAs,
            moduleCode,
            actionCode,
            "DENIED",
            null,
            false,
            true,
            false,
            explanation);
}
