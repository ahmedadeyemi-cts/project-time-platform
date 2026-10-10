using System.Data;
using System.Text.Json;
using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class InvoiceBillingModule
{
    private static void MapAccountingEndpoints(WebApplication app)
    {
        app.MapGet("/api/billing/projects/{projectId:guid}/accounting", (Func<Guid,HttpContext,Task<IResult>>)GetAccountingAsync);
        app.MapPost("/api/billing/projects/{projectId:guid}/accounting", (Func<Guid,AccountingCommand,HttpContext,Task<IResult>>)SaveAccountingAsync);
    }

    private static async Task<bool> AccountingReady(NpgsqlConnection c, NpgsqlTransaction? tx = null)
    {
        await using var cmd = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id='135_accounting_engagement_reporting')", c, tx);
        return (bool)(await cmd.ExecuteScalarAsync())!;
    }
    private static IResult AccountingUnavailable() => Results.Json(new {message="Accounting reporting requires the accounting database update."},statusCode:503);
    private static async Task<JsonElement[]> AccountingRows(NpgsqlConnection c,string source,Guid projectId)
    {
        // Source names are internal constants only, never request input.
        await using var cmd = new NpgsqlCommand($"SELECT row_to_json(s)::text FROM {source} s WHERE project_id=@p",c);
        cmd.Parameters.AddWithValue("p",projectId);
        var rows = new List<JsonElement>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while(await reader.ReadAsync()) { using var doc = JsonDocument.Parse(reader.GetString(0)); rows.Add(doc.RootElement.Clone()); }
        return rows.ToArray();
    }
    private static async Task<IResult> GetAccountingAsync(Guid projectId,HttpContext context)
    {
        var actor=GetSessionUserId(context); if(actor is null) return SessionRequired();
        if(BillingViewAs(context)) return Results.Forbid();
        var config=InvoiceBillingDatabaseConfig.FromEnvironment(); if(ValidateConfig(config) is {} error) return error;
        await using var c=new NpgsqlConnection(config.ConnectionString); await c.OpenAsync(context.RequestAborted);
        var access=await LoadAccessContextAsync(c,actor.Value);
        if(!CanApproveBillingException(access) || !await CanAccessProjectAsync(c,access,projectId)) return Results.NotFound();
        if(!await AccountingReady(c)) return AccountingUnavailable();
        return Results.Ok(new {projectId,canManage=true,currency="USD",
            summary=await AccountingRows(c,"accounting_engagement_report",projectId),
            milestones=await AccountingRows(c,"accounting_milestone_report",projectId),
            time=await AccountingRows(c,"accounting_time_report",projectId),
            revenue=await AccountingRows(c,"accounting_revenue_report",projectId),
            entries=await AccountingRows(c,"accounting_entries",projectId)});
    }
    private static async Task<IResult> SaveAccountingAsync(Guid projectId,AccountingCommand request,HttpContext context)
    {
        var actor=GetSessionUserId(context); if(actor is null) return SessionRequired();
        if(BillingViewAs(context)) return Results.Forbid();
        if(AccountingPolicy.Validate(request,DateOnly.FromDateTime(DateTime.UtcNow)) is {} validation)
            return Results.BadRequest(new {message=validation});
        var config=InvoiceBillingDatabaseConfig.FromEnvironment(); if(ValidateConfig(config) is {} error) return error;
        await using var c=new NpgsqlConnection(config.ConnectionString); await c.OpenAsync(context.RequestAborted);
        if(!await AccountingReady(c)) return AccountingUnavailable();
        await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,context.RequestAborted);
        try
        {
            var access=await LoadAccessContextAsync(c,actor.Value,tx);
            if(!CanApproveBillingException(access) || !await CanAccessProjectAsync(c,access,projectId,tx)) return Results.Forbid();
            var project=await LoadProjectForInvoiceAsync(c,tx,projectId); if(project is null) return Results.NotFound();
            var hash=BillingHash(JsonSerializer.Serialize(new {projectId,actor,request},ManualJson));
            await using(var replay=new NpgsqlCommand("SELECT request_hash FROM accounting_operations WHERE operation_id=@op",c,tx))
            {
                replay.Parameters.AddWithValue("op",request.OperationId);
                if(await replay.ExecuteScalarAsync() is string prior)
                    return prior==hash ? Results.Ok(new {status="already_recorded"}) : Results.Conflict(new {message="Operation ID already belongs to another accounting request."});
            }
            var sql="";
            if(request.Action=="profile")
            {
                await using(var identityLock=new NpgsqlCommand("SELECT client_id FROM clients WHERE client_id=(SELECT client_id FROM projects WHERE project_id=@p) FOR UPDATE",c,tx))
                { identityLock.Parameters.AddWithValue("p",projectId);if(await identityLock.ExecuteScalarAsync() is null) return Results.Conflict(new {message="Link this engagement to a customer first."}); }
                await using(var version=new NpgsqlCommand("SELECT md5(COALESCE(to_jsonb(f)::text,'') || COALESCE(c.salesforce_account_id,'')) FROM projects p LEFT JOIN clients c USING(client_id) LEFT JOIN accounting_engagement_profiles f USING(project_id) WHERE p.project_id=@p",c,tx))
                { version.Parameters.AddWithValue("p",projectId);if((string?)await version.ExecuteScalarAsync()!=request.ExpectedVersion) return Results.Conflict(new {message="Contract or customer identity changed. Reload and review before saving."}); }
                await using(var original=new NpgsqlCommand("SELECT original_contract_amount FROM accounting_engagement_profiles WHERE project_id=@p",c,tx))
                { original.Parameters.AddWithValue("p",projectId);if(await original.ExecuteScalarAsync() is decimal saved && saved!=request.Amount) return Results.Conflict(new {message="Original contract amount is already recorded. Use an approved contract change entry to adjust its total."}); }
                await using var audit=new NpgsqlCommand("""
                    INSERT INTO accounting_profile_audit(audit_id,project_id,actor_user_id,prior_state,new_state)
                    SELECT @op,@p,@actor,jsonb_build_object('profile',(SELECT to_jsonb(f) FROM accounting_engagement_profiles f WHERE project_id=@p),
                        'salesforceAccountId',(SELECT salesforce_account_id FROM clients WHERE client_id=(SELECT client_id FROM projects WHERE project_id=@p))),@state::jsonb
                    """,c,tx);
                audit.Parameters.AddWithValue("op",request.OperationId);audit.Parameters.AddWithValue("p",projectId);
                audit.Parameters.AddWithValue("actor",actor.Value);audit.Parameters.AddWithValue("state",JsonSerializer.Serialize(request,ManualJson));
                await audit.ExecuteNonQueryAsync();
                sql="""
                    INSERT INTO accounting_engagement_profiles(project_id,contract_number,original_contract_amount,updated_by)
                    VALUES(@p,@reference,@amount,@actor) ON CONFLICT(project_id) DO UPDATE SET contract_number=EXCLUDED.contract_number,
                    original_contract_amount=EXCLUDED.original_contract_amount,updated_by=EXCLUDED.updated_by,updated_at=now();
                    UPDATE clients SET salesforce_account_id=@account WHERE client_id=(SELECT client_id FROM projects WHERE project_id=@p);
                    """;
            }
            else if(request.Action=="entry")
                sql="INSERT INTO accounting_entries(entry_id,project_id,kind,amount,effective_date,reference,reason,actor_user_id) VALUES(@op,@p,@kind,@amount,@date,@reference,@reason,@actor)";
            else if(request.Action=="milestone")
            {
                if(!IsFixedPrice(project.ContractType)) return Results.BadRequest(new {message="Billing milestones require a fixed-price engagement."});
                await using(var total=new NpgsqlCommand("SELECT f.original_contract_amount+COALESCE((SELECT sum(amount) FROM accounting_entries WHERE project_id=@p AND kind='contract_change' AND effective_date<=(now() AT TIME ZONE 'UTC')::date),0)-COALESCE((SELECT sum(amount) FROM accounting_milestones WHERE project_id=@p),0) FROM accounting_engagement_profiles f WHERE project_id=@p",c,tx))
                { total.Parameters.AddWithValue("p",projectId);if(await total.ExecuteScalarAsync() is not decimal available || request.Amount>available) return Results.Conflict(new {message="Verify the contract amount first. Scheduled milestones cannot exceed its current total."}); }
                sql="INSERT INTO accounting_milestones(milestone_id,project_id,name,amount,scheduled_date,created_by) VALUES(@op,@p,@name,@amount,@date,@actor)";
            }
            else if(request.Action=="accept")
                sql="""
                    UPDATE accounting_milestones SET accepted_date=@date,acceptance_reference=@reference,accepted_by=@actor
                    WHERE milestone_id=@target AND project_id=@p AND accepted_date IS NULL AND billing_invoice_id IS NULL
                    """;
            else if(request.Action=="rate")
                sql="""
                    INSERT INTO accounting_time_rates(rate_snapshot_id,project_id,time_entry_id,unit_rate,work_date,hours,reference,reason,actor_user_id)
                    SELECT @op,@p,time_entry_id,@amount,work_date,hours,@reference,@reason,@actor FROM time_entries
                    WHERE time_entry_id=@target AND project_id=@p AND billable=true AND hours>0
                    AND status IN ('manager_approved','project_approved','project_validated','pm_approved','accounting_ready','reconciled','locked')
                    AND NOT EXISTS(SELECT 1 FROM billing_invoice_lines l JOIN billing_invoices i USING(billing_invoice_id)
                        WHERE l.time_entry_id=@target AND i.invoice_status NOT IN ('void','voided','draft','cancelled','canceled'))
                    """;
            await using(var cmd=new NpgsqlCommand(sql,c,tx))
            {
                cmd.Parameters.AddWithValue("op",request.OperationId);cmd.Parameters.AddWithValue("p",projectId);
                cmd.Parameters.AddWithValue("actor",actor.Value);cmd.Parameters.AddWithValue("amount",request.Amount);
                cmd.Parameters.AddWithValue("reference",request.Reference.Trim());cmd.Parameters.AddWithValue("reason",request.Reason.Trim());
                cmd.Parameters.AddWithValue("kind",request.Kind);cmd.Parameters.AddWithValue("name",request.Name.Trim());
                cmd.Parameters.AddWithValue("account",request.SalesforceAccountId.Trim());
                cmd.Parameters.AddWithValue("date",request.Date);cmd.Parameters.AddWithValue("target",request.TargetId);
                if(await cmd.ExecuteNonQueryAsync()==0) return Results.Conflict(new {message="Record is no longer eligible. Reload and review its status."});
            }
            await using(var op=new NpgsqlCommand("INSERT INTO accounting_operations(operation_id,project_id,actor_user_id,request_hash,request_json) VALUES(@op,@p,@actor,@hash,@json::jsonb)",c,tx))
            {
                op.Parameters.AddWithValue("op",request.OperationId);op.Parameters.AddWithValue("p",projectId);op.Parameters.AddWithValue("actor",actor.Value);
                op.Parameters.AddWithValue("hash",hash);op.Parameters.AddWithValue("json",JsonSerializer.Serialize(request,ManualJson));await op.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();return Results.Ok(new {status="accounting_recorded"});
        }
        catch(PostgresException ex) when(ex.SqlState is "23505" or "40001" or "40P01")
        { await SafeRollbackAsync(tx);return Results.Conflict(new {message="Another financial operation changed this engagement. Reload and review before retrying."}); }
    }
    private static async Task<string?> ValidateMilestoneInvoice(NpgsqlConnection c,NpgsqlTransaction tx,Guid projectId,ManualInvoiceRequest request,decimal amount)
    {
        if(request.MilestoneId is null) return null;
        if(!await AccountingReady(c,tx)) return "Apply the accounting database update before milestone invoicing.";
        await using(var total=new NpgsqlCommand("SELECT original_contract_amount+COALESCE((SELECT sum(amount) FROM accounting_entries WHERE project_id=@p AND kind='contract_change' AND effective_date<=(now() AT TIME ZONE 'UTC')::date),0) FROM accounting_engagement_profiles WHERE project_id=@p",c,tx))
        { total.Parameters.AddWithValue("p",projectId);if(await total.ExecuteScalarAsync() is not decimal authorized || authorized!=request.AgreedTotal) return "The agreed invoice total must match the verified current contract amount."; }
        await using var cmd=new NpgsqlCommand("SELECT amount,accepted_date,billing_invoice_id FROM accounting_milestones WHERE project_id=@p AND milestone_id=@id FOR UPDATE",c,tx);
        cmd.Parameters.AddWithValue("p",projectId);cmd.Parameters.AddWithValue("id",request.MilestoneId.Value);
        await using var reader=await cmd.ExecuteReaderAsync();
        if(!await reader.ReadAsync()) return "Milestone was not found for this engagement.";
        if(reader.IsDBNull(1)) return "Record milestone acceptance before creating its invoice.";
        if(!reader.IsDBNull(2)) return "This milestone is already linked to an invoice. Review its invoice history.";
        return reader.GetDecimal(0)==amount ? null : "The new invoice charge must equal the accepted milestone amount.";
    }
}

