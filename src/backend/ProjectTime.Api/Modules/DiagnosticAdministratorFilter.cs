using Npgsql;

namespace ProjectTime.Api.Modules;

/// <summary>Endpoint-level authority, independent of URL policy matching.</summary>
internal sealed class DiagnosticAdministratorFilter : IEndpointFilter
{
    private readonly Func<HttpContext, Task<bool>> authority;

    public DiagnosticAdministratorFilter()
        : this(context => IsAdministratorAsync(context)) { }

    internal DiagnosticAdministratorFilter(Func<HttpContext, Task<bool>> authority)
        => this.authority = authority;

    internal static async Task<bool> IsAdministratorAsync(HttpContext context, NpgsqlConnection? existingConnection = null)
    {
        if (ProjectPulseActualSessionAuthority.IsViewAs(context)) return false;
        var userId = ProjectPulseActualSessionAuthority.ReadUserId(
            context, "ProjectPulseSessionUserId", "ProjectPulseActualUserId");
        if (!userId.HasValue) return false;
        var configured = existingConnection is null ? ProjectPulseActualSessionAuthority.BuildConnectionString() : "";
        if (existingConnection is null && string.IsNullOrWhiteSpace(configured)) return false;
        await using var ownedConnection = existingConnection is null ? new NpgsqlConnection(configured) : null;
        var connection = existingConnection ?? ownedConnection!;
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(context.RequestAborted);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM app_users u
                JOIN app_user_role_assignments a ON a.user_id=u.user_id AND a.is_active=TRUE
                JOIN app_roles r ON r.app_role_id=a.app_role_id AND r.is_active=TRUE
                WHERE u.user_id=@user_id AND u.is_active=TRUE
                  AND upper(btrim(r.role_code)) IN ('ADMINISTRATOR','SUPER_ADMINISTRATOR',
                      'SUPERADMINISTRATOR','GLOBAL_ADMINISTRATOR','GLOBALADMINISTRATOR')
            )
            """, connection);
        command.Parameters.AddWithValue("user_id", userId.Value);
        return await command.ExecuteScalarAsync(context.RequestAborted) is true;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (ProjectPulseActualSessionAuthority.IsViewAs(context.HttpContext))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        try
        {
            if (!await authority(context.HttpContext))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        catch (Exception error) when (error is Npgsql.NpgsqlException or InvalidOperationException)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        return await next(context);
    }
}
