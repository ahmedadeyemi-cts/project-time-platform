using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class PasswordResetTargetSafety
{
    // Called after route-level password-reset authority and approved-request lookup.
    internal static async Task<IResult?> ValidateAsync(HttpContext context, NpgsqlConnection connection,
        NpgsqlTransaction transaction, Guid userId, string accountEmail)
    {
        if (ProjectPulseActualSessionAuthority.IsViewAs(context))
            return Results.Json(new { status="view_as_read_only" },statusCode:403);
        if (accountEmail.Equals(Environment.GetEnvironmentVariable("PROJECTPULSE_BREAK_GLASS_ACCOUNT")
                ?? "ahmed.adeyemi@ussignal.local",StringComparison.OrdinalIgnoreCase))
            return Results.Json(new { status="break_glass_password_protected" },statusCode:403);
        if (await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(context,connection,transaction))
            return null;
        await using var command=new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM app_user_role_assignments a JOIN app_roles r ON r.app_role_id=a.app_role_id
            WHERE a.user_id=@user_id AND a.is_active AND r.is_active
              AND trim(both '_' from regexp_replace(upper(btrim(r.role_code)), '[^A-Z0-9]+', '_', 'g'))
                  IN ('SUPER_ADMINISTRATOR','SUPERADMINISTRATOR','GLOBAL_ADMINISTRATOR','GLOBALADMINISTRATOR'));
            """,connection,transaction);
        command.Parameters.AddWithValue("user_id",userId);
        return await command.ExecuteScalarAsync() is true
            ? Results.Json(new { status="super_administrator_target_protected" },statusCode:403) : null;
    }
}
