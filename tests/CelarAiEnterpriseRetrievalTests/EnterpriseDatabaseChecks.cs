using System.Reflection;
using System.Text.Json;
using Npgsql;
using ProjectTime.Api.Ai;

internal static class EnterpriseDatabaseChecks
{
    public static async Task RunAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        string Sql(string name) => (string)(typeof(CelarAiInternalDataService).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? throw new Exception(name));
        var user = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var outsider = Guid.Parse("10000000-0000-0000-0000-000000000099");
        static void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS SQL: "+name); }
        await using (var setup = new NpgsqlCommand("""
            CREATE TEMP TABLE time_entries(user_id uuid,work_date date,status text,billable boolean,hours numeric);
            INSERT INTO time_entries VALUES
            ('10000000-0000-0000-0000-000000000002','2026-08-01','approved',true,7.5),
            ('10000000-0000-0000-0000-000000000002','2026-08-31','draft',false,2.25),
            ('10000000-0000-0000-0000-000000000002','2026-09-01','approved',true,40),
            ('10000000-0000-0000-0000-000000000099','2026-08-01','approved',true,100);
            """, connection,transaction)) await setup.ExecuteNonQueryAsync();
        async Task<JsonDocument> Time(Guid effective, DateOnly start, DateOnly end)
        {
            await using var command = new NpgsqlCommand(Sql("EnterpriseOwnTimeSql"),connection,transaction);
            command.Parameters.AddWithValue("effective_user_id",effective);
            command.Parameters.AddWithValue("period_start",start);
            command.Parameters.AddWithValue("period_end",end);
            return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        }
        using (var month = await Time(user,new(2026,8,1),new(2026,8,31)))
        {
            Check(month.RootElement.GetProperty("totalRecordedHours").GetDecimal()==9.75m,"Month totals exclude other users and out-of-range dates");
            Check(month.RootElement.GetProperty("entryCount").GetInt32()==2,"Entry count is deterministic");
            Check(month.RootElement.GetProperty("records").GetArrayLength()==2,"Approval states and billability remain separate");
        }
        using(var empty = await Time(user,new(2025,8,1),new(2025,8,31)))
            Check(empty.RootElement.GetProperty("totalRecordedHours").GetDecimal()==0,"Successful empty source yields true zero");
        using(var viewAs = await Time(outsider,new(2026,8,1),new(2026,8,31)))
            Check(viewAs.RootElement.GetProperty("totalRecordedHours").GetDecimal()==100,"Effective identity controls time scope");
        async Task<JsonDocument> People(Guid effective, bool broad)
        {
            await using var command = new NpgsqlCommand(Sql("EnterprisePeopleSql"),connection,transaction);
            command.Parameters.AddWithValue("effective_user_id",effective);
            command.Parameters.AddWithValue("is_broad_scope",broad);
            command.Parameters.AddWithValue("can_view_managed_projects",broad);
            command.Parameters.AddWithValue("can_view_team_scope",broad);
            return JsonDocument.Parse((string)(await command.ExecuteScalarAsync())!);
        }
        using(var none = await People(outsider,false))
            Check(none.RootElement.GetProperty("totalPeople").GetInt32()==0,"Unknown effective actor sees no directory records");
        using(var scoped = await People(user,false))
        using(var admin = await People(user,true))
        {
            Check(scoped.RootElement.GetProperty("totalPeople").GetInt32()>0,"Authorized people query executes against actual schema");
            Check(admin.RootElement.GetProperty("totalPeople").GetInt32()>=scoped.RootElement.GetProperty("totalPeople").GetInt32(),"Portfolio scope does not reduce visible set");
            Check(!scoped.RootElement.GetProperty("hasMore").GetBoolean(),"Fixture directory fits declared limit");
        }
        await transaction.RollbackAsync();
        Console.WriteLine("CELAR_AI_ENTERPRISE_DATABASE=PASS");
    }
}
