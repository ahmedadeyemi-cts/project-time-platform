using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class ProjectManagementProductModule
{
    private const string MigrationId = "055_project_management_product";
    private static readonly string[] FieldTypes = ["text","multiline_text","number","integer","ip_address","cidr","date","datetime","email","phone","url","boolean","choice","secret_reference"];

    public static IEndpointRouteBuilder MapProjectManagementProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/project-management-product/overview", (Func<HttpContext,Task<IResult>>)OverviewAsync);
        endpoints.MapGet("/api/secure-project-information/templates", (Func<HttpContext,Task<IResult>>)ListTemplatesAsync);
        endpoints.MapPost("/api/secure-project-information/templates", (Func<SecureTemplateRequest,HttpContext,Task<IResult>>)CreateTemplateAsync);
        endpoints.MapPost("/api/secure-project-information/templates/{templateId:guid}/requests", (Func<Guid,SecureCollectionRequest,HttpContext,Task<IResult>>)CreateCollectionRequestAsync);
        endpoints.MapGet("/api/secure-project-information/templates/{templateId:guid}/export", (Func<Guid,HttpContext,Task<IResult>>)ExportTemplateAsync);
        endpoints.MapPost("/api/secure-project-information/import/preview", (Func<IFormFile,Guid?,HttpContext,Task<IResult>>)PreviewSecureImportAsync).DisableAntiforgery();
        endpoints.MapGet("/api/enterprise-pmo/portfolio", (Func<HttpContext,Task<IResult>>)PmoPortfolioAsync);
        endpoints.MapPost("/api/enterprise-pmo/workbook/preview", (Func<IFormFile,Guid?,HttpContext,Task<IResult>>)PreviewPmoWorkbookAsync).DisableAntiforgery();
        endpoints.MapGet("/api/project-flowhive/v2/plans", (Func<Guid?,HttpContext,Task<IResult>>)ListPlansAsync);
        endpoints.MapPost("/api/project-flowhive/v2/plans", (Func<FlowHivePlanRequest,HttpContext,Task<IResult>>)CreatePlanAsync);
        endpoints.MapPost("/api/project-flowhive/v2/plans/{planId:guid}/revisions", (Func<Guid,FlowHiveRevisionRequest,HttpContext,Task<IResult>>)CreateRevisionAsync);
        endpoints.MapPost("/api/project-flowhive/v2/revisions/{revisionId:guid}/submit", (Func<Guid,HttpContext,Task<IResult>>)SubmitRevisionAsync);
        endpoints.MapGet("/api/project-management-product/audit", (Func<Guid?,string?,HttpContext,Task<IResult>>)AuditAsync);
        return endpoints;
    }

    private static async Task<IResult> OverviewAsync(HttpContext context)
    {
        var access = await AccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        return Results.Ok(new
        {
            group = "8",
            modules = new[]
            {
                new { module="033", name="Secure Project Information Exchange", route="#secure-project-information", purpose="Typed, encrypted, project-scoped technical-information collection with secure portal and Excel roundtrip." },
                new { module="034", name="Enterprise PMO & Project Controls", route="#enterprise-pmo", purpose="PMI-aligned charter, RAID, change control, status reporting, baselines, and workbook governance." },
                new { module="066", name="Project FlowHive", route="#project-flowhive", purpose="Persistent plan revisions, deterministic dependencies, approval-gated immutable baselines, and governed customer artifacts." }
            },
            fieldTypes = FieldTypes,
            workbook = new
            {
                xlsx = "preview_supported",
                csv = "preview_supported",
                xlsb = "mapping_profile_required",
                requestedWorkbook = "EXCEL-UltimateProjectManagerDark.xlsb",
                exactMappingStatus = "source_workbook_not_present_in_repository_or_uploaded_file_library",
                rule = "No cell, formula, macro, or visual mapping is asserted until the authoritative workbook is supplied and checksum-verified."
            },
            security = new
            {
                valuesEncryptedAtRest = true,
                tokensStoredAsHashes = true,
                customerLinksExpireAndCanBeRevoked = true,
                immutableAudit = true,
                immutableApprovedBaselines = true,
                viewAsMutationBlocked = true
            },
            access = PublicActor(access.Actor!),
            generatedAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task<IResult> ListTemplatesAsync(HttpContext context)
    {
        var access = await AccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (!CanViewSecure(access.Actor!)) return Forbidden("Secure Project Information access is required.");
        try
        {
            await using var connection = await OpenAsync(context.RequestAborted);
            if (!await ReadyAsync(connection, context.RequestAborted)) return MigrationRequired();
            await using var command = new NpgsqlCommand("""
                SELECT template.secure_project_information_template_id,
                       template.template_code,
                       template.template_name,
                       template.description,
                       template.project_id,
                       template.customer_id,
                       template.lifecycle_status,
                       template.version_number,
                       template.created_at,
                       template.updated_at,
                       COUNT(field.secure_project_information_field_id)::integer
                FROM secure_project_information_templates template
                LEFT JOIN secure_project_information_fields field ON field.template_id=template.secure_project_information_template_id
                WHERE @broad OR template.project_id IS NULL OR template.project_id=ANY(@project_ids)
                GROUP BY template.secure_project_information_template_id
                ORDER BY template.updated_at DESC;
                """, connection);
            command.Parameters.AddWithValue("broad", access.Actor!.Broad);
            command.Parameters.Add(new NpgsqlParameter("project_ids",NpgsqlDbType.Array|NpgsqlDbType.Uuid){Value=access.ProjectIds});
            var rows = new List<object>();
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                rows.Add(new
                {
                    templateId=reader.GetGuid(0), code=reader.GetString(1), name=reader.GetString(2), description=reader.GetString(3),
                    projectId=reader.IsDBNull(4)?null:(Guid?)reader.GetGuid(4), customerId=reader.IsDBNull(5)?null:(Guid?)reader.GetGuid(5),
                    status=reader.GetString(6), version=reader.GetInt32(7), createdAt=reader.GetFieldValue<DateTimeOffset>(8),
                    updatedAt=reader.GetFieldValue<DateTimeOffset>(9), fieldCount=reader.GetInt32(10)
                });
            }
            return Results.Ok(new { module="033", status=rows.Count==0?"no_templates":"templates_loaded", templates=rows, fieldTypes=FieldTypes, access=PublicActor(access.Actor!) });
        }
        catch(Exception exception){ return SourceFailure(context,"secure_project_information",exception); }
    }

    private static async Task<IResult> CreateTemplateAsync(SecureTemplateRequest request, HttpContext context)
    {
        var access = await AccessAsync(context);
        if (access.Failure is not null) return access.Failure;
        if (!CanManageSecure(access.Actor!)) return Forbidden("Manage Secure Project Information authority is required.");
        if (access.Actor!.IsViewAs) return ViewAsReadOnly();
        var validation = ValidateTemplate(request, access.ProjectIds, access.Actor.Broad);
        if (validation is not null) return Results.BadRequest(validation);
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted);
            if(!await ReadyAsync(connection,context.RequestAborted)) return MigrationRequired();
            await using var transaction=await connection.BeginTransactionAsync(context.RequestAborted);
            var templateId=Guid.NewGuid();
            await using(var command=new NpgsqlCommand("""
                INSERT INTO secure_project_information_templates(secure_project_information_template_id,template_code,template_name,description,project_id,customer_id,lifecycle_status,version_number,created_by_user_id,updated_by_user_id)
                VALUES(@id,@code,@name,@description,@project_id,@customer_id,'draft',1,@user_id,@user_id);
                """,connection,transaction))
            {
                command.Parameters.AddWithValue("id",templateId); command.Parameters.AddWithValue("code",CleanCode(request.Code));
                command.Parameters.AddWithValue("name",Limit(request.Name,240)); command.Parameters.AddWithValue("description",Limit(request.Description??"",5000));
                AddNullable(command,"project_id",NpgsqlDbType.Uuid,request.ProjectId); AddNullable(command,"customer_id",NpgsqlDbType.Uuid,request.CustomerId);
                command.Parameters.AddWithValue("user_id",access.Actor.ActualUserId); await command.ExecuteNonQueryAsync(context.RequestAborted);
            }
            var order=0;
            foreach(var field in request.Fields)
            {
                order++;
                await using var command=new NpgsqlCommand("""
                    INSERT INTO secure_project_information_fields(template_id,field_key,field_label,field_type,display_order,required,sensitive,validation_json,help_text)
                    VALUES(@template_id,@key,@label,@type,@order,@required,@sensitive,@validation::jsonb,@help);
                    """,connection,transaction);
                command.Parameters.AddWithValue("template_id",templateId); command.Parameters.AddWithValue("key",CleanCode(field.Key));
                command.Parameters.AddWithValue("label",Limit(field.Label,240)); command.Parameters.AddWithValue("type",field.Type.ToLowerInvariant());
                command.Parameters.AddWithValue("order",order); command.Parameters.AddWithValue("required",field.Required); command.Parameters.AddWithValue("sensitive",field.Sensitive||field.Type.Equals("secret_reference",StringComparison.OrdinalIgnoreCase));
                command.Parameters.AddWithValue("validation",JsonSerializer.Serialize(field.Validation??new Dictionary<string,object?>())); command.Parameters.AddWithValue("help",Limit(field.HelpText??"",2000));
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await AuditAsync(connection,transaction,request.ProjectId,"033","secure_template",templateId,"template_created",access.Actor,context.TraceIdentifier,new{fieldCount=request.Fields.Length},context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Created($"/api/secure-project-information/templates/{templateId}",new{module="033",status="template_created",templateId,version=1});
        }
        catch(Exception exception){ return SourceFailure(context,"secure_project_information",exception); }
    }

    private static async Task<IResult> CreateCollectionRequestAsync(Guid templateId, SecureCollectionRequest request, HttpContext context)
    {
        var access=await AccessAsync(context); if(access.Failure is not null)return access.Failure;
        if(!CanManageSecure(access.Actor!))return Forbidden("Manage Secure Project Information authority is required.");
        if(access.Actor!.IsViewAs)return ViewAsReadOnly();
        if(!access.Actor.Broad&&!access.ProjectIds.Contains(request.ProjectId))return Forbidden("The project is outside the effective user's scope.");
        var rawToken=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var tokenHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        var expires=request.ExpiresAt??DateTimeOffset.UtcNow.AddDays(14);
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted); if(!await ReadyAsync(connection,context.RequestAborted))return MigrationRequired();
            await using var transaction=await connection.BeginTransactionAsync(context.RequestAborted);
            var requestId=Guid.NewGuid();
            await using(var command=new NpgsqlCommand("""
                INSERT INTO secure_project_information_requests(secure_project_information_request_id,template_id,project_id,customer_id,request_status,access_token_hash,recipient_email,expires_at,issued_by_user_id)
                VALUES(@id,@template_id,@project_id,@customer_id,'issued',@token_hash,@email,@expires,@user_id);
                """,connection,transaction))
            {
                command.Parameters.AddWithValue("id",requestId);command.Parameters.AddWithValue("template_id",templateId);command.Parameters.AddWithValue("project_id",request.ProjectId);
                AddNullable(command,"customer_id",NpgsqlDbType.Uuid,request.CustomerId);command.Parameters.AddWithValue("token_hash",tokenHash);command.Parameters.AddWithValue("email",Limit(request.RecipientEmail??"",320));command.Parameters.AddWithValue("expires",expires);command.Parameters.AddWithValue("user_id",access.Actor.ActualUserId);
                await command.ExecuteNonQueryAsync(context.RequestAborted);
            }
            await AuditAsync(connection,transaction,request.ProjectId,"033","collection_request",requestId,"request_issued",access.Actor,context.TraceIdentifier,new{expires,recipientEmail=request.RecipientEmail},context.RequestAborted);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new{module="033",status="collection_request_issued",requestId,expiresAt=expires,accessToken=rawToken,tokenReturnedOnce=true,deliveryOwner="Module 065",message="Store or deliver this one-time token through the governed Module 065 boundary. Only its SHA-256 hash is stored."});
        }
        catch(Exception exception){return SourceFailure(context,"secure_project_information",exception);}
    }

    private static async Task<IResult> ExportTemplateAsync(Guid templateId,HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanViewSecure(access.Actor!))return Forbidden("Secure Project Information access is required.");
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted);if(!await ReadyAsync(connection,context.RequestAborted))return MigrationRequired();
            await using var command=new NpgsqlCommand("""
              SELECT t.template_name,t.description,f.field_key,f.field_label,f.field_type,f.required,f.sensitive,f.help_text
              FROM secure_project_information_templates t JOIN secure_project_information_fields f ON f.template_id=t.secure_project_information_template_id
              WHERE t.secure_project_information_template_id=@id AND (@broad OR t.project_id IS NULL OR t.project_id=ANY(@project_ids)) ORDER BY f.display_order,f.field_label;
              """,connection);
            command.Parameters.AddWithValue("id",templateId);command.Parameters.AddWithValue("broad",access.Actor!.Broad);command.Parameters.Add(new NpgsqlParameter("project_ids",NpgsqlDbType.Array|NpgsqlDbType.Uuid){Value=access.ProjectIds});
            var fields=new List<TemplateExportField>();string name="Secure Project Information";string description="";
            await using(var reader=await command.ExecuteReaderAsync(context.RequestAborted))while(await reader.ReadAsync(context.RequestAborted)){name=reader.GetString(0);description=reader.GetString(1);fields.Add(new(reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetBoolean(5),reader.GetBoolean(6),reader.GetString(7)));}
            if(fields.Count==0)return Results.NotFound(new{module="033",status="template_not_found_or_outside_scope"});
            using var workbook=new XLWorkbook();var sheet=workbook.AddWorksheet("Customer Input");
            sheet.Cell(1,1).Value="US Signal ProjectPulse";sheet.Cell(2,1).Value=name;sheet.Cell(3,1).Value=description;
            string[] headers=["Field Key","Field Label","Type","Required","Sensitive","Customer Value","Guidance"];
            for(var i=0;i<headers.Length;i++)sheet.Cell(5,i+1).Value=headers[i];
            var row=6;foreach(var field in fields){sheet.Cell(row,1).Value=field.Key;sheet.Cell(row,2).Value=field.Label;sheet.Cell(row,3).Value=field.Type;sheet.Cell(row,4).Value=field.Required?"Yes":"No";sheet.Cell(row,5).Value=field.Sensitive?"Yes":"No";sheet.Cell(row,7).Value=field.HelpText;row++;}
            sheet.Range(5,1,5,7).Style.Font.Bold=true;sheet.Columns().AdjustToContents();sheet.SheetView.FreezeRows(5);
            using var stream=new MemoryStream();workbook.SaveAs(stream);return Results.File(stream.ToArray(),"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",$"{SafeFile(name)}-customer-input.xlsx");
        }
        catch(Exception exception){return SourceFailure(context,"secure_project_information",exception);}
    }

    private static async Task<IResult> PreviewSecureImportAsync(IFormFile file,Guid? templateId,HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanManageSecure(access.Actor!))return Forbidden("Manage Secure Project Information authority is required.");if(access.Actor!.IsViewAs)return ViewAsReadOnly();
        return await PreviewWorkbookAsync(file,templateId,"033",context);
    }

    private static async Task<IResult> PreviewPmoWorkbookAsync(IFormFile file,Guid? projectId,HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanManagePmo(access.Actor!))return Forbidden("Manage Enterprise PMO authority is required.");if(access.Actor!.IsViewAs)return ViewAsReadOnly();
        if(projectId.HasValue&&!access.Actor.Broad&&!access.ProjectIds.Contains(projectId.Value))return Forbidden("The project is outside the effective user's scope.");
        return await PreviewWorkbookAsync(file,projectId,"034",context);
    }

    private static async Task<IResult> PreviewWorkbookAsync(IFormFile file,Guid? targetId,string module,HttpContext context)
    {
        if(file.Length==0||file.Length>25*1024*1024)return Results.BadRequest(new{module,status="invalid_file",message="Upload a non-empty CSV, XLSX, or XLSB file no larger than 25 MB."});
        var extension=Path.GetExtension(file.FileName).ToLowerInvariant();if(extension is not(".xlsx" or ".csv" or ".xlsb"))return Results.BadRequest(new{module,status="unsupported_file_type",message="Supported file types are CSV, XLSX, and XLSB."});
        await using var input=file.OpenReadStream();using var memory=new MemoryStream();await input.CopyToAsync(memory,context.RequestAborted);var bytes=memory.ToArray();var sha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if(extension==".xlsb")return Results.Ok(new{module,status="mapping_required",file=file.FileName,format="xlsb",sha256=sha,message="The binary workbook is preserved as immutable evidence, but exact sheets, formulas, macros, named ranges, and visual mappings require the authoritative EXCEL-UltimateProjectManagerDark.xlsb workbook and an approved mapping profile. No mapping was invented.",canApply=false});
        if(extension==".csv")
        {
            var text=Encoding.UTF8.GetString(bytes);var lines=text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries);var headers=lines.FirstOrDefault()?.Split(',').Select(value=>value.Trim()).ToArray()??[];
            return Results.Ok(new{module,status="preview_ready",file=file.FileName,format="csv",sha256=sha,rowCount=Math.Max(0,lines.Length-1),headers,canApply=false,rule="Preview does not mutate project records."});
        }
        using var workbook=new XLWorkbook(new MemoryStream(bytes));var sheets=workbook.Worksheets.Select(sheet=>new{name=sheet.Name,usedRows=sheet.RangeUsed()?.RowCount()??0,usedColumns=sheet.RangeUsed()?.ColumnCount()??0,headers=sheet.FirstRowUsed()?.CellsUsed().Select(cell=>cell.GetString()).Take(40).ToArray()??[]}).ToArray();
        return Results.Ok(new{module,status="preview_ready",file=file.FileName,format="xlsx",sha256=sha,sheets,canApply=false,rule="Preview is evidence-only until a mapping profile is approved."});
    }

    private static async Task<IResult> PmoPortfolioAsync(HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanViewPmo(access.Actor!))return Forbidden("Enterprise PMO access is required.");
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted);if(!await ReadyAsync(connection,context.RequestAborted))return MigrationRequired();
            await using var command=new NpgsqlCommand("""
              SELECT p.project_id,p.project_code,p.project_name,COALESCE(c.client_name,''),COALESCE(pm.display_name,pm.email,''),
                     COALESCE(ep.governance_tier,'not_configured'),COALESCE(ep.methodology,'not_configured'),COALESCE(ep.charter_status,'not_configured'),COALESCE(ep.health_status,'not_assessed'),COALESCE(ep.current_phase,''),COALESCE(ep.baseline_version,0),
                     COUNT(DISTINCT raid.enterprise_pmo_raid_item_id) FILTER(WHERE raid.status NOT IN ('resolved','closed'))::integer,
                     COUNT(DISTINCT change.enterprise_pmo_change_request_id) FILTER(WHERE change.status IN ('submitted','in_review'))::integer
              FROM projects p LEFT JOIN clients c ON c.client_id=p.client_id LEFT JOIN app_users pm ON pm.user_id=p.project_manager_user_id
              LEFT JOIN enterprise_pmo_projects ep ON ep.project_id=p.project_id LEFT JOIN enterprise_pmo_raid_items raid ON raid.pmo_project_id=ep.enterprise_pmo_project_id
              LEFT JOIN enterprise_pmo_change_requests change ON change.pmo_project_id=ep.enterprise_pmo_project_id
              WHERE @broad OR p.project_id=ANY(@project_ids)
              GROUP BY p.project_id,p.project_code,p.project_name,c.client_name,pm.display_name,pm.email,ep.governance_tier,ep.methodology,ep.charter_status,ep.health_status,ep.current_phase,ep.baseline_version
              ORDER BY CASE COALESCE(ep.health_status,'not_assessed') WHEN 'red' THEN 0 WHEN 'amber' THEN 1 ELSE 2 END,c.client_name,p.project_name;
              """,connection);
            command.Parameters.AddWithValue("broad",access.Actor!.Broad);command.Parameters.Add(new NpgsqlParameter("project_ids",NpgsqlDbType.Array|NpgsqlDbType.Uuid){Value=access.ProjectIds});var rows=new List<object>();
            await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);while(await reader.ReadAsync(context.RequestAborted))rows.Add(new{projectId=reader.GetGuid(0),projectCode=reader.GetString(1),projectName=reader.GetString(2),customer=reader.GetString(3),projectManager=reader.GetString(4),governanceTier=reader.GetString(5),methodology=reader.GetString(6),charterStatus=reader.GetString(7),health=reader.GetString(8),phase=reader.GetString(9),baselineVersion=reader.GetInt32(10),openRaid=reader.GetInt32(11),pendingChanges=reader.GetInt32(12)});
            return Results.Ok(new{module="034",status=rows.Count==0?"no_projects":"portfolio_loaded",projects=rows,pmiDomains=new[]{"integration","scope","schedule","cost","quality","resource","communications","risk","procurement","stakeholder"},access=PublicActor(access.Actor!)});
        }
        catch(Exception exception){return SourceFailure(context,"enterprise_pmo",exception);}
    }

    private static async Task<IResult> ListPlansAsync(Guid? projectId,HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanViewFlowHive(access.Actor!))return Forbidden("FlowHive access is required.");
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted);if(!await ReadyAsync(connection,context.RequestAborted))return MigrationRequired();
            await using var command=new NpgsqlCommand("""
              SELECT plan.flowhive_plan_id,plan.project_id,p.project_code,p.project_name,plan.plan_name,plan.plan_status,plan.current_revision_number,plan.updated_at,
                     COUNT(DISTINCT revision.flowhive_plan_revision_id)::integer,COUNT(DISTINCT baseline.flowhive_baseline_id)::integer
              FROM flowhive_plans plan JOIN projects p ON p.project_id=plan.project_id LEFT JOIN flowhive_plan_revisions revision ON revision.plan_id=plan.flowhive_plan_id LEFT JOIN flowhive_baselines baseline ON baseline.plan_id=plan.flowhive_plan_id
              WHERE (@project_id IS NULL OR plan.project_id=@project_id) AND (@broad OR plan.project_id=ANY(@project_ids))
              GROUP BY plan.flowhive_plan_id,p.project_code,p.project_name ORDER BY plan.updated_at DESC;
              """,connection);
            AddNullable(command,"project_id",NpgsqlDbType.Uuid,projectId);command.Parameters.AddWithValue("broad",access.Actor!.Broad);command.Parameters.Add(new NpgsqlParameter("project_ids",NpgsqlDbType.Array|NpgsqlDbType.Uuid){Value=access.ProjectIds});var rows=new List<object>();
            await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);while(await reader.ReadAsync(context.RequestAborted))rows.Add(new{planId=reader.GetGuid(0),projectId=reader.GetGuid(1),projectCode=reader.GetString(2),projectName=reader.GetString(3),planName=reader.GetString(4),status=reader.GetString(5),currentRevision=reader.GetInt32(6),updatedAt=reader.GetFieldValue<DateTimeOffset>(7),revisionCount=reader.GetInt32(8),baselineCount=reader.GetInt32(9)});
            return Results.Ok(new{module="066",status=rows.Count==0?"no_plans":"plans_loaded",plans=rows,persistence="database",baselineGate="Module 002 approval plus immutable checksum",customerArtifactGate="approved baseline only",access=PublicActor(access.Actor!)});
        }
        catch(Exception exception){return SourceFailure(context,"flowhive",exception);}
    }

    private static async Task<IResult> CreatePlanAsync(FlowHivePlanRequest request,HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanManageFlowHive(access.Actor!))return Forbidden("Manage FlowHive authority is required.");if(access.Actor!.IsViewAs)return ViewAsReadOnly();if(!access.Actor.Broad&&!access.ProjectIds.Contains(request.ProjectId))return Forbidden("The project is outside the effective user's scope.");
        if(string.IsNullOrWhiteSpace(request.PlanName))return Results.BadRequest(new{module="066",status="plan_name_required"});
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted);if(!await ReadyAsync(connection,context.RequestAborted))return MigrationRequired();await using var transaction=await connection.BeginTransactionAsync(context.RequestAborted);var planId=Guid.NewGuid();
            await using(var command=new NpgsqlCommand("INSERT INTO flowhive_plans(flowhive_plan_id,project_id,plan_name,created_by_user_id) VALUES(@id,@project,@name,@user);",connection,transaction)){command.Parameters.AddWithValue("id",planId);command.Parameters.AddWithValue("project",request.ProjectId);command.Parameters.AddWithValue("name",Limit(request.PlanName,260));command.Parameters.AddWithValue("user",access.Actor.ActualUserId);await command.ExecuteNonQueryAsync(context.RequestAborted);}
            await AuditAsync(connection,transaction,request.ProjectId,"066","plan",planId,"plan_created",access.Actor,context.TraceIdentifier,new{request.PlanName},context.RequestAborted);await transaction.CommitAsync(context.RequestAborted);
            return Results.Created($"/api/project-flowhive/v2/plans/{planId}",new{module="066",status="persistent_plan_created",planId,currentRevision=1});
        }
        catch(Exception exception){return SourceFailure(context,"flowhive",exception);}
    }

    private static async Task<IResult> CreateRevisionAsync(Guid planId,FlowHiveRevisionRequest request,HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanManageFlowHive(access.Actor!))return Forbidden("Manage FlowHive authority is required.");if(access.Actor!.IsViewAs)return ViewAsReadOnly();
        if(request.Tasks.Length==0)return Results.BadRequest(new{module="066",status="tasks_required",message="At least one task or milestone is required."});
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted);if(!await ReadyAsync(connection,context.RequestAborted))return MigrationRequired();await using var transaction=await connection.BeginTransactionAsync(context.RequestAborted);
            Guid projectId;int revisionNumber;
            await using(var command=new NpgsqlCommand("SELECT project_id,current_revision_number+1 FROM flowhive_plans WHERE flowhive_plan_id=@id FOR UPDATE;",connection,transaction)){command.Parameters.AddWithValue("id",planId);await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);if(!await reader.ReadAsync(context.RequestAborted))return Results.NotFound(new{module="066",status="plan_not_found"});projectId=reader.GetGuid(0);revisionNumber=reader.GetInt32(1);}
            if(!access.Actor.Broad&&!access.ProjectIds.Contains(projectId))return Forbidden("The plan is outside the effective user's scope.");
            var revisionId=Guid.NewGuid();
            await using(var command=new NpgsqlCommand("""
              INSERT INTO flowhive_plan_revisions(flowhive_plan_revision_id,plan_id,revision_number,revision_label,project_start_date,project_finish_date,notes,source_provenance_json,ai_provenance_json,created_by_user_id)
              VALUES(@id,@plan,@number,@label,@start,@finish,@notes,@source::jsonb,@ai::jsonb,@user);
              UPDATE flowhive_plans SET current_revision_number=@number,updated_at=NOW() WHERE flowhive_plan_id=@plan;
              """,connection,transaction)){command.Parameters.AddWithValue("id",revisionId);command.Parameters.AddWithValue("plan",planId);command.Parameters.AddWithValue("number",revisionNumber);command.Parameters.AddWithValue("label",Limit(request.Label??$"Revision {revisionNumber}",180));AddNullable(command,"start",NpgsqlDbType.Date,request.StartDate);AddNullable(command,"finish",NpgsqlDbType.Date,request.FinishDate);command.Parameters.AddWithValue("notes",Limit(request.Notes??"",10000));command.Parameters.AddWithValue("source",JsonSerializer.Serialize(request.SourceProvenance??new{}));command.Parameters.AddWithValue("ai",JsonSerializer.Serialize(request.AiProvenance??new{provider="none",humanReviewRequired=true}));command.Parameters.AddWithValue("user",access.Actor.ActualUserId);await command.ExecuteNonQueryAsync(context.RequestAborted);}
            foreach(var task in request.Tasks){await using var command=new NpgsqlCommand("""
              INSERT INTO flowhive_revision_tasks(revision_id,wbs_number,parent_wbs_number,task_name,task_description,duration_working_days,planned_effort_hours,percent_complete,is_milestone,constraint_type,constraint_date,responsible_user_id,task_status)
              VALUES(@revision,@wbs,@parent,@name,@description,@duration,@effort,@complete,@milestone,@constraint,@constraint_date,@owner,@status);
              """,connection,transaction);command.Parameters.AddWithValue("revision",revisionId);command.Parameters.AddWithValue("wbs",Limit(task.Wbs,60));command.Parameters.AddWithValue("parent",Limit(task.ParentWbs??"",60));command.Parameters.AddWithValue("name",Limit(task.Name,320));command.Parameters.AddWithValue("description",Limit(task.Description??"",10000));command.Parameters.AddWithValue("duration",Math.Max(0,task.DurationWorkingDays));command.Parameters.AddWithValue("effort",Math.Max(0,task.PlannedEffortHours));command.Parameters.AddWithValue("complete",Math.Clamp(task.PercentComplete,0,100));command.Parameters.AddWithValue("milestone",task.IsMilestone);command.Parameters.AddWithValue("constraint",Limit(task.ConstraintType??"ASAP",30));AddNullable(command,"constraint_date",NpgsqlDbType.Date,task.ConstraintDate);AddNullable(command,"owner",NpgsqlDbType.Uuid,task.ResponsibleUserId);command.Parameters.AddWithValue("status",NormalizeTaskStatus(task.Status));await command.ExecuteNonQueryAsync(context.RequestAborted);}
            foreach(var dependency in request.Dependencies??[]){await using var command=new NpgsqlCommand("INSERT INTO flowhive_revision_dependencies(revision_id,predecessor_wbs,successor_wbs,dependency_type,lag_working_days,rationale) VALUES(@revision,@predecessor,@successor,@type,@lag,@rationale);",connection,transaction);command.Parameters.AddWithValue("revision",revisionId);command.Parameters.AddWithValue("predecessor",Limit(dependency.PredecessorWbs,60));command.Parameters.AddWithValue("successor",Limit(dependency.SuccessorWbs,60));command.Parameters.AddWithValue("type",NormalizeDependency(dependency.Type));command.Parameters.AddWithValue("lag",dependency.LagWorkingDays);command.Parameters.AddWithValue("rationale",Limit(dependency.Rationale??"",5000));await command.ExecuteNonQueryAsync(context.RequestAborted);}
            await AuditAsync(connection,transaction,projectId,"066","revision",revisionId,"revision_created",access.Actor,context.TraceIdentifier,new{revisionNumber,taskCount=request.Tasks.Length,dependencyCount=request.Dependencies?.Length??0},context.RequestAborted);await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new{module="066",status="persistent_revision_created",revisionId,revisionNumber,approvalRequired=true,baselineImmutable=true});
        }
        catch(Exception exception){return SourceFailure(context,"flowhive",exception);}
    }

    private static async Task<IResult> SubmitRevisionAsync(Guid revisionId,HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanManageFlowHive(access.Actor!))return Forbidden("Manage FlowHive authority is required.");if(access.Actor!.IsViewAs)return ViewAsReadOnly();
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted);if(!await ReadyAsync(connection,context.RequestAborted))return MigrationRequired();await using var transaction=await connection.BeginTransactionAsync(context.RequestAborted);
            Guid projectId;await using(var command=new NpgsqlCommand("""
              UPDATE flowhive_plan_revisions revision SET revision_status='submitted'
              FROM flowhive_plans plan WHERE revision.flowhive_plan_revision_id=@id AND plan.flowhive_plan_id=revision.plan_id AND revision.revision_status='draft'
              RETURNING plan.project_id;
              """,connection,transaction)){command.Parameters.AddWithValue("id",revisionId);var value=await command.ExecuteScalarAsync(context.RequestAborted);if(value is not Guid id)return Results.Conflict(new{module="066",status="revision_not_draft_or_not_found"});projectId=id;}
            if(!access.Actor.Broad&&!access.ProjectIds.Contains(projectId))return Forbidden("The revision is outside the effective user's scope.");
            await AuditAsync(connection,transaction,projectId,"066","revision",revisionId,"revision_submitted_for_module_002_approval",access.Actor,context.TraceIdentifier,new{approvalOwner="Module 002"},context.RequestAborted);await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new{module="066",status="revision_submitted",revisionId,approvalOwner="Module 002",baselineCreation="locked_until_approved"});
        }
        catch(Exception exception){return SourceFailure(context,"flowhive",exception);}
    }

    private static async Task<IResult> AuditAsync(Guid? projectId,string? module,HttpContext context)
    {
        var access=await AccessAsync(context);if(access.Failure is not null)return access.Failure;if(!CanViewPmo(access.Actor!)&&!CanViewSecure(access.Actor!)&&!CanViewFlowHive(access.Actor!))return Forbidden("Project management audit access is required.");
        try
        {
            await using var connection=await OpenAsync(context.RequestAborted);if(!await ReadyAsync(connection,context.RequestAborted))return MigrationRequired();await using var command=new NpgsqlCommand("""
              SELECT project_management_audit_event_id,project_id,module_code,entity_type,entity_id,event_code,actor_user_id,actual_user_id,effective_user_id,correlation_id,prior_sha256,new_sha256,event_json,created_at
              FROM project_management_audit_events WHERE (@project_id IS NULL OR project_id=@project_id) AND (@module='' OR module_code=@module) AND (@broad OR project_id IS NULL OR project_id=ANY(@project_ids)) ORDER BY created_at DESC LIMIT 500;
              """,connection);AddNullable(command,"project_id",NpgsqlDbType.Uuid,projectId);command.Parameters.AddWithValue("module",(module??"").Trim());command.Parameters.AddWithValue("broad",access.Actor!.Broad);command.Parameters.Add(new NpgsqlParameter("project_ids",NpgsqlDbType.Array|NpgsqlDbType.Uuid){Value=access.ProjectIds});var rows=new List<object>();await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);while(await reader.ReadAsync(context.RequestAborted))rows.Add(new{eventId=reader.GetGuid(0),projectId=reader.IsDBNull(1)?null:(Guid?)reader.GetGuid(1),module=reader.GetString(2),entityType=reader.GetString(3),entityId=reader.GetGuid(4),eventCode=reader.GetString(5),actorUserId=reader.IsDBNull(6)?null:(Guid?)reader.GetGuid(6),actualUserId=reader.IsDBNull(7)?null:(Guid?)reader.GetGuid(7),effectiveUserId=reader.IsDBNull(8)?null:(Guid?)reader.GetGuid(8),correlationId=reader.GetString(9),priorSha256=reader.GetString(10),newSha256=reader.GetString(11),evidence=JsonDocument.Parse(reader.GetFieldValue<string>(12)).RootElement.Clone(),createdAt=reader.GetFieldValue<DateTimeOffset>(13)});return Results.Ok(new{group="8",status="immutable_audit_loaded",events=rows});
        }
        catch(Exception exception){return SourceFailure(context,"project_management_audit",exception);}
    }

    private static async Task<AccessResult> AccessAsync(HttpContext context)
    {
        var truth=await ProjectFinancialTruthModule.BuildFinancialOperationsTruthAsync(context);if(truth.Failure is not null)return new(null,[],truth.Failure);return new(truth.Snapshot!.Actor,truth.Snapshot.Projects.Select(project=>project.ProjectId).ToArray(),null);
    }
    private static bool CanViewSecure(FinancialOperationsActor actor)=>actor.Broad||actor.HasPermission("VIEW_SECURE_PROJECT_INFORMATION","MANAGE_SECURE_PROJECT_INFORMATION","MANAGE_ALL")||actor.HasRole("PROJECT_MANAGER","PROJECT_MANAGEMENT","PROJECT_TEAM_COORDINATOR","ENGINEER","ENGINEERING","SOLUTION_ARCHITECT","MANAGER");
    private static bool CanManageSecure(FinancialOperationsActor actor)=>!actor.IsViewAs&&(actor.Broad||actor.HasPermission("MANAGE_SECURE_PROJECT_INFORMATION","MANAGE_ALL")||actor.HasRole("PROJECT_MANAGER","PROJECT_MANAGEMENT","PROJECT_TEAM_COORDINATOR"));
    private static bool CanViewPmo(FinancialOperationsActor actor)=>actor.Broad||actor.HasPermission("VIEW_ENTERPRISE_PMO","MANAGE_ENTERPRISE_PMO","MANAGE_ALL")||actor.HasRole("PROJECT_MANAGER","PROJECT_MANAGEMENT","PROJECT_TEAM_COORDINATOR","ENGINEER","ENGINEERING","MANAGER","EXECUTIVE");
    private static bool CanManagePmo(FinancialOperationsActor actor)=>!actor.IsViewAs&&(actor.Broad||actor.HasPermission("MANAGE_ENTERPRISE_PMO","MANAGE_ALL")||actor.HasRole("PROJECT_MANAGER","PROJECT_MANAGEMENT","PROJECT_TEAM_COORDINATOR"));
    private static bool CanViewFlowHive(FinancialOperationsActor actor)=>actor.Broad||actor.HasPermission("VIEW_FLOWHIVE_PRODUCTION","MANAGE_FLOWHIVE_PRODUCTION","MANAGE_ALL")||actor.HasRole("PROJECT_MANAGER","PROJECT_MANAGEMENT","PROJECT_TEAM_COORDINATOR","ENGINEER","ENGINEERING","MANAGER");
    private static bool CanManageFlowHive(FinancialOperationsActor actor)=>!actor.IsViewAs&&(actor.Broad||actor.HasPermission("MANAGE_FLOWHIVE_PRODUCTION","MANAGE_ALL")||actor.HasRole("PROJECT_MANAGER","PROJECT_MANAGEMENT","PROJECT_TEAM_COORDINATOR"));
    private static object PublicActor(FinancialOperationsActor actor)=>new{actor.ActualUserId,actor.EffectiveUserId,actor.DisplayName,actor.Email,actor.Roles,actor.IsViewAs,actor.Broad};
    private static async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken){var value=ProjectFinancialTruthModule.FinancialOperationsConnectionString();if(string.IsNullOrWhiteSpace(value))throw new InvalidOperationException("ProjectPulse database configuration is unavailable.");var connection=new NpgsqlConnection(value);await connection.OpenAsync(cancellationToken);return connection;}
    private static async Task<bool> ReadyAsync(NpgsqlConnection connection,CancellationToken cancellationToken){await using var command=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@id);",connection);command.Parameters.AddWithValue("id",MigrationId);return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));}
    private static async Task AuditAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,Guid? projectId,string module,string entityType,Guid entityId,string eventCode,FinancialOperationsActor actor,string correlationId,object evidence,CancellationToken cancellationToken){var json=JsonSerializer.Serialize(evidence);var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();await using var command=new NpgsqlCommand("INSERT INTO project_management_audit_events(project_id,module_code,entity_type,entity_id,event_code,actor_user_id,actual_user_id,effective_user_id,correlation_id,new_sha256,event_json) VALUES(@project,@module,@type,@entity,@event,@actor,@actual,@effective,@correlation,@hash,@json::jsonb);",connection,transaction);AddNullable(command,"project",NpgsqlDbType.Uuid,projectId);command.Parameters.AddWithValue("module",module);command.Parameters.AddWithValue("type",entityType);command.Parameters.AddWithValue("entity",entityId);command.Parameters.AddWithValue("event",eventCode);command.Parameters.AddWithValue("actor",actor.ActualUserId);command.Parameters.AddWithValue("actual",actor.ActualUserId);command.Parameters.AddWithValue("effective",actor.EffectiveUserId);command.Parameters.AddWithValue("correlation",Limit(correlationId,180));command.Parameters.AddWithValue("hash",hash);command.Parameters.AddWithValue("json",json);await command.ExecuteNonQueryAsync(cancellationToken);}
    private static object? ValidateTemplate(SecureTemplateRequest request,Guid[] projectIds,bool broad){if(string.IsNullOrWhiteSpace(request.Code)||string.IsNullOrWhiteSpace(request.Name))return new{module="033",status="code_and_name_required"};if(request.ProjectId.HasValue&&!broad&&!projectIds.Contains(request.ProjectId.Value))return new{module="033",status="project_outside_scope"};if(request.Fields is null||request.Fields.Length==0)return new{module="033",status="fields_required"};var invalid=request.Fields.FirstOrDefault(field=>string.IsNullOrWhiteSpace(field.Key)||string.IsNullOrWhiteSpace(field.Label)||!FieldTypes.Contains(field.Type,StringComparer.OrdinalIgnoreCase));return invalid is null?null:new{module="033",status="invalid_field",field=invalid.Key,allowedTypes=FieldTypes};}
    private static IResult MigrationRequired()=>Results.Json(new{group="8",status="migration_055_required",migration=MigrationId,message="Apply migration 055 before using persistent Group 8 features."},statusCode:409);
    private static IResult Forbidden(string message)=>Results.Json(new{group="8",status="access_required",message},statusCode:403);
    private static IResult ViewAsReadOnly()=>Results.Json(new{group="8",status="view_as_read_only",message="Exit View-As before changing project-management data."},statusCode:403);
    private static IResult SourceFailure(HttpContext context,string source,Exception exception)=>Results.Json(new{group="8",status="source_unavailable",source,diagnosticCode=exception is PostgresException p?$"POSTGRES_{p.SqlState}":exception.GetType().Name,correlationId=context.TraceIdentifier,message="The requested project-management source is unavailable. Other module content remains unchanged."},statusCode:503);
    private static void AddNullable(NpgsqlCommand command,string name,NpgsqlDbType type,object? value)=>command.Parameters.Add(name,type).Value=value??DBNull.Value;
    private static string Limit(string value,int maximum){var clean=(value??"").Replace('\0',' ').Trim();return clean.Length<=maximum?clean:clean[..maximum];}
    private static string CleanCode(string value)=>Limit(new string((value??"").Trim().ToUpperInvariant().Select(character=>char.IsLetterOrDigit(character)?character:'_').ToArray()).Trim('_'),120);
    private static string SafeFile(string value)=>new string((value??"project-information").ToLowerInvariant().Select(character=>char.IsLetterOrDigit(character)?character:'-').ToArray()).Trim('-');
    private static string NormalizeTaskStatus(string? value)=>value?.ToLowerInvariant() is "in_progress" or "blocked" or "complete" or "cancelled"?value.ToLowerInvariant():"not_started";
    private static string NormalizeDependency(string? value)=>value?.ToUpperInvariant() is "SS" or "FF" or "SF"?value.ToUpperInvariant():"FS";

    private sealed record AccessResult(FinancialOperationsActor? Actor,Guid[] ProjectIds,IResult? Failure);
    private sealed record TemplateExportField(string Key,string Label,string Type,bool Required,bool Sensitive,string HelpText);
    private sealed record SecureTemplateRequest(string Code,string Name,string? Description,Guid? ProjectId,Guid? CustomerId,SecureFieldRequest[] Fields);
    private sealed record SecureFieldRequest(string Key,string Label,string Type,bool Required,bool Sensitive,string? HelpText,Dictionary<string,object?>? Validation);
    private sealed record SecureCollectionRequest(Guid ProjectId,Guid? CustomerId,string? RecipientEmail,DateTimeOffset? ExpiresAt);
    private sealed record FlowHivePlanRequest(Guid ProjectId,string PlanName);
    private sealed record FlowHiveRevisionRequest(string? Label,DateOnly? StartDate,DateOnly? FinishDate,string? Notes,object? SourceProvenance,object? AiProvenance,FlowHiveTaskRequest[] Tasks,FlowHiveDependencyRequest[]? Dependencies);
    private sealed record FlowHiveTaskRequest(string Wbs,string? ParentWbs,string Name,string? Description,decimal DurationWorkingDays,decimal PlannedEffortHours,decimal PercentComplete,bool IsMilestone,string? ConstraintType,DateOnly? ConstraintDate,Guid? ResponsibleUserId,string? Status);
    private sealed record FlowHiveDependencyRequest(string PredecessorWbs,string SuccessorWbs,string? Type,decimal LagWorkingDays,string? Rationale);
}