public sealed record AccountingCommand(Guid OperationId,string Action,decimal Amount,DateOnly Date,
 string Reference,string Reason,string Kind="",string Name="",string SalesforceAccountId="",Guid TargetId=default,string ExpectedVersion="");
public static class AccountingPolicy
{
    public static string? Validate(AccountingCommand q,DateOnly today)
    {
        if(q.OperationId==Guid.Empty || q.Action is not("profile" or "entry" or "milestone" or "accept" or "rate")) return "Choose a valid accounting action.";
        if(q.Date==default || (q.Action!="milestone" && q.Date>today)) return "Enter a valid date; only scheduled milestones may use future dates.";
        if((q.Amount>999999999999.99m || q.Amount < -999999999999.99m) || decimal.Round(q.Amount,2)!=q.Amount) return "Use an amount with at most two decimal places.";
        if(q.Reference is null || q.Reference.Trim().Length<2 || q.Reference.Length>500 || q.Reason is null || q.Reason.Trim().Length<5 || q.Reason.Length>2000) return "Provide an authorization reference and audit reason.";
        if(q.Kind is null || q.Name is null || q.Name.Length>200 || q.SalesforceAccountId is null || (q.SalesforceAccountId.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(q.SalesforceAccountId,"^[a-zA-Z0-9]{15}([a-zA-Z0-9]{3})?$"))) return "Use a 15- or 18-character Salesforce Account ID, or leave it blank.";
        if(q.Action=="profile" && (q.Amount<0 || q.Reference.Length>200)) return "Contract amount must be nonnegative and contract number no more than 200 characters.";
        if(q.Action is "milestone" or "rate" && q.Amount<=0) return "Milestone amounts and confirmed rates must be positive.";
        if(q.Action=="milestone" && q.Name.Trim().Length<2) return "Provide a milestone name.";
        if(q.Action is "rate" or "accept" && q.TargetId==Guid.Empty) return "Select a record.";
        if(q.Action=="entry" && (q.Amount==0 || q.Kind is not("contract_change" or "prepaid_funding" or "prepaid_usage" or "revenue"))) return "Choose an entry type and nonzero amount. Corrections use signed adjusting entries.";
        return null;
    }
}
