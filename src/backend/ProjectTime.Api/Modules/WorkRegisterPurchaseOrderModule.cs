using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

public static class WorkRegisterPurchaseOrderModule
{
    public static WebApplication MapWorkRegisterPurchaseOrderEndpoints(this WebApplication app)
    {
        app.MapGet("/api/work-register/projects/purchase-orders", (Func<HttpContext, Task<IResult>>)GetProjectsAsync);
        app.MapPut("/api/work-register/projects/{projectId:guid}/purchase-order", (Func<Guid, WorkRegisterPurchaseOrderRequest, HttpContext, Task<IResult>>)SaveAsync);
        return app;
    }

    private static Guid? UserId(HttpContext c) => c.Items.TryGetValue("ProjectPulseSessionUserId", out var v) && v is Guid g ? g : null;
    private static IResult SessionRequired() => Results.Json(new { status = "session_required", message = "A valid ProjectPulse session is required." }, statusCode: 401);

    private static async Task<IResult> GetProjectsAsync(HttpContext context)
    {
        var userId = UserId(context); if (userId is null) return SessionRequired();
        var config = InvoiceBillingDatabaseConfig.FromEnvironment();
        if (config.Missing.Count > 0) return Results.BadRequest(new { status = "configuration_missing", missing = config.Missing });
        await using var connection = new NpgsqlConnection(config.ConnectionString); await connection.OpenAsync();
        var access = await AccessAsync(connection, userId.Value);
        if (!access.CanRead) return Results.Json(new { status = "access_denied" }, statusCode: 403);

        const string sql = """
            SELECT p.project_id, COALESCE(p.project_code,''), COALESCE(p.project_name,''), COALESCE(c.client_name,''),
                   COALESCE(profile.purchase_order_required,FALSE), po.po_number, po.authorized_amount,
                   po.effective_start_date, po.effective_end_date, po.customer_reference
            FROM projects p
            LEFT JOIN clients c ON c.client_id=p.client_id
            LEFT JOIN project_billing_profiles profile ON profile.project_id=p.project_id
            LEFT JOIN LATERAL (
              SELECT x.po_number,x.authorized_amount,x.effective_start_date,x.effective_end_date,x.customer_reference
              FROM project_purchase_orders x
              WHERE x.project_id=p.project_id AND x.is_primary=TRUE AND x.po_status='active'
              ORDER BY x.updated_at DESC LIMIT 1
            ) po ON TRUE
            WHERE @broad=TRUE OR p.project_manager_user_id=@user_id OR p.project_coordinator_user_id=@user_id
               OR EXISTS (SELECT 1 FROM project_assignments a WHERE a.project_id=p.project_id AND a.user_id=@user_id)
            ORDER BY p.project_code,p.project_name;
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("broad", access.Broad); cmd.Parameters.AddWithValue("user_id", userId.Value);
        var rows = new List<object>(); await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) rows.Add(new {
            projectId=r.GetGuid(0), projectCode=r.GetString(1), projectName=r.GetString(2), customerName=r.GetString(3),
            purchaseOrderRequired=r.GetBoolean(4), purchaseOrder=r.IsDBNull(5)?null:new {
                poNumber=r.GetString(5), authorizedAmount=r.IsDBNull(6)?(decimal?)null:r.GetDecimal(6),
                effectiveStartDate=r.IsDBNull(7)?null:r.GetValue(7), effectiveEndDate=r.IsDBNull(8)?null:r.GetValue(8),
                customerReference=r.IsDBNull(9)?"":r.GetString(9)
            }
        });
        return Results.Ok(new { status="work_register_purchase_orders_loaded", count=rows.Count, projects=rows });
    }

    private static async Task<IResult> SaveAsync(Guid projectId, WorkRegisterPurchaseOrderRequest request, HttpContext context)
    {
        var userId=UserId(context); if(userId is null) return SessionRequired();
        var po=(request.PoNumber??"").Trim(); var reference=(request.CustomerReference??"").Trim();
        if(request.PurchaseOrderRequired && string.IsNullOrWhiteSpace(po)) return Results.BadRequest(new { status="validation_failed", message="PO Number is required when PO Required is selected." });
        if(request.AuthorizedAmount is decimal a && a<0) return Results.BadRequest(new { status="validation_failed", message="Authorized amount cannot be negative." });
        if(request.EffectiveStartDate is DateOnly s && request.EffectiveEndDate is DateOnly e && e<s) return Results.BadRequest(new { status="validation_failed", message="Effective end date cannot be before start date." });
        if(string.IsNullOrWhiteSpace(request.ChangeReason)) return Results.BadRequest(new { status="validation_failed", message="A change reason is required for Work Register audit history." });
        var config=InvoiceBillingDatabaseConfig.FromEnvironment();
        if(config.Missing.Count>0) return Results.BadRequest(new { status="configuration_missing", missing=config.Missing });
        await using var connection=new NpgsqlConnection(config.ConnectionString); await connection.OpenAsync();
        await using var tx=await connection.BeginTransactionAsync(); var access=await AccessAsync(connection,userId.Value,tx);
        if(!access.CanWrite || !await CanAccessAsync(connection,tx,access,projectId)){await tx.RollbackAsync();return Results.Json(new { status="access_denied", message="Current role cannot modify this Work Register project." },statusCode:403);}
        var oldSnapshot="{}";
        await using(var before=new NpgsqlCommand("""
          SELECT jsonb_build_object(
            'purchaseOrderRequired',COALESCE(profile.purchase_order_required,FALSE),
            'poNumber',COALESCE(po.po_number,''),
            'authorizedAmount',po.authorized_amount,
            'effectiveStartDate',po.effective_start_date,
            'effectiveEndDate',po.effective_end_date,
            'customerReference',COALESCE(po.customer_reference,'')
          )::text
          FROM projects project
          LEFT JOIN project_billing_profiles profile ON profile.project_id=project.project_id
          LEFT JOIN LATERAL (
            SELECT candidate.po_number,candidate.authorized_amount,candidate.effective_start_date,candidate.effective_end_date,candidate.customer_reference
            FROM project_purchase_orders candidate
            WHERE candidate.project_id=project.project_id AND candidate.is_primary=TRUE AND candidate.po_status='active'
            ORDER BY candidate.updated_at DESC LIMIT 1
          ) po ON TRUE
          WHERE project.project_id=@project_id;
          """,connection,tx)){before.Parameters.AddWithValue("project_id",projectId);oldSnapshot=Convert.ToString(await before.ExecuteScalarAsync())??"{}";}
        await using(var profile=new NpgsqlCommand("""
          INSERT INTO project_billing_profiles(project_id,purchase_order_required,created_by_user_id,updated_by_user_id,created_at,updated_at)
          VALUES(@project_id,@required,@user_id,@user_id,NOW(),NOW())
          ON CONFLICT(project_id) DO UPDATE SET purchase_order_required=EXCLUDED.purchase_order_required,updated_by_user_id=EXCLUDED.updated_by_user_id,updated_at=NOW();
          """,connection,tx)){profile.Parameters.AddWithValue("project_id",projectId);profile.Parameters.AddWithValue("required",request.PurchaseOrderRequired);profile.Parameters.AddWithValue("user_id",userId.Value);await profile.ExecuteNonQueryAsync();}
        await using(var clear=new NpgsqlCommand("UPDATE project_purchase_orders SET is_primary=FALSE,updated_by_user_id=@user_id,updated_at=NOW() WHERE project_id=@project_id AND is_primary=TRUE;",connection,tx)){clear.Parameters.AddWithValue("project_id",projectId);clear.Parameters.AddWithValue("user_id",userId.Value);await clear.ExecuteNonQueryAsync();}
        if(!string.IsNullOrWhiteSpace(po)){
          await using var cmd=new NpgsqlCommand("""
            INSERT INTO project_purchase_orders(project_purchase_order_id,project_id,po_number,po_status,is_primary,authorized_amount,effective_start_date,effective_end_date,customer_reference,source_system,created_by_user_id,updated_by_user_id,created_at,updated_at)
            VALUES(gen_random_uuid(),@project_id,@po,'active',TRUE,@amount,@start,@end,@reference,'work_register',@user_id,@user_id,NOW(),NOW())
            ON CONFLICT(project_id,po_number) DO UPDATE SET po_status='active',is_primary=TRUE,authorized_amount=EXCLUDED.authorized_amount,effective_start_date=EXCLUDED.effective_start_date,effective_end_date=EXCLUDED.effective_end_date,customer_reference=EXCLUDED.customer_reference,source_system='work_register',updated_by_user_id=EXCLUDED.updated_by_user_id,updated_at=NOW();
            """,connection,tx);
          cmd.Parameters.AddWithValue("project_id",projectId);cmd.Parameters.AddWithValue("po",po);
          cmd.Parameters.Add("amount",NpgsqlDbType.Numeric).Value=request.AuthorizedAmount is decimal amount?amount:DBNull.Value;
          cmd.Parameters.Add("start",NpgsqlDbType.Date).Value=request.EffectiveStartDate is DateOnly start?start:DBNull.Value;
          cmd.Parameters.Add("end",NpgsqlDbType.Date).Value=request.EffectiveEndDate is DateOnly end?end:DBNull.Value;
          cmd.Parameters.AddWithValue("reference",reference);cmd.Parameters.AddWithValue("user_id",userId.Value);await cmd.ExecuteNonQueryAsync();
        }
        var newSnapshot=JsonSerializer.Serialize(new {
          purchaseOrderRequired=request.PurchaseOrderRequired,poNumber=po,request.AuthorizedAmount,
          request.EffectiveStartDate,request.EffectiveEndDate,customerReference=reference
        });
        await using(var audit=new NpgsqlCommand("""
          INSERT INTO work_register_change_history(
            work_register_change_history_id,source_table,work_id,action,change_summary,
            changed_fields_csv,changed_by_user_id,old_value_json,new_value_json)
          VALUES(gen_random_uuid(),'projects',@project_id,'purchase_order_updated',@reason,
            'Purchase Order Required, PO Number, Authorized Amount, Effective Dates, Customer Reference',
            @user_id,CAST(@old_value AS jsonb),CAST(@new_value AS jsonb));
          """,connection,tx)){
          audit.Parameters.AddWithValue("project_id",projectId);audit.Parameters.AddWithValue("reason",request.ChangeReason!.Trim());
          audit.Parameters.AddWithValue("user_id",userId.Value);audit.Parameters.AddWithValue("old_value",oldSnapshot);audit.Parameters.AddWithValue("new_value",newSnapshot);
          await audit.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync(); return Results.Ok(new { status="work_register_purchase_order_saved", projectId, purchaseOrderRequired=request.PurchaseOrderRequired, poNumber=po });
    }

    private static async Task<PoAccess> AccessAsync(NpgsqlConnection c, Guid userId, NpgsqlTransaction? tx=null)
    {
        const string sql="""SELECT COALESCE(array_agg(DISTINCT r.role_code) FILTER(WHERE r.role_code IS NOT NULL),ARRAY[]::text[]) FROM app_users u LEFT JOIN app_user_role_assignments a ON a.user_id=u.user_id AND a.is_active=TRUE LEFT JOIN app_roles r ON r.app_role_id=a.app_role_id AND r.is_active=TRUE WHERE u.user_id=@user_id AND u.is_active=TRUE GROUP BY u.user_id;""";
        await using var cmd=new NpgsqlCommand(sql,c,tx);cmd.Parameters.AddWithValue("user_id",userId);var result=await cmd.ExecuteScalarAsync();
        var roles=result is string[] arr?arr.ToHashSet(StringComparer.OrdinalIgnoreCase):new HashSet<string>(StringComparer.OrdinalIgnoreCase);return new PoAccess(userId,roles);
    }
    private static async Task<bool> CanAccessAsync(NpgsqlConnection c,NpgsqlTransaction tx,PoAccess a,Guid projectId)
    {
        const string sql="""SELECT EXISTS(SELECT 1 FROM projects p WHERE p.project_id=@project_id AND (@broad=TRUE OR p.project_manager_user_id=@user_id OR p.project_coordinator_user_id=@user_id OR EXISTS(SELECT 1 FROM project_assignments x WHERE x.project_id=p.project_id AND x.user_id=@user_id)));""";
        await using var cmd=new NpgsqlCommand(sql,c,tx);cmd.Parameters.AddWithValue("project_id",projectId);cmd.Parameters.AddWithValue("broad",a.Broad);cmd.Parameters.AddWithValue("user_id",a.UserId);return Convert.ToBoolean(await cmd.ExecuteScalarAsync()??false);
    }
}

internal sealed record WorkRegisterPurchaseOrderRequest(bool PurchaseOrderRequired,string? PoNumber,decimal? AuthorizedAmount,DateOnly? EffectiveStartDate,DateOnly? EffectiveEndDate,string? CustomerReference,string? ChangeReason);
internal sealed record PoAccess(Guid UserId,IReadOnlySet<string> Roles)
{
    private static readonly HashSet<string> BroadRoles=new(StringComparer.OrdinalIgnoreCase){"SUPER_ADMINISTRATOR","ADMINISTRATOR","PROJECT_TEAM_COORDINATOR","PROJECT_MANAGEMENT_LEAD","PROJECT_MANAGEMENT_TEAM_LEAD","PM_TEAM_LEAD","ACCOUNTING","ACCOUNTING_BILLING","BILLING","FINANCE","EXECUTIVE"};
    private static readonly HashSet<string> WriteRoles=new(StringComparer.OrdinalIgnoreCase){"SUPER_ADMINISTRATOR","ADMINISTRATOR","PROJECT_TEAM_COORDINATOR","ACCOUNTING","ACCOUNTING_BILLING","BILLING","FINANCE","PROJECT_MANAGEMENT","PROJECT_MANAGER","PROJECT_MANAGEMENT_LEAD","PROJECT_MANAGEMENT_TEAM_LEAD","PM_TEAM_LEAD"};
    public bool Broad=>Roles.Any(BroadRoles.Contains); public bool CanRead=>Roles.Count>0; public bool CanWrite=>Roles.Any(WriteRoles.Contains);
}
