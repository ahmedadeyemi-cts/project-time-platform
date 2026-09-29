using System.Net;
using System.Reflection;
using System.Text;
using Npgsql;
using ProjectTime.Api.Modules;

internal static class CrmCredentialConcurrencyTests
{
    internal static async Task RunAsync()
    {
        var value = Environment.GetEnvironmentVariable("SECURITY_TEST_DB");
        if (string.IsNullOrEmpty(value)) { Console.WriteLine("CRM_CREDENTIAL_CONCURRENCY=NOT_RUN isolated_database_not_configured"); return; }
        var builder = new NpgsqlConnectionStringBuilder(value);
        if (builder.Database != "security_ci") throw new InvalidOperationException("Use the isolated security_ci database.");
        var schema = "crm_test_" + Guid.NewGuid().ToString("N");
        var previousKey = Environment.GetEnvironmentVariable("PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY");
        var key = Enumerable.Range(1, 32).Select(x => (byte)x).ToArray();
        await using var admin = new NpgsqlConnection(value);
        await admin.OpenAsync();
        await Sql(admin, $"CREATE SCHEMA {schema}");
        builder.SearchPath = schema;
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        try
        {
            await using var first = new NpgsqlConnection(builder.ConnectionString);
            await using var second = new NpgsqlConnection(builder.ConnectionString);
            await first.OpenAsync(); await second.OpenAsync();
            await Sql(first, """
                CREATE TABLE crm_integration_providers(provider_key text PRIMARY KEY, provider_name text, auth_model text,
                    base_url text, health_check_url text, oauth_authorization_url text, oauth_token_url text,
                    oauth_client_id text, oauth_scopes text, api_key_header text, api_key_prefix text, is_enabled boolean,
                    provider_status text, last_error_code text, updated_by uuid, updated_at timestamptz);
                CREATE TABLE crm_integration_credentials(provider_key text,credential_kind text,ciphertext bytea,nonce bytea,
                    authentication_tag bytea,credential_version text,expires_at timestamptz,rotated_at timestamptz,rotated_by uuid,
                    PRIMARY KEY(provider_key,credential_kind));
                CREATE TABLE crm_integration_token_refresh_events(refresh_event_id uuid,provider_key text,refresh_trigger text,
                    refresh_status text,diagnostic_code text,provider_http_status int,next_expires_at timestamptz,
                    actor_user_id uuid,event_metadata jsonb,created_at timestamptz);
                CREATE TABLE projectpulse_module_audit_events(event_id uuid,module_number text,entity_type text,entity_id text,
                    action_code text,actor_user_id uuid,evidence_json jsonb);
                INSERT INTO crm_integration_providers VALUES('example','Example','oauth2','https://93.184.216.34/',
                    'https://93.184.216.34/health','https://93.184.216.34/authorize','https://93.184.216.34/old',
                    'old-client','','Authorization','Bearer',true,'','','11111111-1111-1111-1111-111111111111',NOW());
                """);
            Environment.SetEnvironmentVariable("PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY", Convert.ToBase64String(key));
            var actor = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var snapshot = await Invoke("ReadProviderConfigurationAsync", first, "example", CancellationToken.None);
            await Sql(second, "UPDATE crm_integration_providers SET oauth_token_url='https://93.184.216.34/current',oauth_client_id='current-client'");
            await Invoke("SaveCredentialAsync", first, null, "example", "oauth_client_secret", "synthetic-client-secret", null, actor, key, CancellationToken.None);
            await Invoke("SaveCredentialAsync", first, null, "example", "oauth_token", "{\"accessToken\":\"synthetic-access\",\"refreshToken\":\"synthetic-refresh\"}", null, actor, key, CancellationToken.None);
            var handler = new ControlledHandler();
            var factory = new Factory(handler);
            var refresh = Invoke("RefreshOAuthTokenAsync", first, snapshot, actor, "test", factory, CancellationToken.None);
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(handler.Target == "https://93.184.216.34/current", "Refresh must reload the destination under its lease");
            Check(handler.Body.Contains("client_id=current-client"), "Refresh must reload OAuth identity under its lease");
            await using (var denied = await CrmProviderOperationLease.TryAcquireAsync(second, "example", CancellationToken.None))
                Check(denied is null, "Credential/configuration operation must not overlap an in-flight refresh");
            await using (var unrelated = await CrmProviderOperationLease.TryAcquireAsync(second, "different-provider", CancellationToken.None))
                Check(unrelated is not null, "Unrelated providers do not share a global lock");
            handler.Release.TrySetResult();
            var result = await refresh.WaitAsync(TimeSpan.FromSeconds(10));
            Check((bool)result!.GetType().GetProperty("Refreshed")!.GetValue(result)!, "Valid refresh still succeeds");
            await using (var released = await CrmProviderOperationLease.TryAcquireAsync(second, "example", CancellationToken.None))
                Check(released is not null, "Successful refresh releases its lease");
            Check((await CrmErpIntegrationModule.LoadCredentialAsync(first, "example", "oauth_token", key, CancellationToken.None))!.Contains("next-access"), "Valid refreshed credentials persist");
            await Sql(second, "UPDATE crm_integration_providers SET is_enabled=false");
            var disabled = await Invoke("RefreshOAuthTokenAsync", first, snapshot, actor, "test", factory, CancellationToken.None);
            Check(!(bool)disabled!.GetType().GetProperty("Refreshed")!.GetValue(disabled)!, "Stale worker snapshot cannot refresh a disabled provider");
            Check(handler.Calls == 1, "Disabled provider sends no credential-bearing request");
            Environment.SetEnvironmentVariable("PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY", null);
            await Invoke("RefreshOAuthTokenAsync", first, snapshot, actor, "test", factory, CancellationToken.None);
            await using (var released = await CrmProviderOperationLease.TryAcquireAsync(second, "example", CancellationToken.None))
                Check(released is not null, "Early failure releases its lease");
            Console.WriteLine($"CRM_CREDENTIAL_CONCURRENCY=PASS assertions={checks}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PROJECTPULSE_INTEGRATION_SECRET_ENCRYPTION_KEY", previousKey);
            await Sql(admin, $"DROP SCHEMA {schema} CASCADE");
        }
    }
    private static async Task Sql(NpgsqlConnection connection, string sql)
    { await using var command = new NpgsqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
    private static async Task<object?> Invoke(string name, params object?[] args)
    {
        var method = typeof(CrmErpIntegrationModule).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        var task = (Task)method.Invoke(null, args)!; await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }
    private sealed class Factory(ControlledHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false) { Timeout = TimeSpan.FromSeconds(10) };
    }
    private sealed class ControlledHandler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Target = "", Body = ""; internal int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Target = request.RequestUri!.ToString(); Body = await request.Content!.ReadAsStringAsync(token);
            Started.TrySetResult(); await Release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"next-access\",\"refresh_token\":\"next-refresh\",\"expires_in\":3600}", Encoding.UTF8, "application/json") };
        }
    }
}
