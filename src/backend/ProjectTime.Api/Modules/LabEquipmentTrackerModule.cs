using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static class LabEquipmentTrackerModule
{
    private const string Module = "081";
    private const string ContractVersion = "081-enterprise-v1";
    private const string Migration = "076_module_081_lab_equipment_tracker";

    public static IEndpointRouteBuilder MapLabEquipmentTrackerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/lab-equipment-tracker");
        group.MapGet("/capabilities", Capabilities);
        group.MapGet(
            "/access",
            (Func<HttpContext, Task<IResult>>)GetAccessAsync);
        group.MapGet(
            "/summary",
            (Func<HttpContext, Task<IResult>>)GetSummaryAsync);
        group.MapGet(
            "/teams",
            (Func<HttpContext, Task<IResult>>)ListManagingTeamsAsync);
        group.MapGet("/equipment", ListEquipmentAsync);
        group.MapPost("/equipment", CreateEquipmentAsync);
        group.MapPut("/equipment/{equipmentId:guid}", UpdateEquipmentAsync);
        group.MapPost("/equipment/{equipmentId:guid}/retire", RetireEquipmentAsync);
        group.MapGet("/ip-addresses", ListIpAddressesAsync);
        group.MapPost("/ip-addresses", CreateIpAddressAsync);
        group.MapPut("/ip-addresses/{allocationId:guid}", UpdateIpAddressAsync);
        group.MapGet("/connections", ListConnectionsAsync);
        group.MapPost("/connections", CreateConnectionAsync);
        group.MapGet("/rack-view", GetRackViewAsync);
        group.MapGet("/imports", ListImportsAsync);
        group.MapPost("/imports/preview", LabEquipmentImportService.PreviewAsync).DisableAntiforgery();
        group.MapPost("/imports/{batchId:guid}/commit", LabEquipmentImportService.CommitAsync);
        group.MapDelete("/imports/{batchId:guid}/preview", LabEquipmentImportService.CancelPreviewAsync);
        group.MapGet("/history", ListHistoryAsync);
        group.MapGet("/exports/{format}", ExportAsync);
        return endpoints;
    }

    private static IResult Capabilities(HttpContext context) => Results.Ok(new
    {
        module = Module,
        contractVersion = ContractVersion,
        route = "lab-equipment-tracker",
        tabs = new[] { "equipment", "ip-address-management", "cabling-connections", "rack-view", "imports-review", "history-audit" },
        importFormats = new[] { "csv", "xlsx" },
        exportFormats = new[] { "xlsx", "pdf" },
        controls = new[] { "team-scope", "project-scope", "view-as-read-only", "duplicate-ip", "network-overlap", "rack-conflict", "immutable-provenance", "formula-neutralization" }
    });

    private static async Task<IResult> GetAccessAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await RequireAccessAsync(context, connection, manage: false);
            if (access.Error is not null) return access.Error;
            var dataReady = await RuntimeReadyAsync(connection, context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                contractVersion = ContractVersion,
                scope = Scope(access.Value!),
                permissions = PermissionProjection(access.Value!),
                dataReady,
                status = dataReady ? "ready" : "migration_required",
                migration = Migration,
                message = dataReady
                    ? "Module 081 access and data foundations are ready."
                    : "Module 081 data foundations are not ready. Migration 076 must be applied and verified before records can be changed.",
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "resolve access"); }
    }

    private static async Task<IResult> GetSummaryAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await RequireAccessAsync(context, connection, manage: false);
            if (access.Error is not null) return access.Error;
            var sql = $"""
                SELECT
                  COUNT(*)::bigint,
                  COUNT(*) FILTER(WHERE equipment_status='active')::bigint,
                  COUNT(*) FILTER(WHERE equipment_status='maintenance')::bigint,
                  COUNT(*) FILTER(WHERE warranty_expires_on BETWEEN CURRENT_DATE AND CURRENT_DATE+INTERVAL '90 days')::bigint,
                  COUNT(DISTINCT lab_location)::bigint,
                  COUNT(DISTINCT NULLIF(rack,''))::bigint
                FROM lab_equipment equipment
                WHERE {EquipmentScopePredicate("equipment")};
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access.Value!);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            await reader.ReadAsync(context.RequestAborted);
            var equipment = reader.GetInt64(0);var active = reader.GetInt64(1);var maintenance = reader.GetInt64(2);
            var warranty = reader.GetInt64(3);var locations = reader.GetInt64(4);var racks = reader.GetInt64(5);
            await reader.CloseAsync();

            var ipSql = $"""
                SELECT COUNT(*)::bigint,
                       COUNT(*) FILTER(WHERE allocation_status='available')::bigint,
                       COUNT(*) FILTER(WHERE allocation_status='assigned')::bigint,
                       COUNT(*) FILTER(WHERE allocation_status='conflict')::bigint
                FROM lab_ip_allocations allocation
                WHERE {TeamScopePredicate("allocation.managing_team")};
                """;
            await using var ipCommand = new NpgsqlCommand(ipSql, connection);
            EnterpriseGovernanceAccessResolver.AddScopeParameters(ipCommand, access.Value!);
            await using var ipReader = await ipCommand.ExecuteReaderAsync(context.RequestAborted);
            await ipReader.ReadAsync(context.RequestAborted);
            return Results.Ok(new
            {
                module = Module,
                contractVersion = ContractVersion,
                scope = Scope(access.Value!),
                permissions = PermissionProjection(access.Value!),
                kpis = new
                {
                    equipment, active, maintenance, warrantyExpiring = warranty, locations, racks,
                    ipAllocations = ipReader.GetInt64(0), availableIps = ipReader.GetInt64(1),
                    assignedIps = ipReader.GetInt64(2), conflicts = ipReader.GetInt64(3)
                },
                generatedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "load summary"); }
    }


    private static async Task<IResult> ListManagingTeamsAsync(HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await RequireAccessAsync(context, connection, manage: false);
            if (access.Error is not null) return access.Error;

            await using var command = new NpgsqlCommand("""
                SELECT DISTINCT btrim(COALESCE(to_jsonb(app_user)->>'team_name','')) AS team_name
                FROM app_users app_user
                WHERE app_user.is_active=TRUE
                  AND btrim(COALESCE(to_jsonb(app_user)->>'team_name',''))<>''
                  AND (@broad_scope=TRUE OR lower(btrim(COALESCE(to_jsonb(app_user)->>'team_name','')))=lower(@team_name))
                ORDER BY team_name;
                """, connection);
            command.Parameters.AddWithValue("broad_scope", access.Value!.IsBroadScope);
            command.Parameters.AddWithValue("team_name", access.Value.TeamName ?? string.Empty);
            var teams = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted)) teams.Add(reader.GetString(0));
            if (!string.IsNullOrWhiteSpace(access.Value.TeamName)
                && !teams.Contains(access.Value.TeamName, StringComparer.OrdinalIgnoreCase))
                teams.Insert(0, access.Value.TeamName);
            return Results.Ok(new { module=Module, teams, scope=Scope(access.Value) });
        }
        catch (Exception exception)
        {
            return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list managing teams");
        }
    }

    private static async Task<IResult> ListEquipmentAsync(
        HttpContext context, string? search = null, string? team = null, string? location = null,
        string? pod = null, string? status = null, int limit = 250)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await RequireAccessAsync(context, connection, manage: false);
            if (access.Error is not null) return access.Error;
            var sql = $"""
                SELECT equipment.equipment_id,equipment.equipment_number,equipment.managing_team,
                       equipment.equipment_name,equipment.equipment_type,equipment.manufacturer,equipment.model,
                       equipment.serial_number,equipment.asset_tag,equipment.hostname,
                       COALESCE(equipment.mac_address::text,''),equipment.lab_location,equipment.pod,
                       equipment.physical_location,equipment.rack,equipment.rack_unit_start,equipment.rack_unit_height,
                       equipment.equipment_status,COALESCE(custodian.display_name,custodian.email,''),
                       COALESCE(project.project_code,''),COALESCE(project.project_name,''),
                       equipment.support_contract,equipment.warranty_expires_on,equipment.notes,
                       equipment.source_workbook,equipment.source_sheet,equipment.source_row,equipment.source_checksum,
                       equipment.created_at,equipment.updated_at,equipment.revision_number,
                       COALESCE(string_agg(DISTINCT allocation.ip_address::text,',' ORDER BY allocation.ip_address::text) FILTER(WHERE allocation.ip_address IS NOT NULL),'')
                FROM lab_equipment equipment
                LEFT JOIN app_users custodian ON custodian.user_id=equipment.custodian_user_id
                LEFT JOIN projects project ON project.project_id=equipment.linked_project_id
                LEFT JOIN lab_ip_allocations allocation ON allocation.equipment_id=equipment.equipment_id AND allocation.allocation_status<>'retired'
                WHERE {EquipmentScopePredicate("equipment")}
                  AND (@search='' OR equipment.equipment_number ILIKE '%'||@search||'%'
                    OR equipment.equipment_name ILIKE '%'||@search||'%' OR equipment.hostname ILIKE '%'||@search||'%'
                    OR equipment.serial_number ILIKE '%'||@search||'%' OR equipment.asset_tag ILIKE '%'||@search||'%')
                  AND (@team='' OR lower(equipment.managing_team)=lower(@team))
                  AND (@location='' OR lower(equipment.lab_location)=lower(@location))
                  AND (@pod='' OR lower(equipment.pod)=lower(@pod))
                  AND (@status='' OR equipment.equipment_status=@status)
                GROUP BY equipment.equipment_id,custodian.display_name,custodian.email,project.project_code,project.project_name
                ORDER BY equipment.updated_at DESC,equipment.equipment_number
                LIMIT @limit;
                """;
            await using var command = new NpgsqlCommand(sql, connection);
            EnterpriseGovernanceAccessResolver.AddScopeParameters(command, access.Value!);
            command.Parameters.AddWithValue("search", Clean(search, 120));command.Parameters.AddWithValue("team", Clean(team, 160));
            command.Parameters.AddWithValue("location", Clean(location, 180));command.Parameters.AddWithValue("pod", Clean(pod, 120));
            command.Parameters.AddWithValue("status", NormalizeChoice(status, EquipmentStatuses, allowEmpty: true));
            command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 1000));
            var rows = new List<object>();
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            while (await reader.ReadAsync(context.RequestAborted))
            {
                rows.Add(new
                {
                    equipmentId=reader.GetGuid(0),equipmentNumber=reader.GetString(1),managingTeam=reader.GetString(2),
                    name=reader.GetString(3),type=reader.GetString(4),manufacturer=reader.GetString(5),model=reader.GetString(6),
                    serialNumber=reader.GetString(7),assetTag=reader.GetString(8),hostname=reader.GetString(9),macAddress=reader.GetString(10),
                    location=reader.GetString(11),pod=reader.GetString(12),physicalLocation=reader.GetString(13),rack=reader.GetString(14),
                    rackUnitStart=NullableInt16(reader,15),rackUnitHeight=reader.GetInt16(16),status=reader.GetString(17),custodian=reader.GetString(18),
                    projectCode=reader.GetString(19),projectName=reader.GetString(20),supportContract=reader.GetString(21),
                    warrantyExpiresOn=NullableDate(reader,22),notes=reader.GetString(23),sourceWorkbook=reader.GetString(24),
                    sourceSheet=reader.GetString(25),sourceRow=reader.GetString(26),sourceChecksum=reader.GetString(27),
                    createdAt=reader.GetFieldValue<DateTimeOffset>(28),updatedAt=reader.GetFieldValue<DateTimeOffset>(29),revision=reader.GetInt32(30),
                    ipAddresses=reader.GetString(31).Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
                });
            }
            return Results.Ok(new { module=Module,scope=Scope(access.Value!),permissions=PermissionProjection(access.Value!),equipment=rows });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "list equipment"); }
    }

    private static async Task<IResult> CreateEquipmentAsync(LabEquipmentRequest request, HttpContext context)
    {
        try
        {
            var validation = ValidateEquipment(request);if (validation is not null) return validation;
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access = await RequireAccessAsync(context, connection, manage: true);if (access.Error is not null) return access.Error;
            if (!await CanManageTeamAsync(connection, access.Value!, request.ManagingTeam, context.RequestAborted))
                return EnterpriseGovernanceResults.Forbidden(Module,"You can add equipment only for an explicitly authorized team.");
            var sql = """
                INSERT INTO lab_equipment(
                  managing_team,equipment_name,equipment_type,manufacturer,model,serial_number,asset_tag,hostname,mac_address,
                  lab_location,pod,physical_location,rack,rack_unit_start,rack_unit_height,equipment_status,custodian_user_id,
                  linked_project_id,support_contract,warranty_expires_on,notes,created_by_user_id,updated_by_user_id)
                VALUES(@team,@name,@type,@manufacturer,@model,@serial,@asset,@hostname,NULLIF(@mac,'')::macaddr,
                  @location,@pod,@physical,@rack,@rack_start,@rack_height,@status,@custodian,@project,@support,@warranty,@notes,@actor,@actor)
                RETURNING equipment_id,equipment_number;
                """;
            await using var command = new NpgsqlCommand(sql, connection);AddEquipmentParameters(command, request, access.Value!.EffectiveUserId);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);await reader.ReadAsync(context.RequestAborted);
            var id=reader.GetGuid(0);var number=reader.GetString(1);await reader.CloseAsync();
            await AuditAsync(connection,access.Value!,"equipment",id,"equipment_created",null,request,context.RequestAborted);
            return Results.Created($"/api/lab-equipment-tracker/equipment/{id}",new { module=Module,equipmentId=id,equipmentNumber=number,status="created" });
        }
        catch(PostgresException exception) when(exception.SqlState is "23505" or "P0001") { return Conflict(exception.MessageText); }
        catch(Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"create equipment"); }
    }

    private static async Task<IResult> UpdateEquipmentAsync(Guid equipmentId, LabEquipmentRequest request, HttpContext context)
    {
        try
        {
            var validation=ValidateEquipment(request);if(validation is not null)return validation;
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access=await RequireAccessAsync(context,connection,manage:true);if(access.Error is not null)return access.Error;
            if(!await CanManageEquipmentAsync(connection,access.Value!,equipmentId,context.RequestAborted))return EnterpriseGovernanceResults.Forbidden(Module,"This equipment record is outside your authorized team or project scope.");
            if(!await CanManageTeamAsync(connection,access.Value!,request.ManagingTeam,context.RequestAborted))return EnterpriseGovernanceResults.Forbidden(Module,"The selected managing team is outside your authorized scope.");
            var prior=await SnapshotAsync(connection,"lab_equipment","equipment_id",equipmentId,context.RequestAborted);
            var sql=$"""
                UPDATE lab_equipment SET managing_team=@team,equipment_name=@name,equipment_type=@type,manufacturer=@manufacturer,
                  model=@model,serial_number=@serial,asset_tag=@asset,hostname=@hostname,mac_address=NULLIF(@mac,'')::macaddr,
                  lab_location=@location,pod=@pod,physical_location=@physical,rack=@rack,rack_unit_start=@rack_start,
                  rack_unit_height=@rack_height,equipment_status=@status,custodian_user_id=@custodian,linked_project_id=@project,
                  support_contract=@support,warranty_expires_on=@warranty,notes=@notes,updated_by_user_id=@actor
                WHERE equipment_id=@id AND revision_number=@revision AND equipment_status NOT IN ('retired','disposed');
                """;
            await using var command=new NpgsqlCommand(sql,connection);AddEquipmentParameters(command,request,access.Value!.EffectiveUserId);
            command.Parameters.AddWithValue("id",equipmentId);command.Parameters.AddWithValue("revision",request.Revision);
            if(await command.ExecuteNonQueryAsync(context.RequestAborted)!=1)return Results.Conflict(new { module=Module,code="STALE_OR_IMMUTABLE",message="The equipment changed since it was loaded or is retired. Refresh before trying again." });
            await AuditAsync(connection,access.Value!,"equipment",equipmentId,"equipment_updated",prior,request,context.RequestAborted);
            return Results.Ok(new { module=Module,equipmentId,status="updated" });
        }
        catch(PostgresException exception) when(exception.SqlState is "23505" or "P0001") { return Conflict(exception.MessageText); }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"update equipment");}
    }

    private static async Task<IResult> RetireEquipmentAsync(Guid equipmentId, LabRetireRequest request, HttpContext context)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(request.Reason))return Bad("RETIREMENT_REASON_REQUIRED","Provide the retirement or disposal reason.");
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access=await RequireAccessAsync(context,connection,manage:true);if(access.Error is not null)return access.Error;
            if(!await CanManageEquipmentAsync(connection,access.Value!,equipmentId,context.RequestAborted))return EnterpriseGovernanceResults.Forbidden(Module,"This equipment record is outside your authorized scope.");
            var prior=await SnapshotAsync(connection,"lab_equipment","equipment_id",equipmentId,context.RequestAborted);
            await using var command=new NpgsqlCommand("""
                UPDATE lab_equipment SET equipment_status=@status,retired_at=NOW(),retired_by_user_id=@actor,
                  updated_by_user_id=@actor,notes=concat_ws(E'\n',NULLIF(notes,''),@reason)
                WHERE equipment_id=@id AND equipment_status NOT IN ('retired','disposed');
                """,connection);
            command.Parameters.AddWithValue("status",request.Dispose?"disposed":"retired");command.Parameters.AddWithValue("actor",access.Value!.EffectiveUserId);
            command.Parameters.AddWithValue("reason",Clean(request.Reason,2000));command.Parameters.AddWithValue("id",equipmentId);
            if(await command.ExecuteNonQueryAsync(context.RequestAborted)!=1)return Results.NotFound(new { module=Module,code="EQUIPMENT_NOT_FOUND",message="The active equipment record was not found." });
            await AuditAsync(connection,access.Value!,"equipment",equipmentId,request.Dispose?"equipment_disposed":"equipment_retired",prior,request,context.RequestAborted);
            return Results.Ok(new { module=Module,equipmentId,status=request.Dispose?"disposed":"retired" });
        }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"retire equipment");}
    }

    private static async Task<IResult> ListIpAddressesAsync(HttpContext context,string? search=null,string? location=null,string? pod=null,string? zone=null,string? status=null,int limit=500)
    {
        try
        {
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access=await RequireAccessAsync(context,connection,manage:false);if(access.Error is not null)return access.Error;
            var sql=$"""
                SELECT allocation.ip_allocation_id,allocation.managing_team,allocation.lab_location,allocation.pod,allocation.network_zone,
                  allocation.address_family,allocation.network_cidr::text,allocation.usable_range,COALESCE(allocation.ip_address::text,''),
                  allocation.prefix_length,COALESCE(allocation.gateway::text,''),allocation.vlan_id,allocation.vlan_name,allocation.vrf,
                  allocation.allocation_status,allocation.equipment_id,COALESCE(equipment.equipment_number,''),COALESCE(equipment.equipment_name,''),
                  allocation.interface_name,allocation.hostname,allocation.purpose,COALESCE(owner.display_name,owner.email,''),
                  allocation.reservation_expires_at,allocation.source_workbook,allocation.source_sheet,allocation.source_row,
                  allocation.source_checksum,allocation.updated_at,allocation.revision_number
                FROM lab_ip_allocations allocation
                LEFT JOIN lab_equipment equipment ON equipment.equipment_id=allocation.equipment_id
                LEFT JOIN app_users owner ON owner.user_id=allocation.reservation_owner_user_id
                WHERE {TeamScopePredicate("allocation.managing_team")}
                  AND (@search='' OR allocation.ip_address::text ILIKE '%'||@search||'%' OR allocation.network_cidr::text ILIKE '%'||@search||'%'
                    OR allocation.hostname ILIKE '%'||@search||'%' OR allocation.purpose ILIKE '%'||@search||'%')
                  AND (@location='' OR lower(allocation.lab_location)=lower(@location))
                  AND (@pod='' OR lower(allocation.pod)=lower(@pod))
                  AND (@zone='' OR allocation.network_zone=@zone)
                  AND (@status='' OR allocation.allocation_status=@status)
                ORDER BY allocation.lab_location,allocation.pod,allocation.network_cidr,allocation.ip_address NULLS FIRST
                LIMIT @limit;
                """;
            await using var command=new NpgsqlCommand(sql,connection);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access.Value!);
            command.Parameters.AddWithValue("search",Clean(search,120));command.Parameters.AddWithValue("location",Clean(location,180));command.Parameters.AddWithValue("pod",Clean(pod,120));
            command.Parameters.AddWithValue("zone",NormalizeChoice(zone,NetworkZones,true));command.Parameters.AddWithValue("status",NormalizeChoice(status,AllocationStatuses,true));command.Parameters.AddWithValue("limit",Math.Clamp(limit,1,2000));
            var rows=new List<object>();await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);
            while(await reader.ReadAsync(context.RequestAborted))rows.Add(new
            {
                allocationId=reader.GetGuid(0),managingTeam=reader.GetString(1),location=reader.GetString(2),pod=reader.GetString(3),zone=reader.GetString(4),addressFamily=reader.GetInt16(5),
                network=reader.GetString(6),usableRange=reader.GetString(7),ipAddress=reader.GetString(8),prefixLength=reader.GetInt16(9),gateway=reader.GetString(10),
                vlanId=NullableInt32(reader,11),vlanName=reader.GetString(12),vrf=reader.GetString(13),status=reader.GetString(14),equipmentId=NullableGuid(reader,15),
                equipmentNumber=reader.GetString(16),equipmentName=reader.GetString(17),interfaceName=reader.GetString(18),hostname=reader.GetString(19),purpose=reader.GetString(20),
                reservationOwner=reader.GetString(21),reservationExpiresAt=NullableDateTime(reader,22),sourceWorkbook=reader.GetString(23),sourceSheet=reader.GetString(24),
                sourceRow=reader.GetString(25),sourceChecksum=reader.GetString(26),updatedAt=reader.GetFieldValue<DateTimeOffset>(27),revision=reader.GetInt32(28)
            });
            return Results.Ok(new { module=Module,scope=Scope(access.Value!),permissions=PermissionProjection(access.Value!),allocations=rows });
        }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"list IP allocations");}
    }

    private static async Task<IResult> CreateIpAddressAsync(LabIpAllocationRequest request,HttpContext context)
    {
        try
        {
            var validation=ValidateIp(request);if(validation is not null)return validation;
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access=await RequireAccessAsync(context,connection,manage:true);if(access.Error is not null)return access.Error;
            if(!await CanManageTeamAsync(connection,access.Value!,request.ManagingTeam,context.RequestAborted))return EnterpriseGovernanceResults.Forbidden(Module,"The selected managing team is outside your authorized scope.");
            var sql=$"""
                INSERT INTO lab_ip_allocations(managing_team,lab_location,pod,network_zone,address_family,network_cidr,usable_range,ip_address,
                  prefix_length,gateway,vlan_id,vlan_name,vrf,allocation_status,equipment_id,interface_name,hostname,purpose,
                  reservation_owner_user_id,reservation_expires_at,created_by_user_id,updated_by_user_id)
                VALUES(@team,@location,@pod,@zone,@family,@network::cidr,@range,NULLIF(@address,'')::inet,@prefix,NULLIF(@gateway,'')::inet,
                  @vlan,@vlan_name,@vrf,@status,@equipment,@interface,@hostname,@purpose,@owner,@expires,@actor,@actor)
                RETURNING ip_allocation_id;
                """;
            await using var command=new NpgsqlCommand(sql,connection);AddIpParameters(command,request,access.Value!.EffectiveUserId);
            var id=(Guid)(await command.ExecuteScalarAsync(context.RequestAborted))!;
            await AuditAsync(connection,access.Value!,"ip_allocation",id,"ip_allocation_created",null,request,context.RequestAborted);
            return Results.Created($"/api/lab-equipment-tracker/ip-addresses/{id}",new { module=Module,allocationId=id,status="created" });
        }
        catch(PostgresException exception) when(exception.SqlState is "23505" or "P0001") { return Conflict(exception.MessageText); }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"create IP allocation");}
    }

    private static async Task<IResult> UpdateIpAddressAsync(Guid allocationId,LabIpAllocationRequest request,HttpContext context)
    {
        try
        {
            var validation=ValidateIp(request);if(validation is not null)return validation;
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var access=await RequireAccessAsync(context,connection,manage:true);if(access.Error is not null)return access.Error;
            if(!await CanManageAllocationAsync(connection,access.Value!,allocationId,context.RequestAborted))return EnterpriseGovernanceResults.Forbidden(Module,"This IP allocation is outside your authorized team scope.");
            var prior=await SnapshotAsync(connection,"lab_ip_allocations","ip_allocation_id",allocationId,context.RequestAborted);
            var sql="""
                UPDATE lab_ip_allocations SET managing_team=@team,lab_location=@location,pod=@pod,network_zone=@zone,address_family=@family,
                  network_cidr=@network::cidr,usable_range=@range,ip_address=NULLIF(@address,'')::inet,prefix_length=@prefix,
                  gateway=NULLIF(@gateway,'')::inet,vlan_id=@vlan,vlan_name=@vlan_name,vrf=@vrf,allocation_status=@status,
                  equipment_id=@equipment,interface_name=@interface,hostname=@hostname,purpose=@purpose,reservation_owner_user_id=@owner,
                  reservation_expires_at=@expires,updated_by_user_id=@actor
                WHERE ip_allocation_id=@id AND revision_number=@revision;
                """;
            await using var command=new NpgsqlCommand(sql,connection);AddIpParameters(command,request,access.Value!.EffectiveUserId);command.Parameters.AddWithValue("id",allocationId);command.Parameters.AddWithValue("revision",request.Revision);
            if(await command.ExecuteNonQueryAsync(context.RequestAborted)!=1)return Results.Conflict(new { module=Module,code="STALE_ALLOCATION",message="The allocation changed since it was loaded. Refresh before trying again." });
            await AuditAsync(connection,access.Value!,"ip_allocation",allocationId,"ip_allocation_updated",prior,request,context.RequestAborted);
            return Results.Ok(new { module=Module,allocationId,status="updated" });
        }
        catch(PostgresException exception) when(exception.SqlState is "23505" or "P0001") { return Conflict(exception.MessageText); }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"update IP allocation");}
    }

    private static async Task<IResult> ListConnectionsAsync(HttpContext context,string? location=null,string? pod=null,int limit=500)
    {
        try
        {
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);var access=await RequireAccessAsync(context,connection,false);if(access.Error is not null)return access.Error;
            var sql=$"""
                SELECT connection.connection_id,connection.lab_location,connection.pod,connection.from_equipment_id,
                  source.equipment_number,source.equipment_name,connection.from_interface,connection.to_equipment_id,
                  target.equipment_number,target.equipment_name,connection.to_interface,connection.media_type,connection.cable_label,
                  connection.vlan_id,COALESCE(connection.ip_address::text,''),connection.connection_status,connection.notes,connection.updated_at,connection.revision_number
                FROM lab_cable_connections connection
                JOIN lab_equipment source ON source.equipment_id=connection.from_equipment_id
                JOIN lab_equipment target ON target.equipment_id=connection.to_equipment_id
                WHERE ({EquipmentScopePredicate("source")}) AND ({EquipmentScopePredicate("target")})
                  AND (@location='' OR lower(connection.lab_location)=lower(@location))
                  AND (@pod='' OR lower(connection.pod)=lower(@pod))
                ORDER BY connection.lab_location,connection.pod,source.equipment_number,connection.from_interface LIMIT @limit;
                """;
            await using var command=new NpgsqlCommand(sql,connection);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access.Value!);command.Parameters.AddWithValue("location",Clean(location,180));command.Parameters.AddWithValue("pod",Clean(pod,120));command.Parameters.AddWithValue("limit",Math.Clamp(limit,1,2000));
            var rows=new List<object>();await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);
            while(await reader.ReadAsync(context.RequestAborted))rows.Add(new { connectionId=reader.GetGuid(0),location=reader.GetString(1),pod=reader.GetString(2),fromEquipmentId=reader.GetGuid(3),fromNumber=reader.GetString(4),fromName=reader.GetString(5),fromInterface=reader.GetString(6),toEquipmentId=reader.GetGuid(7),toNumber=reader.GetString(8),toName=reader.GetString(9),toInterface=reader.GetString(10),media=reader.GetString(11),cableLabel=reader.GetString(12),vlanId=NullableInt32(reader,13),ipAddress=reader.GetString(14),status=reader.GetString(15),notes=reader.GetString(16),updatedAt=reader.GetFieldValue<DateTimeOffset>(17),revision=reader.GetInt32(18) });
            return Results.Ok(new { module=Module,scope=Scope(access.Value!),connections=rows });
        }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"list connections");}
    }

    private static async Task<IResult> CreateConnectionAsync(LabConnectionRequest request,HttpContext context)
    {
        try
        {
            if(request.FromEquipmentId==Guid.Empty||request.ToEquipmentId==Guid.Empty||string.IsNullOrWhiteSpace(request.FromInterface)||string.IsNullOrWhiteSpace(request.ToInterface))return Bad("CONNECTION_ENDPOINTS_REQUIRED","Select both equipment records and interfaces.");
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);var access=await RequireAccessAsync(context,connection,true);if(access.Error is not null)return access.Error;
            if(!await CanManageEquipmentAsync(connection,access.Value!,request.FromEquipmentId,context.RequestAborted)||!await CanManageEquipmentAsync(connection,access.Value!,request.ToEquipmentId,context.RequestAborted))return EnterpriseGovernanceResults.Forbidden(Module,"Both connection endpoints must be within your authorized equipment scope.");
            var sql=$"""
                INSERT INTO lab_cable_connections(lab_location,pod,from_equipment_id,from_interface,to_equipment_id,to_interface,
                  media_type,cable_label,vlan_id,ip_address,connection_status,notes,created_by_user_id,updated_by_user_id)
                VALUES(@location,@pod,@from_id,@from_interface,@to_id,@to_interface,@media,@label,@vlan,NULLIF(@address,'')::inet,@status,@notes,@actor,@actor)
                RETURNING connection_id;
                """;
            await using var command=new NpgsqlCommand(sql,connection);command.Parameters.AddWithValue("location",Required(request.Location,180));command.Parameters.AddWithValue("pod",Clean(request.Pod,120));command.Parameters.AddWithValue("from_id",request.FromEquipmentId);command.Parameters.AddWithValue("from_interface",Required(request.FromInterface,160));command.Parameters.AddWithValue("to_id",request.ToEquipmentId);command.Parameters.AddWithValue("to_interface",Required(request.ToInterface,160));command.Parameters.AddWithValue("media",Clean(request.Media,80));command.Parameters.AddWithValue("label",Clean(request.CableLabel,120));AddNullableInt(command,"vlan",request.VlanId);command.Parameters.AddWithValue("address",Clean(request.IpAddress,80));command.Parameters.AddWithValue("status",NormalizeChoice(request.Status,ConnectionStatuses));command.Parameters.AddWithValue("notes",Clean(request.Notes,4000));command.Parameters.AddWithValue("actor",access.Value!.EffectiveUserId);
            var id=(Guid)(await command.ExecuteScalarAsync(context.RequestAborted))!;await AuditAsync(connection,access.Value!,"connection",id,"connection_created",null,request,context.RequestAborted);
            return Results.Created($"/api/lab-equipment-tracker/connections/{id}",new { module=Module,connectionId=id,status="created" });
        }
        catch(PostgresException exception) when(exception.SqlState is "23505" or "P0001"){return Conflict(exception.MessageText);}
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"create connection");}
    }

    private static async Task<IResult> GetRackViewAsync(HttpContext context,string? location=null,string? rack=null)
    {
        try
        {
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);var access=await RequireAccessAsync(context,connection,false);if(access.Error is not null)return access.Error;
            var sql=$"""
                SELECT equipment.lab_location,equipment.rack,equipment.rack_unit_start,equipment.rack_unit_height,
                  equipment.equipment_id,equipment.equipment_number,equipment.equipment_name,equipment.equipment_status
                FROM lab_equipment equipment
                WHERE {EquipmentScopePredicate("equipment")} AND equipment.rack_unit_start IS NOT NULL AND btrim(equipment.rack)<>''
                  AND equipment.equipment_status NOT IN ('retired','disposed')
                  AND (@location='' OR lower(equipment.lab_location)=lower(@location))
                  AND (@rack='' OR lower(equipment.rack)=lower(@rack))
                ORDER BY equipment.lab_location,equipment.rack,equipment.rack_unit_start DESC;
                """;
            await using var command=new NpgsqlCommand(sql,connection);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access.Value!);command.Parameters.AddWithValue("location",Clean(location,180));command.Parameters.AddWithValue("rack",Clean(rack,120));
            var placements=new List<RackPlacement>();await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);
            while(await reader.ReadAsync(context.RequestAborted))placements.Add(new RackPlacement(reader.GetString(0),reader.GetString(1),reader.GetInt16(2),reader.GetInt16(3),reader.GetGuid(4),reader.GetString(5),reader.GetString(6),reader.GetString(7)));
            var racks=placements.GroupBy(row=>new { row.Location,row.Rack }).Select(group=>new
            {
                location=group.Key.Location,rack=group.Key.Rack,occupiedUnits=group.Sum(row=>row.Height),freeUnits=Math.Max(0,42-group.Sum(row=>row.Height)),
                conflict=group.SelectMany(row=>Enumerable.Range(row.Start,row.Height)).GroupBy(unit=>unit).Any(unit=>unit.Count()>1),
                placements=group.Select(row=>new { equipmentId=row.EquipmentId,equipmentNumber=row.Number,name=row.Name,start=row.Start,height=row.Height,status=row.Status })
            }).ToArray();
            return Results.Ok(new { module=Module,scope=Scope(access.Value!),rackUnitCapacity=42,racks });
        }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"load rack view");}
    }

    private static async Task<IResult> ListImportsAsync(HttpContext context,int limit=100)
    {
        try
        {
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);var access=await RequireAccessAsync(context,connection,false);if(access.Error is not null)return access.Error;
            if(!access.Value!.IsBroadScope&&!access.Value.CanImportLabEquipment)return EnterpriseGovernanceResults.Forbidden(Module,"Import evidence is limited to authorized administrators.");
            await using var command=new NpgsqlCommand("""
                SELECT batch.import_batch_id,batch.original_file_name,batch.file_sha256,batch.file_size_bytes,batch.parser_version,
                  batch.source_document_type,batch.target_surface,batch.batch_status,batch.accepted_count,batch.warning_count,batch.rejected_count,
                  COALESCE(creator.display_name,creator.email,''),batch.created_at,batch.reviewed_at,batch.committed_at
                FROM lab_import_batches batch JOIN app_users creator ON creator.user_id=batch.created_by_user_id
                ORDER BY batch.created_at DESC LIMIT @limit;
                """,connection);command.Parameters.AddWithValue("limit",Math.Clamp(limit,1,500));var rows=new List<object>();await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);
            while(await reader.ReadAsync(context.RequestAborted))rows.Add(new { batchId=reader.GetGuid(0),fileName=reader.GetString(1),sha256=reader.GetString(2),sizeBytes=reader.GetInt64(3),parserVersion=reader.GetString(4),format=reader.GetString(5),target=reader.GetString(6),status=reader.GetString(7),accepted=reader.GetInt32(8),warnings=reader.GetInt32(9),rejected=reader.GetInt32(10),createdBy=reader.GetString(11),createdAt=reader.GetFieldValue<DateTimeOffset>(12),reviewedAt=NullableDateTime(reader,13),committedAt=NullableDateTime(reader,14) });
            return Results.Ok(new { module=Module,imports=rows });
        }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"list imports");}
    }

    private static async Task<IResult> ListHistoryAsync(HttpContext context,string? entityType=null,Guid? entityId=null,int limit=250)
    {
        try
        {
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);var access=await RequireAccessAsync(context,connection,false);if(access.Error is not null)return access.Error;
            var sql=$"""
                SELECT audit.audit_event_id,audit.entity_type,audit.entity_id,audit.event_code,
                  COALESCE(actor.display_name,actor.email,''),audit.event_metadata,audit.occurred_at
                FROM lab_equipment_audit_events audit
                LEFT JOIN app_users actor ON actor.user_id=audit.effective_actor_user_id
                WHERE (@type='' OR audit.entity_type=@type) AND (@entity_id IS NULL OR audit.entity_id=@entity_id)
                  AND (
                    @broad_scope=TRUE
                    OR (audit.entity_type='equipment' AND EXISTS(
                      SELECT 1 FROM lab_equipment equipment WHERE equipment.equipment_id=audit.entity_id AND {EquipmentScopePredicate("equipment")}))
                    OR (audit.entity_type='ip_allocation' AND EXISTS(
                      SELECT 1 FROM lab_ip_allocations allocation WHERE allocation.ip_allocation_id=audit.entity_id AND {TeamScopePredicate("allocation.managing_team")}))
                    OR (audit.entity_type='connection' AND EXISTS(
                      SELECT 1 FROM lab_cable_connections connection
                      JOIN lab_equipment source ON source.equipment_id=connection.from_equipment_id
                      JOIN lab_equipment target ON target.equipment_id=connection.to_equipment_id
                      WHERE connection.connection_id=audit.entity_id AND {EquipmentScopePredicate("source")} AND {EquipmentScopePredicate("target")}))
                  )
                ORDER BY audit.occurred_at DESC LIMIT @limit;
                """;
            await using var command=new NpgsqlCommand(sql,connection);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access.Value!);command.Parameters.AddWithValue("type",Clean(entityType,40));command.Parameters.Add("entity_id",NpgsqlDbType.Uuid).Value=entityId.HasValue?entityId.Value:DBNull.Value;command.Parameters.AddWithValue("limit",Math.Clamp(limit,1,1000));
            var rows=new List<object>();await using var reader=await command.ExecuteReaderAsync(context.RequestAborted);
            while(await reader.ReadAsync(context.RequestAborted))rows.Add(new { auditId=reader.GetGuid(0),entityType=reader.GetString(1),entityId=reader.GetGuid(2),eventCode=reader.GetString(3),actor=reader.GetString(4),metadata=JsonDocument.Parse(reader.GetString(5)).RootElement.Clone(),occurredAt=reader.GetFieldValue<DateTimeOffset>(6) });
            return Results.Ok(new { module=Module,history=rows });
        }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"load history");}
    }

    private static async Task<IResult> ExportAsync(string format,HttpContext context,string? search=null,string? location=null,string? pod=null,string? status=null)
    {
        try
        {
            format=Clean(format,10).ToLowerInvariant();if(format is not("xlsx" or "pdf"))return Bad("EXPORT_FORMAT_INVALID","Use xlsx or pdf.");
            await using var connection=await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);var access=await RequireAccessAsync(context,connection,false);if(access.Error is not null)return access.Error;
            if(access.Value!.IsViewAs)return EnterpriseGovernanceResults.ViewAsReadOnly(Module);if(!access.Value.CanExport(Module))return EnterpriseGovernanceResults.Forbidden(Module,"Your role does not allow Module 081 exports.");
            var equipment=await LoadEquipmentExportAsync(connection,access.Value,search,location,pod,status,context.RequestAborted);
            var addresses=await LoadIpExportAsync(connection,access.Value,location,pod,context.RequestAborted);
            var connections=await LoadConnectionExportAsync(connection,access.Value,location,pod,context.RequestAborted);
            var conflicts=addresses.Where(row=>row.Status=="conflict").Select(row=>new[]{"IP allocation",row.Address,string.IsNullOrWhiteSpace(row.Address)?$"Network review: {row.Network}":"Allocation is marked conflict","Review required"}).ToArray();
            var history=await LoadHistoryExportAsync(connection,access.Value,context.RequestAborted);
            var scope=ScopeLabel(access.Value);var filters=$"search={Clean(search,120)}; location={Clean(location,180)}; pod={Clean(pod,120)}; status={Clean(status,24)}";
            var bytes=format=="xlsx"?EnterpriseGovernanceExports.BuildLabExcel(equipment,addresses,connections,conflicts,history,scope,filters):EnterpriseGovernanceExports.BuildLabPdf(equipment,addresses,scope,filters);
            await AuditAsync(connection,access.Value,"export",Guid.NewGuid(),$"{format}_export_created",null,new { filters,rowCount=equipment.Count+addresses.Count+connections.Count },context.RequestAborted);
            var timestamp=DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture);return Results.File(bytes,format=="xlsx"?"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":"application/pdf",$"US-Signal-Lab-Equipment-{timestamp}.{format}");
        }
        catch(Exception exception){return EnterpriseGovernanceResults.Unavailable(Module,exception,context,"create export");}
    }

    private static async Task<(EnterpriseGovernanceAccess? Value,IResult? Error)> RequireAccessAsync(HttpContext context,NpgsqlConnection connection,bool manage)
    {
        var access=await EnterpriseGovernanceAccessResolver.ResolveAsync(context,connection,context.RequestAborted);if(access is null)return(null,EnterpriseGovernanceResults.Unauthorized(Module));
        if(!access.CanViewLabEquipment)return(access,EnterpriseGovernanceResults.Forbidden(Module,"Your role does not have Lab Equipment Tracker access."));
        if(manage&&access.IsViewAs)return(access,EnterpriseGovernanceResults.ViewAsReadOnly(Module));
        if(manage&&!access.CanManageLabEquipment)return(access,EnterpriseGovernanceResults.Forbidden(Module,"Your role has read-only Lab Equipment Tracker access."));
        return(access,null);
    }

    private static async Task<bool> RuntimeReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE migration_id=@migration)
               AND to_regclass('public.lab_equipment') IS NOT NULL
               AND to_regclass('public.lab_ip_allocations') IS NOT NULL
               AND to_regclass('public.lab_cable_connections') IS NOT NULL
               AND to_regclass('public.lab_import_batches') IS NOT NULL
               AND to_regclass('public.lab_equipment_audit_events') IS NOT NULL;
            """, connection);
        command.Parameters.AddWithValue("migration", Migration);
        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    internal static async Task<(EnterpriseGovernanceAccess? Value,IResult? Error)> RequireImportAccessAsync(HttpContext context,NpgsqlConnection connection)
    {
        var access=await EnterpriseGovernanceAccessResolver.ResolveAsync(context,connection,context.RequestAborted);if(access is null)return(null,EnterpriseGovernanceResults.Unauthorized(Module));
        if(access.IsViewAs)return(access,EnterpriseGovernanceResults.ViewAsReadOnly(Module));
        if(!access.CanImportLabEquipment)return(access,EnterpriseGovernanceResults.Forbidden(Module,"Only an authorized administrator can review and commit lab imports."));
        return(access,null);
    }

    internal static async Task AuditAsync(NpgsqlConnection connection,EnterpriseGovernanceAccess access,string entityType,Guid entityId,string eventCode,object? prior,object? next,CancellationToken cancellationToken,NpgsqlTransaction? transaction=null)
    {
        await using var command=new NpgsqlCommand("""
            INSERT INTO lab_equipment_audit_events(entity_type,entity_id,event_code,actual_actor_user_id,effective_actor_user_id,prior_state,new_state,event_metadata)
            VALUES(@type,@id,@event,@actual,@effective,@prior::jsonb,@next::jsonb,@metadata::jsonb);
            """,connection,transaction);command.Parameters.AddWithValue("type",entityType);command.Parameters.AddWithValue("id",entityId);command.Parameters.AddWithValue("event",eventCode);command.Parameters.AddWithValue("actual",access.ActualUserId);command.Parameters.AddWithValue("effective",access.EffectiveUserId);command.Parameters.AddWithValue("prior",prior is null?"null":JsonSerializer.Serialize(prior));command.Parameters.AddWithValue("next",next is null?"null":JsonSerializer.Serialize(next));command.Parameters.AddWithValue("metadata",JsonSerializer.Serialize(new { module=Module,contractVersion=ContractVersion }));await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> CanManageTeamAsync(NpgsqlConnection connection,EnterpriseGovernanceAccess access,string team,CancellationToken cancellationToken)
    {
        if(access.IsBroadScope||access.CanManageLabEquipment)return true;
        return string.Equals(access.TeamName,team,StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> CanManageEquipmentAsync(NpgsqlConnection connection,EnterpriseGovernanceAccess access,Guid id,CancellationToken cancellationToken)
    { await using var command=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM lab_equipment equipment WHERE equipment.equipment_id=@id AND "+EquipmentScopePredicate("equipment")+");",connection);command.Parameters.AddWithValue("id",id);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access);return await command.ExecuteScalarAsync(cancellationToken) is true; }
    private static async Task<bool> CanManageAllocationAsync(NpgsqlConnection connection,EnterpriseGovernanceAccess access,Guid id,CancellationToken cancellationToken)
    { await using var command=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM lab_ip_allocations allocation WHERE allocation.ip_allocation_id=@id AND "+TeamScopePredicate("allocation.managing_team")+");",connection);command.Parameters.AddWithValue("id",id);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access);return await command.ExecuteScalarAsync(cancellationToken) is true; }

    private static string EquipmentScopePredicate(string alias)=>$"""
        (@broad_scope=TRUE OR @lab_full_scope=TRUE OR {alias}.custodian_user_id=@user_id
          OR (COALESCE(@team_name,'')<>'' AND lower({alias}.managing_team)=lower(@team_name))
          OR ({alias}.linked_project_id IS NOT NULL AND EXISTS(SELECT 1 FROM projects project WHERE project.project_id={alias}.linked_project_id AND (project.project_manager_user_id=@user_id OR EXISTS(SELECT 1 FROM project_assignments assignment WHERE assignment.project_id=project.project_id AND assignment.user_id=@user_id))))
        )
        """;
    private static string TeamScopePredicate(string expression)=>$"""
        (@broad_scope=TRUE OR @lab_full_scope=TRUE
          OR (COALESCE(@team_name,'')<>'' AND lower({expression})=lower(@team_name)))
        """;

    private static async Task<JsonElement?> SnapshotAsync(NpgsqlConnection connection,string table,string key,Guid id,CancellationToken cancellationToken)
    { await using var command=new NpgsqlCommand($"SELECT row_to_json(snapshot)::text FROM (SELECT * FROM {table} WHERE {key}=@id) snapshot;",connection);command.Parameters.AddWithValue("id",id);var raw=await command.ExecuteScalarAsync(cancellationToken) as string;return string.IsNullOrWhiteSpace(raw)?null:JsonDocument.Parse(raw).RootElement.Clone(); }

    private static IResult? ValidateEquipment(LabEquipmentRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.ManagingTeam)||string.IsNullOrWhiteSpace(request.Name)||string.IsNullOrWhiteSpace(request.Type)||string.IsNullOrWhiteSpace(request.Location))return Bad("EQUIPMENT_FIELDS_REQUIRED","Managing team, equipment name, type, and lab location are required.");
        if(!EquipmentStatuses.Contains(request.Status??"active",StringComparer.OrdinalIgnoreCase))return Bad("EQUIPMENT_STATUS_INVALID","Select an approved equipment status.");
        if(request.RackUnitStart is <1 or >42||request.RackUnitHeight is <1 or >42||(request.RackUnitStart.HasValue&&request.RackUnitStart.Value+request.RackUnitHeight-1>42))return Bad("RACK_PLACEMENT_INVALID","Rack placement must fit within units 1–42.");
        return null;
    }

    private static IResult? ValidateIp(LabIpAllocationRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.ManagingTeam)||string.IsNullOrWhiteSpace(request.Location)||string.IsNullOrWhiteSpace(request.Pod)||string.IsNullOrWhiteSpace(request.Network))return Bad("IPAM_FIELDS_REQUIRED","Managing team, location, pod, and network CIDR are required.");
        if(!NetworkZones.Contains(request.Zone??"",StringComparer.OrdinalIgnoreCase)||!AllocationStatuses.Contains(request.Status??"",StringComparer.OrdinalIgnoreCase))return Bad("IPAM_CHOICE_INVALID","Select approved network-zone and allocation-status values.");
        var parts=(request.Network??"").Split('/');if(parts.Length!=2||!System.Net.IPAddress.TryParse(parts[0],out var address)||!int.TryParse(parts[1],out var prefix))return Bad("NETWORK_CIDR_INVALID","Provide a valid IPv4 or IPv6 CIDR network.");
        var family=address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork?4:6;var max=family==4?32:128;if(prefix<0||prefix>max||request.AddressFamily!=family||request.PrefixLength!=prefix)return Bad("ADDRESS_FAMILY_MISMATCH","Address family and prefix must match the selected network.");
        if(!string.IsNullOrWhiteSpace(request.IpAddress)&&!System.Net.IPAddress.TryParse(request.IpAddress,out _))return Bad("IP_ADDRESS_INVALID","Provide a valid IP address.");
        return null;
    }

    private static void AddEquipmentParameters(NpgsqlCommand command,LabEquipmentRequest request,Guid actor)
    {
        command.Parameters.AddWithValue("team",Required(request.ManagingTeam,160));command.Parameters.AddWithValue("name",Required(request.Name,240));command.Parameters.AddWithValue("type",Required(request.Type,100));command.Parameters.AddWithValue("manufacturer",Clean(request.Manufacturer,120));command.Parameters.AddWithValue("model",Clean(request.Model,160));command.Parameters.AddWithValue("serial",Clean(request.SerialNumber,180));command.Parameters.AddWithValue("asset",Clean(request.AssetTag,120));command.Parameters.AddWithValue("hostname",Clean(request.Hostname,255));command.Parameters.AddWithValue("mac",Clean(request.MacAddress,40));command.Parameters.AddWithValue("location",Required(request.Location,180));command.Parameters.AddWithValue("pod",Clean(request.Pod,120));command.Parameters.AddWithValue("physical",Clean(request.PhysicalLocation,240));command.Parameters.AddWithValue("rack",Clean(request.Rack,120));AddNullableInt(command,"rack_start",request.RackUnitStart);command.Parameters.AddWithValue("rack_height",(short)Math.Clamp(request.RackUnitHeight,1,42));command.Parameters.AddWithValue("status",NormalizeChoice(request.Status,EquipmentStatuses));AddNullableGuid(command,"custodian",request.CustodianUserId);AddNullableGuid(command,"project",request.LinkedProjectId);command.Parameters.AddWithValue("support",Clean(request.SupportContract,240));AddNullableDate(command,"warranty",request.WarrantyExpiresOn);command.Parameters.AddWithValue("notes",Clean(request.Notes,8000));command.Parameters.AddWithValue("actor",actor);
    }

    private static void AddIpParameters(NpgsqlCommand command,LabIpAllocationRequest request,Guid actor)
    {
        command.Parameters.AddWithValue("team",Required(request.ManagingTeam,160));command.Parameters.AddWithValue("location",Required(request.Location,180));command.Parameters.AddWithValue("pod",Required(request.Pod,120));command.Parameters.AddWithValue("zone",NormalizeChoice(request.Zone,NetworkZones));command.Parameters.AddWithValue("family",(short)request.AddressFamily);command.Parameters.AddWithValue("network",Required(request.Network,160));command.Parameters.AddWithValue("range",Clean(request.UsableRange,160));command.Parameters.AddWithValue("address",Clean(request.IpAddress,80));command.Parameters.AddWithValue("prefix",(short)request.PrefixLength);command.Parameters.AddWithValue("gateway",Clean(request.Gateway,80));AddNullableInt(command,"vlan",request.VlanId);command.Parameters.AddWithValue("vlan_name",Clean(request.VlanName,120));command.Parameters.AddWithValue("vrf",Clean(request.Vrf,120));command.Parameters.AddWithValue("status",NormalizeChoice(request.Status,AllocationStatuses));AddNullableGuid(command,"equipment",request.EquipmentId);command.Parameters.AddWithValue("interface",Clean(request.InterfaceName,160));command.Parameters.AddWithValue("hostname",Clean(request.Hostname,255));command.Parameters.AddWithValue("purpose",Clean(request.Purpose,4000));AddNullableGuid(command,"owner",request.ReservationOwnerUserId);AddNullableDateTime(command,"expires",request.ReservationExpiresAt);command.Parameters.AddWithValue("actor",actor);
    }

    private static object Scope(EnterpriseGovernanceAccess access)=>new { mode=access.IsBroadScope?"organization":access.CanManageTeam?"assigned_teams":"team_or_project",effectiveUserId=access.EffectiveUserId,effectiveUser=access.DisplayName,team=access.TeamName,isViewAs=access.IsViewAs };
    private static string ScopeLabel(EnterpriseGovernanceAccess access)=>access.IsBroadScope?"Organization-wide":access.CanManageTeam?"Explicitly assigned teams":$"{access.TeamName} and authorized projects";
    private static object PermissionProjection(EnterpriseGovernanceAccess access)=>new { canManage=access.CanManageLabEquipment,canImport=access.CanImportLabEquipment,canExport=access.CanExport(Module),viewAsReadOnly=access.IsViewAs };

    private static async Task<List<LabEquipmentExportRow>> LoadEquipmentExportAsync(NpgsqlConnection connection,EnterpriseGovernanceAccess access,string? search,string? location,string? pod,string? status,CancellationToken cancellationToken)
    {
        var rows=new List<LabEquipmentExportRow>();var sql=$"""
            SELECT equipment.equipment_number,equipment.managing_team,equipment.equipment_name,equipment.equipment_type,equipment.manufacturer,equipment.model,equipment.serial_number,equipment.asset_tag,equipment.hostname,equipment.lab_location,equipment.pod,equipment.rack,CASE WHEN equipment.rack_unit_start IS NULL THEN '' ELSE equipment.rack_unit_start::text||'–'||(equipment.rack_unit_start+equipment.rack_unit_height-1)::text END,equipment.equipment_status,COALESCE(custodian.display_name,custodian.email,''),concat_ws(' · ',NULLIF(project.project_code,''),NULLIF(project.project_name,'')),equipment.updated_at::text
            FROM lab_equipment equipment LEFT JOIN app_users custodian ON custodian.user_id=equipment.custodian_user_id LEFT JOIN projects project ON project.project_id=equipment.linked_project_id
            WHERE {EquipmentScopePredicate("equipment")} AND (@search='' OR equipment.equipment_name ILIKE '%'||@search||'%' OR equipment.equipment_number ILIKE '%'||@search||'%') AND (@location='' OR lower(equipment.lab_location)=lower(@location)) AND (@pod='' OR lower(equipment.pod)=lower(@pod)) AND (@status='' OR equipment.equipment_status=@status)
            ORDER BY equipment.equipment_number;
            """;await using var command=new NpgsqlCommand(sql,connection);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access);command.Parameters.AddWithValue("search",Clean(search,120));command.Parameters.AddWithValue("location",Clean(location,180));command.Parameters.AddWithValue("pod",Clean(pod,120));command.Parameters.AddWithValue("status",NormalizeChoice(status,EquipmentStatuses,true));await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))rows.Add(new LabEquipmentExportRow(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),reader.GetString(10),reader.GetString(11),reader.GetString(12),reader.GetString(13),reader.GetString(14),reader.GetString(15),reader.GetString(16)));return rows;
    }

    private static async Task<List<LabIpExportRow>> LoadIpExportAsync(NpgsqlConnection connection,EnterpriseGovernanceAccess access,string? location,string? pod,CancellationToken cancellationToken)
    {
        var rows=new List<LabIpExportRow>();var sql=$"""
            SELECT allocation.lab_location,allocation.pod,allocation.network_zone,allocation.network_cidr::text,COALESCE(allocation.ip_address::text,''),COALESCE(allocation.gateway::text,''),concat_ws(' · ',allocation.vlan_id::text,NULLIF(allocation.vlan_name,'')),allocation.vrf,allocation.allocation_status,concat_ws(' · ',NULLIF(equipment.equipment_number,''),NULLIF(equipment.equipment_name,'')),allocation.interface_name,allocation.purpose
            FROM lab_ip_allocations allocation LEFT JOIN lab_equipment equipment ON equipment.equipment_id=allocation.equipment_id
            WHERE {TeamScopePredicate("allocation.managing_team")} AND (@location='' OR lower(allocation.lab_location)=lower(@location)) AND (@pod='' OR lower(allocation.pod)=lower(@pod)) ORDER BY allocation.lab_location,allocation.pod,allocation.network_cidr,allocation.ip_address;
            """;await using var command=new NpgsqlCommand(sql,connection);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access);command.Parameters.AddWithValue("location",Clean(location,180));command.Parameters.AddWithValue("pod",Clean(pod,120));await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))rows.Add(new LabIpExportRow(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),reader.GetString(10),reader.GetString(11)));return rows;
    }

    private static async Task<List<LabConnectionExportRow>> LoadConnectionExportAsync(NpgsqlConnection connection,EnterpriseGovernanceAccess access,string? location,string? pod,CancellationToken cancellationToken)
    {
        var rows=new List<LabConnectionExportRow>();var sql=$"""
            SELECT connection.lab_location,connection.pod,concat_ws(' · ',source.equipment_number,source.equipment_name),connection.from_interface,concat_ws(' · ',target.equipment_number,target.equipment_name),connection.to_interface,connection.media_type,connection.cable_label,COALESCE(connection.vlan_id::text,''),COALESCE(connection.ip_address::text,''),connection.connection_status,connection.notes
            FROM lab_cable_connections connection JOIN lab_equipment source ON source.equipment_id=connection.from_equipment_id JOIN lab_equipment target ON target.equipment_id=connection.to_equipment_id
            WHERE ({EquipmentScopePredicate("source")}) AND ({EquipmentScopePredicate("target")}) AND (@location='' OR lower(connection.lab_location)=lower(@location)) AND (@pod='' OR lower(connection.pod)=lower(@pod)) ORDER BY connection.lab_location,connection.pod,source.equipment_number;
            """;await using var command=new NpgsqlCommand(sql,connection);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access);command.Parameters.AddWithValue("location",Clean(location,180));command.Parameters.AddWithValue("pod",Clean(pod,120));await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))rows.Add(new LabConnectionExportRow(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5),reader.GetString(6),reader.GetString(7),reader.GetString(8),reader.GetString(9),reader.GetString(10),reader.GetString(11)));return rows;
    }

    private static async Task<List<string[]>> LoadHistoryExportAsync(NpgsqlConnection connection,EnterpriseGovernanceAccess access,CancellationToken cancellationToken)
    {
        var rows=new List<string[]>();var sql=$"""
            SELECT audit.occurred_at::text,audit.entity_type||' · '||audit.entity_id::text,audit.event_code,
              COALESCE(actor.display_name,actor.email,''),audit.event_metadata::text
            FROM lab_equipment_audit_events audit
            LEFT JOIN app_users actor ON actor.user_id=audit.effective_actor_user_id
            WHERE @broad_scope=TRUE
              OR (audit.entity_type='equipment' AND EXISTS(
                SELECT 1 FROM lab_equipment equipment WHERE equipment.equipment_id=audit.entity_id AND {EquipmentScopePredicate("equipment")}))
              OR (audit.entity_type='ip_allocation' AND EXISTS(
                SELECT 1 FROM lab_ip_allocations allocation WHERE allocation.ip_allocation_id=audit.entity_id AND {TeamScopePredicate("allocation.managing_team")}))
              OR (audit.entity_type='connection' AND EXISTS(
                SELECT 1 FROM lab_cable_connections connection
                JOIN lab_equipment source ON source.equipment_id=connection.from_equipment_id
                JOIN lab_equipment target ON target.equipment_id=connection.to_equipment_id
                WHERE connection.connection_id=audit.entity_id AND {EquipmentScopePredicate("source")} AND {EquipmentScopePredicate("target")}))
            ORDER BY audit.occurred_at DESC LIMIT 1000;
            """;
        await using var command=new NpgsqlCommand(sql,connection);EnterpriseGovernanceAccessResolver.AddScopeParameters(command,access);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))rows.Add([reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4)]);return rows;
    }

    private static IResult Bad(string code,string message)=>Results.BadRequest(new { module=Module,code,message });
    private static IResult Conflict(string message)=>Results.Conflict(new { module=Module,code="VALIDATION_CONFLICT",message });
    internal static string Clean(string? value,int max)=>string.Concat((value??"").Where(ch=>!char.IsControl(ch)||ch is '\n' or '\t')).Trim() is var text&&text.Length>max?text[..max]:text;
    private static string Required(string? value,int max)=>Clean(value,max);
    private static string NormalizeChoice(string? value,IReadOnlyList<string> allowed,bool allowEmpty=false){var normalized=Clean(value,40).ToLowerInvariant().Replace('-','_').Replace(' ','_');if(allowEmpty&&normalized=="")return "";return allowed.Contains(normalized,StringComparer.OrdinalIgnoreCase)?normalized:allowed[0];}
    private static readonly string[] EquipmentStatuses=["active","spare","reserved","maintenance","retired","disposed"];
    private static readonly string[] NetworkZones=["underlay","overlay","management","service","transit","other"];
    private static readonly string[] AllocationStatuses=["available","reserved","assigned","conflict","retired"];
    private static readonly string[] ConnectionStatuses=["planned","active","maintenance","disconnected","retired"];
    private static void AddNullableGuid(NpgsqlCommand command,string name,Guid? value)=>command.Parameters.Add(name,NpgsqlDbType.Uuid).Value=value.HasValue?value.Value:DBNull.Value;
    private static void AddNullableInt(NpgsqlCommand command,string name,int? value)=>command.Parameters.Add(name,NpgsqlDbType.Integer).Value=value.HasValue?value.Value:DBNull.Value;
    private static void AddNullableDate(NpgsqlCommand command,string name,DateOnly? value)=>command.Parameters.Add(name,NpgsqlDbType.Date).Value=value.HasValue?value.Value:DBNull.Value;
    private static void AddNullableDateTime(NpgsqlCommand command,string name,DateTimeOffset? value)=>command.Parameters.Add(name,NpgsqlDbType.TimestampTz).Value=value.HasValue?value.Value:DBNull.Value;
    private static Guid? NullableGuid(NpgsqlDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetGuid(index);
    private static int? NullableInt32(NpgsqlDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetInt32(index);
    private static short? NullableInt16(NpgsqlDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetInt16(index);
    private static DateOnly? NullableDate(NpgsqlDataReader reader,int index)=>reader.IsDBNull(index)?null:DateOnly.FromDateTime(reader.GetDateTime(index));
    private static DateTimeOffset? NullableDateTime(NpgsqlDataReader reader,int index)=>reader.IsDBNull(index)?null:reader.GetFieldValue<DateTimeOffset>(index);
    private sealed record RackPlacement(string Location,string Rack,int Start,int Height,Guid EquipmentId,string Number,string Name,string Status);
}

public sealed record LabEquipmentRequest(
    string? ManagingTeam,string? Name,string? Type,string? Manufacturer,string? Model,string? SerialNumber,
    string? AssetTag,string? Hostname,string? MacAddress,string? Location,string? Pod,string? PhysicalLocation,
    string? Rack,int? RackUnitStart,int RackUnitHeight,string? Status,Guid? CustodianUserId,Guid? LinkedProjectId,
    string? SupportContract,DateOnly? WarrantyExpiresOn,string? Notes,int Revision=1);

public sealed record LabRetireRequest(string? Reason,bool Dispose=false);

public sealed record LabIpAllocationRequest(
    string? ManagingTeam,string? Location,string? Pod,string? Zone,int AddressFamily,string? Network,string? UsableRange,
    string? IpAddress,int PrefixLength,string? Gateway,int? VlanId,string? VlanName,string? Vrf,string? Status,
    Guid? EquipmentId,string? InterfaceName,string? Hostname,string? Purpose,Guid? ReservationOwnerUserId,
    DateTimeOffset? ReservationExpiresAt,int Revision=1);

public sealed record LabConnectionRequest(
    string? Location,string? Pod,Guid FromEquipmentId,string? FromInterface,Guid ToEquipmentId,string? ToInterface,
    string? Media,string? CableLabel,int? VlanId,string? IpAddress,string? Status,string? Notes);
