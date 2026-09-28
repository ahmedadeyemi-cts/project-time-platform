using Microsoft.AspNetCore.Http;
using Npgsql;
using ProjectTime.Api.Modules;

internal static class SecurityDatabaseTests
{
    internal static async Task RunAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("SECURITY_TEST_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("SECURITY_DATABASE_REGRESSIONS=NOT_RUN isolated_database_not_configured");
            return;
        }
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        // Temporary tables shadow application tables on this one connection.
        // The suite never updates an existing application table or schema.
        async Task Execute(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        await Execute("""
            CREATE TEMP TABLE app_users (user_id uuid PRIMARY KEY, email text, is_active boolean DEFAULT true, login_enabled boolean DEFAULT true, entra_object_id text, entra_tenant_id text);
            CREATE TEMP TABLE app_roles (app_role_id integer PRIMARY KEY, role_code text, is_active boolean DEFAULT true);
            CREATE TEMP TABLE app_user_role_assignments (user_id uuid, app_role_id integer, is_active boolean DEFAULT true);
            CREATE TEMP TABLE auth_sessions (auth_session_id uuid, user_id uuid, provider_code text, session_token_hash text, created_at timestamptz, expires_at timestamptz, revoked_at timestamptz);
            CREATE TEMP TABLE auth_local_accounts (user_id uuid, password_hash_updated_at timestamptz);
            INSERT INTO app_roles VALUES (1,'SUPER_ADMINISTRATOR',true),(2,'ADMINISTRATOR',true),(3,'ENGINEERING',true);
            INSERT INTO app_users (user_id,email) VALUES
                ('11111111-1111-1111-1111-111111111111','super@example.invalid'),
                ('22222222-2222-2222-2222-222222222222','admin@example.invalid'),
                ('33333333-3333-3333-3333-333333333333','engineer@example.invalid');
            INSERT INTO app_user_role_assignments VALUES
                ('11111111-1111-1111-1111-111111111111',1,true),
                ('22222222-2222-2222-2222-222222222222',2,true),
                ('33333333-3333-3333-3333-333333333333',3,true);
            """);
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        Guid super = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid admin = Guid.Parse("22222222-2222-2222-2222-222222222222");
        Guid engineer = Guid.Parse("33333333-3333-3333-3333-333333333333");
        DefaultHttpContext Context(Guid user)
        {
            var context = new DefaultHttpContext();
            context.Items["ProjectPulseSessionUserId"] = user;
            context.Items["ProjectPulseActualUserId"] = user;
            context.Items["ProjectPulseEffectiveUserId"] = user;
            return context;
        }
        Check(await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(Context(super), connection), "Active super-administrator admitted");
        Check(!await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(Context(admin), connection), "Ordinary administrator cannot gain permanent super authority");
        var forged = Context(engineer);
        forged.Items["ProjectPulseActualEmail"] = "super@example.invalid";
        forged.Items["ProjectPulsePermanentFullControl"] = true;
        Check(!await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(forged, connection), "Email collision and cached flag cannot change identity");
        Check((Guid)forged.Items["ProjectPulseActualUserId"]! == engineer, "Authority lookup never rewrites actor identity");
        await Execute("UPDATE app_users SET is_active=false WHERE email='super@example.invalid'");
        Check(!await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(Context(super), connection), "Disabled super-administrator rejected");
        await Execute("UPDATE app_users SET is_active=true; UPDATE app_user_role_assignments SET is_active=false WHERE app_role_id=1");
        Check(!await ProjectPulseActualSessionAuthority.IsSuperAdministratorAsync(Context(super), connection), "Revoked super-administrator assignment rejected");

        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/backend/ProjectTime.Api/Program.cs"))) root = root.Parent;
        var source = await File.ReadAllTextAsync(Path.Combine(root!.FullName, "src/backend/ProjectTime.Api/Program.cs"));
        string QueryContaining(string marker)
        {
            int location = source.IndexOf(marker, StringComparison.Ordinal);
            if (location < 0) throw new Exception("Production SQL marker missing");
            int start = source.LastIndexOf("\"\"\"", location, StringComparison.Ordinal) + 3;
            int end = source.IndexOf("\"\"\"", location, StringComparison.Ordinal);
            return source[start..end];
        }
        var sessionQuery = QueryContaining("FROM auth_sessions s\n            JOIN app_users u");
        async Task<bool> SessionValid()
        {
            await using var command = new NpgsqlCommand(sessionQuery, connection);
            command.Parameters.AddWithValue("session_token_hash", "synthetic-token-hash");
            return await command.ExecuteScalarAsync() is Guid;
        }
        await Execute("""
            INSERT INTO auth_sessions VALUES ('44444444-4444-4444-4444-444444444444','33333333-3333-3333-3333-333333333333','LOCAL','synthetic-token-hash',NOW()-INTERVAL '1 hour',NOW()+INTERVAL '1 hour',NULL);
            INSERT INTO auth_local_accounts VALUES ('33333333-3333-3333-3333-333333333333',NOW()-INTERVAL '2 hours');
            """);
        Check(await SessionValid(), "Current session admitted");
        await Execute("UPDATE auth_local_accounts SET password_hash_updated_at=NOW()");
        Check(!await SessionValid(), "Password change invalidates earlier session");
        await Execute("UPDATE auth_local_accounts SET password_hash_updated_at=NOW()-INTERVAL '1 day'; UPDATE auth_sessions SET created_at=NOW()-INTERVAL '13 hours'");
        Check(!await SessionValid(), "Absolute session lifetime enforced despite future expiry");
        await Execute("UPDATE auth_sessions SET created_at=NOW()-INTERVAL '1 hour'; UPDATE app_users SET login_enabled=false WHERE email='engineer@example.invalid'");
        Check(!await SessionValid(), "Disabled login invalidates session");
        await Execute("UPDATE app_users SET login_enabled=true; UPDATE app_user_role_assignments SET is_active=false WHERE app_role_id=3");
        Check(!await SessionValid(), "Role removal invalidates session");

        await Execute("UPDATE app_users SET entra_object_id='bound-object',entra_tenant_id='bound-tenant' WHERE email='super@example.invalid'");
        var lookupQuery = QueryContaining("AND ((entra_object_id = @entra_object_id AND entra_tenant_id = @tenant_id)");
        async Task<Guid?> Lookup(string objectId, string tenantId, string email)
        {
            await using var command = new NpgsqlCommand(lookupQuery, connection);
            command.Parameters.AddWithValue("entra_object_id", objectId);
            command.Parameters.AddWithValue("tenant_id", tenantId);
            command.Parameters.AddWithValue("email", email);
            return await command.ExecuteScalarAsync() as Guid?;
        }
        Check(await Lookup("bound-object", "bound-tenant", "renamed@example.invalid") == super, "Stable tenant/object identity survives an email rename");
        Check(await Lookup("other-object", "bound-tenant", "super@example.invalid") is null, "Email cannot rebind an established object identity");
        Check(await Lookup("bound-object", "other-tenant", "super@example.invalid") is null, "Object identity cannot cross tenants");
        Check(await Lookup("new-object", "bound-tenant", "engineer@example.invalid") == engineer, "Unbound imported account remains linkable");
        Console.WriteLine($"SECURITY_DATABASE_REGRESSIONS=PASS assertions={count}");
    }
}
