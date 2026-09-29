using System.Reflection;
using System.Text.Json;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ProjectTime.Api.Modules;

internal static class PrivilegedIntegrationTests
{
    internal static async Task RunAsync()
    {
        var configured = Environment.GetEnvironmentVariable("SECURITY_TEST_DB");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Console.WriteLine("SECURITY_PRIVILEGED_INTEGRATION=NOT_RUN isolated_database_not_configured");
            return;
        }
        // A fresh schema lets the real authorization resolvers open their own connections.
        // No application table is read, overwritten or reused.
        var schema = "security_fixture_" + Guid.NewGuid().ToString("N");
        var builder = new NpgsqlConnectionStringBuilder(configured) { SearchPath = schema, Pooling = false };
        async Task Sql(string sql)
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var c = new NpgsqlCommand($"SET search_path TO {schema}; " + sql, connection); await c.ExecuteNonQueryAsync();
        }
        await Sql($"CREATE SCHEMA {schema};");
        var prior = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
        try
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", builder.ConnectionString);
            await Sql("""
                CREATE TABLE app_roles(app_role_id int PRIMARY KEY,role_code text,is_active boolean DEFAULT TRUE);
                CREATE TABLE app_permissions(app_permission_id int PRIMARY KEY,permission_code text);
                CREATE TABLE app_role_permissions(app_role_id int,app_permission_id int);
                CREATE TABLE app_user_role_assignments(user_id uuid,app_role_id int,is_active boolean DEFAULT TRUE);
                INSERT INTO app_roles VALUES(1,'PROJECT_TEAM_COORDINATOR'),(2,'ADMINISTRATOR'),(3,'SUPER_ADMINISTRATOR'),(4,'ENGINEERING');
                CREATE TABLE app_users(user_id uuid PRIMARY KEY,is_active boolean DEFAULT TRUE,login_enabled boolean DEFAULT TRUE,
                    job_title text,department_name text,department text,team_name text,email text);
                INSERT INTO app_users(user_id,email) VALUES
                    ('10000000-0000-0000-0000-000000000001','ptc@example.invalid'),
                    ('10000000-0000-0000-0000-000000000002','admin@example.invalid'),
                    ('10000000-0000-0000-0000-000000000003','protected@example.invalid');
                INSERT INTO app_users(user_id,job_title,department_name,department,team_name)
                    VALUES('10000000-0000-0000-0000-000000000004','Super Administrator','Accounting','Finance','Executive');
                INSERT INTO app_permissions VALUES(1,'SYSTEM_ADMINISTRATION'),(2,'MANAGE_ALL'),(3,'MANAGE_ENTRA_SECRET'),(4,'MANAGE_GLOBAL_MAIL_CONFIGURATION');
                INSERT INTO app_role_permissions SELECT 1,app_permission_id FROM app_permissions;
                INSERT INTO app_user_role_assignments VALUES
                    ('10000000-0000-0000-0000-000000000001',1),
                    ('10000000-0000-0000-0000-000000000002',2),
                    ('10000000-0000-0000-0000-000000000003',3),
                    ('10000000-0000-0000-0000-000000000004',4);
                """);
            DefaultHttpContext Context(int role, bool viewAs = false)
            {
                var c = new DefaultHttpContext();
                var user = Guid.Parse($"10000000-0000-0000-0000-{role:D12}");
                c.Items["ProjectPulseActualUserId"] = user;
                c.Items["ProjectPulseSessionUserId"] = user;
                c.Items["ProjectPulseEffectiveUserId"] = user;
                c.Items["ProjectPulseIsViewAs"] = viewAs;
                c.Request.Method = "POST";
                return c;
            }
            async Task<int> ResultStatus(Type type, string method, params object[] args)
            {
                var target = type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!;
                var task = (Task)target.Invoke(null,args)!; await task;
                var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
                var failure = result.GetType().GetProperty("Failure")!.GetValue(result);
                // Platform operations returns an owned open connection on successful admission.
                if (result.GetType().GetProperty("Connection")?.GetValue(result) is NpgsqlConnection connection)
                    await connection.DisposeAsync();
                return failure is null ? 200 : ((IStatusCodeHttpResult)failure).StatusCode ?? 500;
            }
            foreach (var role in new[] {1,2,3})
            {
                var expected = role == 3 ? 200 : 403;
                Check(await ResultStatus(typeof(MicrosoftServicesRuntimeCompatibility),"ResolveAccessAsync",Context(role)) == expected,
                    "Services profile activation requires permanent administrator authority");
                Check(await ResultStatus(typeof(MicrosoftSsoConnectionProfilesModule),"ResolveAccessAsync",Context(role),true) == expected,
                    "Delegated secret/mail/system permissions cannot replace identity-provider authority");
                Check(await ResultStatus(typeof(PlatformOperationsModule),"AuthorizeAsync",Context(role),false,true) == (role==1?403:200),
                    "Production-resilience data requires an administrator role");
            }
            Check(await ResultStatus(typeof(MicrosoftSsoConnectionProfilesModule),"ResolveAccessAsync",Context(1),false)==200,
                "Delegated Microsoft read access remains available");
            foreach (var type in new[] { typeof(MicrosoftServicesRuntimeCompatibility),typeof(MicrosoftSsoConnectionProfilesModule) })
            {
                var args=type==typeof(MicrosoftServicesRuntimeCompatibility)?new object[]{Context(3,true)}:new object[]{Context(3,true),true};
                Check(await ResultStatus(type,"ResolveAccessAsync",args)==403,"View-As cannot activate identity profiles");
            }
            var contracts = typeof(ContractsPrepaidManagementModule);
            // Real middleware and database guard: no endpoint is invoked for a
            // protected account, regardless of canonical casing/trailing slash.
            using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
            var invoke = typeof(SecurityHardeningModule).GetMethod("InvokeAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
            foreach(var suffix in new[]{"users/email","users/profile","users/roles","local-password","users/deactivate","users/delete","users/bulk-update"})
            foreach(var spelling in new[]{"/api/admin/user-admin/"+suffix,"/api/admin/user-admin/"+suffix+"/",("/api/admin/user-admin/"+suffix).ToUpperInvariant()+"/"})
            {
                var context=Context(2);context.RequestServices=services;context.Request.Path=spelling;
                context.Request.ContentType="application/json";
                var payload=suffix.EndsWith("bulk-update")
                    ? "{\"userIds\":[\"10000000-0000-0000-0000-000000000003\"]}"
                    : "{\"userId\":\"10000000-0000-0000-0000-000000000003\"}";
                var body=Encoding.UTF8.GetBytes(payload);context.Request.Body=new MemoryStream(body);context.Request.ContentLength=body.Length;
                context.Response.Body=new MemoryStream();var reached=false;
                Func<Task> next=()=>{reached=true;return Task.CompletedTask;};
                await (Task)invoke.Invoke(null,new object[]{context,next})!;
                Check(!reached && context.Response.StatusCode==403,"Administrator cannot modify protected account through "+spelling+" status="+context.Response.StatusCode+" response="+Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
            }
            var target=typeof(SecurityHardeningModule).GetMethod("TargetsExistingSuperAdministratorAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
            await Sql("UPDATE app_users SET is_active=FALSE WHERE email='protected@example.invalid'");
            await using(var connection=new NpgsqlConnection(builder.ConnectionString))
            {
                await connection.OpenAsync();
                Check(await (Task<bool>)target.Invoke(null,new object[]{connection,Array.Empty<Guid>(),"PROTECTED@example.invalid"})!,
                    "Disabled protected account remains protected when addressed by email");
            }
            await Sql("UPDATE app_users SET is_active=TRUE WHERE email='protected@example.invalid'");
            var eligible = contracts.GetMethod("GetEligibleAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
            var engineer = Context(4); engineer.Request.Method="GET";
            engineer.Request.QueryString=new QueryString("?clientId=20000000-0000-0000-0000-000000000001");
            var eligibleTask=(Task<IResult>)eligible.Invoke(null,new object[]{engineer})!;
            Check(((IStatusCodeHttpResult)await eligibleTask).StatusCode==403,
                "Engineer cannot enumerate contract balances, including with misleading HR titles");
            var usage=contracts.GetMethod("RecordUsageAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
            var request=JsonSerializer.Deserialize("""
                {"TimeEntryId":"30000000-0000-0000-0000-000000000001","ContractId":"40000000-0000-0000-0000-000000000001",
                 "ProjectId":"50000000-0000-0000-0000-000000000001","WorkDate":"2026-09-29","Hours":1,"BillingRate":100}
                """,usage.GetParameters()[0].ParameterType)!;
            var usageTask=(Task<IResult>)usage.Invoke(null,new object[]{request,Context(4)})!;
            Check(((IStatusCodeHttpResult)await usageTask).StatusCode==403,
                "Engineer cannot record contract usage, including with misleading HR titles");
            await Sql("UPDATE app_user_role_assignments SET is_active=FALSE WHERE app_role_id=3;");
            Check(await ResultStatus(typeof(MicrosoftServicesRuntimeCompatibility),"ResolveAccessAsync",Context(3))==403,
                "Revoked administrator assignment cannot activate a profile");
            Check(await ResultStatus(typeof(MicrosoftSsoConnectionProfilesModule),"ResolveAccessAsync",Context(3),true)==403,
                "Revoked administrator assignment cannot change SSO credentials");
            Console.WriteLine($"SECURITY_PRIVILEGED_INTEGRATION=PASS assertions={count}");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection",prior);
            await Sql($"SET search_path TO public; DROP SCHEMA {schema} CASCADE;");
        }
    }
}
