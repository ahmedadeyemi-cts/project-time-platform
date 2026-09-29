using System.Reflection;
using Microsoft.AspNetCore.Builder;
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
            foreach(var role in new[]{1,2,3})
            {
                Check(await ResultStatus(typeof(AdminExperienceCommon),"AuthorizeAsync",Context(role),false)==(role==1?403:200),
                    "Maintenance administration cannot be delegated through SYSTEM_ADMINISTRATION");
                var method=typeof(CiCdPipelineModule).GetMethod("RequireAdminAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
                var failure=await (Task<IResult?>)method.Invoke(null,new object[]{Context(role)})!;
                Check((failure is null?200:((IStatusCodeHttpResult)failure).StatusCode)==(role==1?403:200),
                    "CI/CD dispatch and rollback require administrator roles");
            }
            var schedule=typeof(CelarAiRuntimeVersionModule).GetMethod("UpdateScheduleAsync",BindingFlags.Static|BindingFlags.NonPublic)!;
            foreach(var context in new[]{Context(1),Context(3,true)})
            {
                var result=await (Task<IResult>)schedule.Invoke(null,new object[]{new CelarAiRuntimeMaintenanceScheduleRequest(true,"Monday","03:00","UTC"),context,CancellationToken.None})!;
                Check(((IStatusCodeHttpResult)result).StatusCode==403,"Unauthorized maintenance requests are blocked before contacting the VM");
            }
            await using(var connection=new NpgsqlConnection(builder.ConnectionString))
            {
                await connection.OpenAsync();await using var transaction=await connection.BeginTransactionAsync();
                var targetUser=Guid.Parse("10000000-0000-0000-0000-000000000003");
                foreach(var role in new[]{1,2,3})
                {
                    var failure=await PasswordResetTargetSafety.ValidateAsync(Context(role),connection,transaction,targetUser,"protected@example.invalid");
                    Check((failure is null?200:((IStatusCodeHttpResult)failure).StatusCode)==(role==3?200:403),
                        "Reset completion protects Super Administrator targets");
                    failure=await PasswordResetTargetSafety.ValidateAsync(Context(role),connection,transaction,targetUser,
                        Environment.GetEnvironmentVariable("PROJECTPULSE_BREAK_GLASS_ACCOUNT")??"ahmed.adeyemi@ussignal.local");
                    Check(((IStatusCodeHttpResult)failure!).StatusCode==403,"Break-glass passwords cannot be reset through completion");
                }
                Check(await PasswordResetTargetSafety.ValidateAsync(Context(3,true),connection,transaction,targetUser,"protected@example.invalid") is not null,
                    "View-As cannot reset a protected password");
                Check(await PasswordResetTargetSafety.ValidateAsync(Context(2),connection,transaction,Guid.Parse("10000000-0000-0000-0000-000000000004"),"ordinary.local") is null,
                    "Target protection preserves an authorized ordinary-account reset");
                await transaction.RollbackAsync();
            }
            // Exercise the real compatibility middleware with a delegated, authenticated actor.
            var webBuilder=WebApplication.CreateBuilder(new WebApplicationOptions {Args=Array.Empty<string>(),EnvironmentName="Test"});
            await using(var app=webBuilder.Build())
            {
                app.UseCanonicalApiPaths();app.UseMicrosoftIntegrationSecurityCompatibility();
                var reached=false;((IApplicationBuilder)app).Run(context=>{reached=true;return Task.CompletedTask;});
                var pipeline=((IApplicationBuilder)app).Build();
                await Sql("DELETE FROM app_role_permissions WHERE app_role_id=1 AND app_permission_id=3");
                foreach(var route in new[]{"directory-users/import-selected","client-secret"})
                foreach(var variant in new[]{0,1,2})
                {
                    var path="/api/microsoft-integration/"+route;
                    if(variant==1)path+="/";if(variant==2)path=path.ToUpperInvariant()+"/";
                    var context=Context(1);context.RequestServices=app.Services;context.Request.Path=path;
                    context.Request.Method=route=="client-secret"?"PUT":"POST";
                    context.Request.ContentType="application/json";
                    var bytes=Encoding.UTF8.GetBytes("{\"defaultRoleCode\":\"SUPER_ADMINISTRATOR\",\"clientSecret\":\"synthetic-secret\"}");
                    context.Request.Body=new MemoryStream(bytes);context.Request.ContentLength=bytes.Length;context.Response.Body=new MemoryStream();
                    reached=false;await pipeline(context);
                    Check(!reached && context.Response.StatusCode==(route=="client-secret"?403:400),
                        "Delegated system authority cannot write secrets or select privileged import roles: "+path);
                }
                await Sql("INSERT INTO app_role_permissions VALUES(1,3)");
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
            await Sql("""
                INSERT INTO app_roles VALUES(5,'PROJECT_MANAGEMENT',TRUE);
                INSERT INTO app_permissions VALUES(5,'MANAGE_PROJECT_ASSIGNMENTS');
                INSERT INTO app_role_permissions VALUES(5,5);
                INSERT INTO app_users(user_id,email) VALUES('10000000-0000-0000-0000-000000000005','pm@example.invalid');
                INSERT INTO app_user_role_assignments VALUES('10000000-0000-0000-0000-000000000005',5,TRUE);
                CREATE TABLE project_intake_requests(project_intake_request_id uuid,requested_by_user_id uuid,assigned_pm_user_id uuid,account_executive_user_id uuid,solution_architect_user_id uuid);
                INSERT INTO project_intake_requests VALUES('90000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000004',NULL,NULL,NULL);
                """);
            foreach(var format in new[]{"D","N","B","P"})
            foreach(var suffix in new[]{"post-intake","supporting-documents/upload","project-link"})
            {
                var context=Context(5);context.RequestServices=services;
                context.Request.Path="/api/project-intake/"+Guid.Parse("90000000-0000-0000-0000-000000000001").ToString(format)+"/"+suffix+"/";
                context.Request.ContentType="application/json";var bytes=Encoding.UTF8.GetBytes("{}");
                context.Request.Body=new MemoryStream(bytes);context.Request.ContentLength=bytes.Length;context.Response.Body=new MemoryStream();
                var reached=false;Func<Task> next=()=>{reached=true;return Task.CompletedTask;};
                await (Task)invoke.Invoke(null,new object[]{context,next})!;
                var response=Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
                Check(!reached && context.Response.StatusCode==403 && response.Contains("intake_access_denied"),
                    "Foreign intake rejects alternate GUID spelling "+format+"/"+suffix+": "+response);
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
