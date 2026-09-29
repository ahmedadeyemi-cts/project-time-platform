using System.Net.Http.Headers;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

internal static class DirectoryIdentitySafety
{
    internal static async Task<JsonElement> ReadVerifiedUserAsync(string objectId, string graphToken, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(objectId, out var id)) throw new InvalidOperationException("A stable Entra object identifier is required.");
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 1024 * 1024 };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", graphToken);
        var uri = $"https://graph.microsoft.com/v1.0/users/{id:D}?$select=id,mail,userPrincipalName,displayName,accountEnabled,jobTitle,department,officeLocation";
        using var response = await client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!Guid.TryParse(document.RootElement.GetProperty("id").GetString(), out var returned) || returned != id)
            throw new InvalidOperationException("Directory identity could not be verified.");
        return document.RootElement.Clone();
    }

    internal static async Task<bool> CanRefreshAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid userId, string objectId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT u.is_active AND COALESCE(u.login_enabled,TRUE)
              AND (NULLIF(btrim(u.entra_object_id),'') IS NULL OR u.entra_object_id=@object_id)
              AND NOT EXISTS (
                SELECT 1 FROM app_user_role_assignments a JOIN app_roles r ON r.app_role_id=a.app_role_id
                WHERE a.user_id=u.user_id AND a.is_active AND r.is_active
                  AND upper(r.role_code) IN ('SUPER_ADMINISTRATOR','SUPERADMINISTRATOR','GLOBAL_ADMINISTRATOR','GLOBALADMINISTRATOR','ADMINISTRATOR'))
            FROM app_users u WHERE u.user_id=@user_id FOR UPDATE OF u;
            """, connection, transaction);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("object_id", objectId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
}
