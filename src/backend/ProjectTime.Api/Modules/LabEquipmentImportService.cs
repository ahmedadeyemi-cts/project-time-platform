using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class LabEquipmentImportService
{
    private const string Module = "081";
    private const string ParserVersion = "081-import-v1";
    private const long MaxFileBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> Targets = new(StringComparer.OrdinalIgnoreCase) { "equipment", "ipam", "connections" };
    private static readonly string[] SensitiveTerms = ["password","credential","secret","token","privatekey","apikey","connectionstring","clientsecret"];

    internal static async Task<IResult> PreviewAsync(HttpContext context)
    {
        try
        {
            if (!context.Request.HasFormContentType)
                return Bad("IMPORT_FORM_REQUIRED", "Upload an approved CSV or XLSX file using multipart form data.");
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var file = form.Files.GetFile("file");
            var target = Normalize(form["targetSurface"].FirstOrDefault());
            if (file is null || file.Length == 0) return Bad("IMPORT_FILE_REQUIRED", "Select a non-empty CSV or XLSX file.");
            if (file.Length > MaxFileBytes) return Bad("IMPORT_FILE_TOO_LARGE", "The import file must be 10 MB or smaller.");
            if (!Targets.Contains(target)) return Bad("IMPORT_TARGET_INVALID", "Select Equipment, IP Address Management, or Cabling & Connections.");
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".csv" or ".xlsx")) return Bad("IMPORT_FORMAT_INVALID", "Only approved .csv and .xlsx files are accepted.");

            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await LabEquipmentTrackerModule.RequireImportAccessAsync(context, connection);
            if (authorization.Error is not null) return authorization.Error;

            await using var source = file.OpenReadStream();
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory, context.RequestAborted);
            var bytes = memory.ToArray();
            var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var parsed = extension == ".csv" ? ParseCsv(bytes) : ParseWorkbook(bytes);
            if (parsed.Rows.Count == 0) return Bad("IMPORT_EMPTY", "The import does not contain any data rows.");
            if (parsed.Headers.Any(IsSensitiveHeader))
                return Bad("SENSITIVE_COLUMNS_BLOCKED", "The file contains a credential, password, secret, token, key, or connection-string column. Remove it before importing.");

            var mapping = BuildMapping(parsed.Headers, target);
            var missing = RequiredFields(target).Where(field => !mapping.Values.Contains(field, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (missing.Length > 0)
                return Bad("IMPORT_MAPPING_INCOMPLETE", $"Required columns could not be mapped: {string.Join(", ", missing)}.");

            var batchId = Guid.NewGuid();
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            try
            {
                await using (var batch = new NpgsqlCommand("""
                    INSERT INTO lab_import_batches(import_batch_id,original_file_name,file_sha256,file_size_bytes,parser_version,
                      source_document_type,target_surface,batch_status,created_by_user_id)
                    VALUES(@id,@file,@sha,@size,@parser,@format,@target,'preview',@actor);
                    """, connection, transaction))
                {
                    batch.Parameters.AddWithValue("id", batchId);
                    batch.Parameters.AddWithValue("file", LabEquipmentTrackerModule.Clean(Path.GetFileName(file.FileName), 240));
                    batch.Parameters.AddWithValue("sha", checksum);
                    batch.Parameters.AddWithValue("size", bytes.LongLength);
                    batch.Parameters.AddWithValue("parser", ParserVersion);
                    batch.Parameters.AddWithValue("format", extension[1..]);
                    batch.Parameters.AddWithValue("target", target);
                    batch.Parameters.AddWithValue("actor", authorization.Value!.EffectiveUserId);
                    await batch.ExecuteNonQueryAsync(context.RequestAborted);
                }

                var fingerprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var previews = new List<object>();
                var accepted = 0; var warnings = 0; var rejected = 0;
                foreach (var sourceRow in parsed.Rows.Take(5000))
                {
                    var payload = MapRow(sourceRow.Values, mapping);
                    payload["source_sheet"] = sourceRow.Sheet;
                    payload["source_row"] = sourceRow.RowNumber.ToString(CultureInfo.InvariantCulture);
                    var messages = ValidateRow(payload, target);
                    var fingerprint = Fingerprint(target, payload);
                    var status = messages.Any(message => message.Level == "error") ? "rejected"
                        : !fingerprints.Add(fingerprint) ? "duplicate"
                        : messages.Count > 0 ? "warning" : "accepted";
                    if (status == "accepted") accepted++; else if (status is "warning" or "duplicate") warnings++; else rejected++;
                    await using var rowCommand = new NpgsqlCommand("""
                        INSERT INTO lab_import_rows(import_batch_id,source_sheet,source_row_number,row_fingerprint,row_status,
                          sanitized_payload,validation_messages)
                        VALUES(@batch,@sheet,@row,@fingerprint,@status,@payload::jsonb,@messages::jsonb)
                        ON CONFLICT(import_batch_id,row_fingerprint) DO NOTHING;
                        """, connection, transaction);
                    rowCommand.Parameters.AddWithValue("batch", batchId);
                    rowCommand.Parameters.AddWithValue("sheet", LabEquipmentTrackerModule.Clean(sourceRow.Sheet, 160));
                    rowCommand.Parameters.AddWithValue("row", sourceRow.RowNumber);
                    rowCommand.Parameters.AddWithValue("fingerprint", fingerprint);
                    rowCommand.Parameters.AddWithValue("status", status);
                    rowCommand.Parameters.AddWithValue("payload", JsonSerializer.Serialize(payload));
                    rowCommand.Parameters.AddWithValue("messages", JsonSerializer.Serialize(messages));
                    await rowCommand.ExecuteNonQueryAsync(context.RequestAborted);
                    if (previews.Count < 250) previews.Add(new { sheet = sourceRow.Sheet, row = sourceRow.RowNumber, status, payload, messages });
                }

                await using (var update = new NpgsqlCommand("""
                    UPDATE lab_import_batches SET accepted_count=@accepted,warning_count=@warnings,rejected_count=@rejected,
                      batch_status=CASE WHEN @rejected>0 OR @warnings>0 THEN 'review_required' ELSE 'preview' END
                    WHERE import_batch_id=@id;
                    """, connection, transaction))
                {
                    update.Parameters.AddWithValue("accepted", accepted); update.Parameters.AddWithValue("warnings", warnings);
                    update.Parameters.AddWithValue("rejected", rejected); update.Parameters.AddWithValue("id", batchId);
                    await update.ExecuteNonQueryAsync(context.RequestAborted);
                }
                await LabEquipmentTrackerModule.AuditAsync(connection, authorization.Value!, "import_batch", batchId, "import_preview_created", null,
                    new { fileName = Path.GetFileName(file.FileName), checksum, target, accepted, warnings, rejected, mapping }, context.RequestAborted, transaction);
                await transaction.CommitAsync(context.RequestAborted);
                return Results.Ok(new
                {
                    module = Module, batchId, target, checksum, parserVersion = ParserVersion,
                    status = warnings > 0 || rejected > 0 ? "review_required" : "preview",
                    counts = new { accepted, warnings, rejected, total = accepted + warnings + rejected },
                    mapping, rows = previews, truncated = parsed.Rows.Count > previews.Count,
                    message = rejected > 0 ? "Rejected rows will not be committed. Review all warnings before approval." : "Preview created. No operational records have been changed."
                });
            }
            catch { await transaction.RollbackAsync(context.RequestAborted); throw; }
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return Results.Conflict(new { module = Module, code = "IMPORT_ALREADY_EXISTS", message = "That exact file has already been previewed or imported for this target. Review the existing batch instead of importing it again." });
        }
        catch (InvalidDataException exception) { return Bad("IMPORT_PARSE_FAILED", exception.Message); }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "preview import"); }
    }

    internal static async Task<IResult> CommitAsync(Guid batchId, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await LabEquipmentTrackerModule.RequireImportAccessAsync(context, connection);
            if (authorization.Error is not null) return authorization.Error;
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            try
            {
                string target;
                await using (var batch = new NpgsqlCommand("""
                    SELECT target_surface FROM lab_import_batches
                    WHERE import_batch_id=@id AND batch_status IN ('preview','review_required')
                    FOR UPDATE;
                    """, connection, transaction))
                {
                    batch.Parameters.AddWithValue("id", batchId);
                    target = (await batch.ExecuteScalarAsync(context.RequestAborted) as string) ?? string.Empty;
                }
                if (target == string.Empty) return Results.Conflict(new { module = Module, code = "IMPORT_NOT_COMMITTABLE", message = "The import preview was not found or has already been committed, cancelled, or rejected." });

                var rows = new List<(Guid RowId, string Status, Dictionary<string, string> Payload)>();
                await using (var command = new NpgsqlCommand("""
                    SELECT import_row_id,row_status,sanitized_payload::text
                    FROM lab_import_rows WHERE import_batch_id=@id AND row_status IN ('accepted','warning')
                    ORDER BY source_sheet,source_row_number;
                    """, connection, transaction))
                {
                    command.Parameters.AddWithValue("id", batchId);
                    await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
                    while (await reader.ReadAsync(context.RequestAborted))
                        rows.Add((reader.GetGuid(0), reader.GetString(1), JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(2)) ?? []));
                }
                if (rows.Count == 0) return Results.Conflict(new { module = Module, code = "IMPORT_HAS_NO_ACCEPTED_ROWS", message = "No accepted or reviewed-warning rows are available to commit." });

                var committed = 0;
                foreach (var row in rows)
                {
                    var entityId = target switch
                    {
                        "equipment" => await CommitEquipmentAsync(connection, transaction, row.Payload, batchId, authorization.Value!, context.RequestAborted),
                        "ipam" => await CommitIpAsync(connection, transaction, row.Payload, batchId, authorization.Value!, context.RequestAborted),
                        "connections" => await CommitConnectionAsync(connection, transaction, row.Payload, batchId, authorization.Value!, context.RequestAborted),
                        _ => throw new InvalidDataException("The import target is not supported.")
                    };
                    await using var mark = new NpgsqlCommand("""
                        UPDATE lab_import_rows SET row_status='committed',committed_entity_id=@entity,
                          reviewed_by_user_id=@actor,reviewed_at=NOW() WHERE import_row_id=@row;
                        """, connection, transaction);
                    mark.Parameters.AddWithValue("entity", entityId); mark.Parameters.AddWithValue("actor", authorization.Value!.EffectiveUserId); mark.Parameters.AddWithValue("row", row.RowId);
                    await mark.ExecuteNonQueryAsync(context.RequestAborted); committed++;
                }
                await using (var update = new NpgsqlCommand("""
                    UPDATE lab_import_batches SET batch_status='committed',reviewed_by_user_id=@actor,committed_by_user_id=@actor,
                      reviewed_at=NOW(),committed_at=NOW() WHERE import_batch_id=@id;
                    """, connection, transaction))
                {
                    update.Parameters.AddWithValue("actor", authorization.Value!.EffectiveUserId); update.Parameters.AddWithValue("id", batchId);
                    await update.ExecuteNonQueryAsync(context.RequestAborted);
                }
                await LabEquipmentTrackerModule.AuditAsync(connection, authorization.Value!, "import_batch", batchId, "import_committed", null,
                    new { target, committed }, context.RequestAborted, transaction);
                await transaction.CommitAsync(context.RequestAborted);
                return Results.Ok(new { module = Module, batchId, status = "committed", committed });
            }
            catch { await transaction.RollbackAsync(context.RequestAborted); throw; }
        }
        catch (PostgresException exception) when (exception.SqlState is "23505" or "P0001")
        { return Results.Conflict(new { module = Module, code = "IMPORT_CONFLICT", message = exception.MessageText }); }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "commit import"); }
    }

    internal static async Task<IResult> CancelPreviewAsync(Guid batchId, HttpContext context)
    {
        try
        {
            await using var connection = await EnterpriseGovernanceAccessResolver.OpenAsync(context.RequestAborted);
            var authorization = await LabEquipmentTrackerModule.RequireImportAccessAsync(context, connection);
            if (authorization.Error is not null) return authorization.Error;
            await using var transaction = await connection.BeginTransactionAsync(context.RequestAborted);
            await using var command = new NpgsqlCommand("DELETE FROM lab_import_batches WHERE import_batch_id=@id AND batch_status IN ('preview','review_required') RETURNING original_file_name,file_sha256;", connection, transaction);
            command.Parameters.AddWithValue("id", batchId);
            await using var reader = await command.ExecuteReaderAsync(context.RequestAborted);
            if (!await reader.ReadAsync(context.RequestAborted)) return Results.Conflict(new { module = Module, code = "IMPORT_NOT_CANCELLABLE", message = "Only an uncommitted preview can be cancelled." });
            var fileName = reader.GetString(0); var checksum = reader.GetString(1); await reader.CloseAsync();
            await LabEquipmentTrackerModule.AuditAsync(connection, authorization.Value!, "import_batch", batchId, "import_preview_cancelled", null,
                new { fileName, checksum }, context.RequestAborted, transaction);
            await transaction.CommitAsync(context.RequestAborted);
            return Results.Ok(new { module = Module, batchId, status = "cancelled" });
        }
        catch (Exception exception) { return EnterpriseGovernanceResults.Unavailable(Module, exception, context, "cancel import preview"); }
    }

    private static async Task<Guid> CommitEquipmentAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Dictionary<string, string> row, Guid batchId, EnterpriseGovernanceAccess access, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var command = new NpgsqlCommand("""
            INSERT INTO lab_equipment(equipment_id,managing_team,equipment_name,equipment_type,manufacturer,model,serial_number,
              asset_tag,hostname,lab_location,pod,physical_location,rack,rack_unit_start,rack_unit_height,equipment_status,
              support_contract,notes,source_workbook,source_sheet,source_row,source_checksum,import_batch_id,created_by_user_id,updated_by_user_id)
            SELECT @id,@team,@name,@type,@manufacturer,@model,@serial,@asset,@hostname,@location,@pod,@physical,@rack,
              @rack_start,@rack_height,@status,@support,@notes,batch.original_file_name,@sheet,@source_row,batch.file_sha256,@batch,@actor,@actor
            FROM lab_import_batches batch WHERE batch.import_batch_id=@batch;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("team", Required(row, "managing_team"));
        command.Parameters.AddWithValue("name", Required(row, "equipment_name")); command.Parameters.AddWithValue("type", Required(row, "equipment_type"));
        foreach (var key in new[] { "manufacturer", "model", "serial_number", "asset_tag", "hostname", "physical_location", "rack", "support_contract", "notes" }) command.Parameters.AddWithValue(key, Value(row, key));
        command.Parameters.AddWithValue("location", Required(row, "lab_location")); command.Parameters.AddWithValue("pod", Value(row, "pod"));
        AddNullableInt(command, "rack_start", Int(row, "rack_unit_start")); command.Parameters.AddWithValue("rack_height", Math.Clamp(Int(row, "rack_unit_height") ?? 1, 1, 42));
        command.Parameters.AddWithValue("status", Choice(row, "status", ["active","spare","reserved","maintenance"], "active"));
        command.Parameters.AddWithValue("sheet", Value(row, "source_sheet")); command.Parameters.AddWithValue("source_row", Value(row, "source_row"));
        command.Parameters.AddWithValue("batch", batchId); command.Parameters.AddWithValue("actor", access.EffectiveUserId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await LabEquipmentTrackerModule.AuditAsync(connection, access, "equipment", id, "equipment_imported", null, new { batchId }, cancellationToken, transaction);
        return id;
    }

    private static async Task<Guid> CommitIpAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Dictionary<string, string> row, Guid batchId, EnterpriseGovernanceAccess access, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid(); var network = Required(row, "network_cidr"); var parts = network.Split('/'); var family = System.Net.IPAddress.Parse(parts[0]).AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 4 : 6;
        Guid? equipmentId = null; var equipmentNumber = Value(row, "equipment_number");
        if (equipmentNumber != string.Empty)
        {
            await using var lookup = new NpgsqlCommand("SELECT equipment_id FROM lab_equipment WHERE lower(equipment_number)=lower(@number);", connection, transaction);
            lookup.Parameters.AddWithValue("number", equipmentNumber); var equipmentValue = await lookup.ExecuteScalarAsync(cancellationToken); equipmentId = equipmentValue is Guid resolvedEquipmentId ? resolvedEquipmentId : null;
        }
        await using var command = new NpgsqlCommand("""
            INSERT INTO lab_ip_allocations(ip_allocation_id,managing_team,lab_location,pod,network_zone,address_family,network_cidr,
              usable_range,ip_address,prefix_length,gateway,vlan_id,vlan_name,vrf,allocation_status,equipment_id,interface_name,
              hostname,purpose,source_workbook,source_sheet,source_row,source_checksum,import_batch_id,created_by_user_id,updated_by_user_id)
            SELECT @id,@team,@location,@pod,@zone,@family,@network::cidr,@range,NULLIF(@address,'')::inet,@prefix,NULLIF(@gateway,'')::inet,
              @vlan,@vlan_name,@vrf,@status,@equipment,@interface,@hostname,@purpose,batch.original_file_name,@sheet,@source_row,
              batch.file_sha256,@batch,@actor,@actor FROM lab_import_batches batch WHERE batch.import_batch_id=@batch;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("team", Required(row, "managing_team")); command.Parameters.AddWithValue("location", Required(row, "lab_location")); command.Parameters.AddWithValue("pod", Required(row, "pod")); command.Parameters.AddWithValue("zone", Choice(row, "network_zone", ["underlay","overlay","management","service","transit","other"], "other")); command.Parameters.AddWithValue("family", family); command.Parameters.AddWithValue("network", network); command.Parameters.AddWithValue("range", Value(row, "usable_range")); command.Parameters.AddWithValue("address", Value(row, "ip_address")); command.Parameters.AddWithValue("prefix", int.Parse(parts[1], CultureInfo.InvariantCulture)); command.Parameters.AddWithValue("gateway", Value(row, "gateway")); AddNullableInt(command, "vlan", Int(row, "vlan_id")); command.Parameters.AddWithValue("vlan_name", Value(row, "vlan_name")); command.Parameters.AddWithValue("vrf", Value(row, "vrf")); command.Parameters.AddWithValue("status", Choice(row, "status", ["available","reserved","assigned","conflict"], equipmentId.HasValue ? "assigned" : "available")); AddNullableGuid(command, "equipment", equipmentId); command.Parameters.AddWithValue("interface", Value(row, "interface_name")); command.Parameters.AddWithValue("hostname", Value(row, "hostname")); command.Parameters.AddWithValue("purpose", Value(row, "purpose")); command.Parameters.AddWithValue("sheet", Value(row, "source_sheet")); command.Parameters.AddWithValue("source_row", Value(row, "source_row")); command.Parameters.AddWithValue("batch", batchId); command.Parameters.AddWithValue("actor", access.EffectiveUserId);
        await command.ExecuteNonQueryAsync(cancellationToken); await LabEquipmentTrackerModule.AuditAsync(connection, access, "ip_allocation", id, "ip_allocation_imported", null, new { batchId }, cancellationToken, transaction); return id;
    }

    private static async Task<Guid> CommitConnectionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Dictionary<string, string> row, Guid batchId, EnterpriseGovernanceAccess access, CancellationToken cancellationToken)
    {
        async Task<Guid> Equipment(string key)
        {
            await using var lookup = new NpgsqlCommand("SELECT equipment_id FROM lab_equipment WHERE lower(equipment_number)=lower(@number);", connection, transaction);
            lookup.Parameters.AddWithValue("number", Required(row, key)); return (Guid)(await lookup.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidDataException($"Equipment {Value(row, key)} was not found."));
        }
        var id = Guid.NewGuid(); var fromId = await Equipment("from_equipment_number"); var toId = await Equipment("to_equipment_number");
        await using var command = new NpgsqlCommand("""
            INSERT INTO lab_cable_connections(connection_id,lab_location,pod,from_equipment_id,from_interface,to_equipment_id,to_interface,
              media_type,cable_label,vlan_id,ip_address,connection_status,notes,source_checksum,import_batch_id,created_by_user_id,updated_by_user_id)
            SELECT @id,@location,@pod,@from_id,@from_interface,@to_id,@to_interface,@media,@label,@vlan,NULLIF(@address,'')::inet,
              @status,@notes,batch.file_sha256,@batch,@actor,@actor FROM lab_import_batches batch WHERE batch.import_batch_id=@batch;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("location", Required(row, "lab_location")); command.Parameters.AddWithValue("pod", Value(row, "pod")); command.Parameters.AddWithValue("from_id", fromId); command.Parameters.AddWithValue("from_interface", Required(row, "from_interface")); command.Parameters.AddWithValue("to_id", toId); command.Parameters.AddWithValue("to_interface", Required(row, "to_interface")); command.Parameters.AddWithValue("media", Value(row, "media_type")); command.Parameters.AddWithValue("label", Value(row, "cable_label")); AddNullableInt(command, "vlan", Int(row, "vlan_id")); command.Parameters.AddWithValue("address", Value(row, "ip_address")); command.Parameters.AddWithValue("status", Choice(row, "status", ["planned","active","maintenance","disconnected"], "active")); command.Parameters.AddWithValue("notes", Value(row, "notes")); command.Parameters.AddWithValue("batch", batchId); command.Parameters.AddWithValue("actor", access.EffectiveUserId);
        await command.ExecuteNonQueryAsync(cancellationToken); await LabEquipmentTrackerModule.AuditAsync(connection, access, "connection", id, "connection_imported", null, new { batchId }, cancellationToken, transaction); return id;
    }

    private static ParsedImport ParseCsv(byte[] bytes)
    {
        var text = new UTF8Encoding(false, true).GetString(bytes); var records = ParseCsvRecords(text);
        if (records.Count < 2) return new ParsedImport(records.FirstOrDefault() ?? [], []);
        var headers = records[0].Select(CleanHeader).ToArray(); var rows = new List<SourceRow>();
        for (var index = 1; index < records.Count; index++) rows.Add(new SourceRow("CSV", index + 1, RowDictionary(headers, records[index])));
        return new ParsedImport(headers, rows);
    }

    private static ParsedImport ParseWorkbook(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false); using var workbook = new XLWorkbook(stream);
        var allRows = new List<SourceRow>(); var allHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Worksheets)
        {
            var range = sheet.RangeUsed(); if (range is null) continue; var first = range.FirstRowUsed(); if (first is null) continue;
            var headers = first.Cells(1, range.ColumnCount()).Select(cell => CleanHeader(cell.GetString())).ToArray(); foreach (var header in headers) allHeaders.Add(header);
            foreach (var row in range.RowsUsed().Skip(1))
            {
                var values = row.Cells(1, headers.Length).Select(cell => cell.GetFormattedString()).ToArray();
                if (values.All(string.IsNullOrWhiteSpace)) continue; allRows.Add(new SourceRow(sheet.Name, row.RowNumber(), RowDictionary(headers, values)));
            }
        }
        return new ParsedImport(allHeaders.ToArray(), allRows);
    }

    private static List<string[]> ParseCsvRecords(string text)
    {
        var rows = new List<string[]>(); var row = new List<string>(); var field = new StringBuilder(); var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var ch = text[index];
            if (ch == '"') { if (quoted && index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; } else quoted = !quoted; }
            else if (ch == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((ch == '\n' || ch == '\r') && !quoted) { if (ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++; row.Add(field.ToString()); field.Clear(); if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row.ToArray()); row = []; }
            else field.Append(ch);
        }
        if (quoted) throw new InvalidDataException("The CSV contains an unterminated quoted field.");
        row.Add(field.ToString()); if (row.Any(value => !string.IsNullOrWhiteSpace(value))) rows.Add(row.ToArray()); return rows;
    }

    private static Dictionary<string, string> RowDictionary(IReadOnlyList<string> headers, IReadOnlyList<string> values)
    { var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); for (var index = 0; index < headers.Count; index++) if (!string.IsNullOrWhiteSpace(headers[index])) result[headers[index]] = LabEquipmentTrackerModule.Clean(index < values.Count ? values[index] : string.Empty, 4000); return result; }

    private static Dictionary<string, string> BuildMapping(IEnumerable<string> headers, string target)
    {
        var aliases = Aliases(target); var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers) { var compact = Compact(header); var match = aliases.FirstOrDefault(pair => pair.Value.Contains(compact, StringComparer.OrdinalIgnoreCase)); if (!string.IsNullOrWhiteSpace(match.Key)) mapping[header] = match.Key; }
        return mapping;
    }

    private static Dictionary<string, string> MapRow(Dictionary<string, string> source, Dictionary<string, string> mapping)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in mapping) if (source.TryGetValue(pair.Key, out var value)) result[pair.Value] = value;
        return result;
    }

    private static List<ValidationMessage> ValidateRow(Dictionary<string, string> row, string target)
    {
        var messages = new List<ValidationMessage>();
        foreach (var required in RequiredFields(target)) if (Value(row, required) == string.Empty) messages.Add(new("error", required, "Required value is missing."));
        if (target == "ipam")
        {
            var cidr = Value(row, "network_cidr").Split('/'); if (cidr.Length != 2 || !System.Net.IPAddress.TryParse(cidr[0], out _) || !int.TryParse(cidr.ElementAtOrDefault(1), out _)) messages.Add(new("error", "network_cidr", "Network must be a valid CIDR."));
            var ip = Value(row, "ip_address"); if (ip != string.Empty && !System.Net.IPAddress.TryParse(ip, out _)) messages.Add(new("error", "ip_address", "IP address is invalid."));
        }
        if (target == "equipment")
        {
            if (Value(row, "serial_number") == string.Empty && Value(row, "asset_tag") == string.Empty) messages.Add(new("warning", "identity", "Serial number and asset tag are both empty."));
            var rackStart = Int(row, "rack_unit_start"); var rackHeight = Int(row, "rack_unit_height") ?? 1;
            if (rackStart is < 1 or > 42 || rackHeight is < 1 or > 42 || (rackStart.HasValue && rackStart.Value + rackHeight - 1 > 42))
                messages.Add(new("error", "rack_placement", "Rack placement must fit within units 1–42."));
        }
        return messages;
    }

    private static Dictionary<string, string[]> Aliases(string target) => target switch
    {
        "equipment" => new(StringComparer.OrdinalIgnoreCase)
        {
            ["managing_team"]=["managingteam","team","ownerteam"],["equipment_name"]=["equipmentname","name","devicename"],["equipment_type"]=["equipmenttype","type","category","devicetype"],
            ["manufacturer"]=["manufacturer","vendor","make"],["model"]=["model"],["serial_number"]=["serialnumber","serial","sn"],["asset_tag"]=["assettag","assetid"],["hostname"]=["hostname","devicehostname"],
            ["lab_location"]=["lablocation","location","site"],["pod"]=["pod","labpod"],["physical_location"]=["physicallocation","position"],["rack"]=["rack","rackname"],["rack_unit_start"]=["rackunitstart","rackunit","ru"],
            ["rack_unit_height"]=["rackunitheight","ruheight","height"],["status"]=["status"],["support_contract"]=["supportcontract","contract"],["notes"]=["notes","comments"]
        },
        "ipam" => new(StringComparer.OrdinalIgnoreCase)
        {
            ["managing_team"]=["managingteam","team","ownerteam"],["lab_location"]=["lablocation","location","site"],["pod"]=["pod","labpod"],["network_zone"]=["networkzone","zone"],["network_cidr"]=["networkcidr","cidr","network","subnet"],
            ["usable_range"]=["usablerange","range"],["ip_address"]=["ipaddress","ip"],["gateway"]=["gateway","defaultgateway"],["vlan_id"]=["vlanid","vlan"],["vlan_name"]=["vlanname"],["vrf"]=["vrf"],["status"]=["status"],
            ["equipment_number"]=["equipmentnumber","equipmentid","deviceid"],["interface_name"]=["interfacename","interface","port"],["hostname"]=["hostname"],["purpose"]=["purpose","description","notes"]
        },
        _ => new(StringComparer.OrdinalIgnoreCase)
        {
            ["lab_location"]=["lablocation","location","site"],["pod"]=["pod","labpod"],["from_equipment_number"]=["fromequipmentnumber","fromdevice","fromequipment"],["from_interface"]=["frominterface","fromport"],
            ["to_equipment_number"]=["toequipmentnumber","todevice","toequipment"],["to_interface"]=["tointerface","toport"],["media_type"]=["mediatype","media","cabletype"],["cable_label"]=["cablelabel","label"],
            ["vlan_id"]=["vlanid","vlan"],["ip_address"]=["ipaddress","ip"],["status"]=["status"],["notes"]=["notes","comments"]
        }
    };

    private static string[] RequiredFields(string target) => target switch
    { "equipment" => ["managing_team","equipment_name","equipment_type","lab_location"], "ipam" => ["managing_team","lab_location","pod","network_cidr"], _ => ["lab_location","from_equipment_number","from_interface","to_equipment_number","to_interface"] };
    private static bool IsSensitiveHeader(string header) { var compact = Compact(header); return SensitiveTerms.Any(compact.Contains); }
    private static string Fingerprint(string target, Dictionary<string, string> payload) { var canonical = target + "|" + string.Join("|", payload.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value.Trim().ToLowerInvariant()}")); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant(); }
    private static string CleanHeader(string value) => LabEquipmentTrackerModule.Clean(value, 120).ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
    private static string Compact(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
    private static string Value(Dictionary<string, string> row, string key) => row.TryGetValue(key, out var value) ? LabEquipmentTrackerModule.Clean(value, 4000) : string.Empty;
    private static string Required(Dictionary<string, string> row, string key) => Value(row, key) is { Length: > 0 } value ? value : throw new InvalidDataException($"Required import value {key} is missing.");
    private static int? Int(Dictionary<string, string> row, string key) => int.TryParse(Value(row, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static string Choice(Dictionary<string, string> row, string key, string[] allowed, string fallback) { var value = Normalize(Value(row, key)); return allowed.Contains(value, StringComparer.OrdinalIgnoreCase) ? value : fallback; }
    private static void AddNullableInt(NpgsqlCommand command, string name, int? value) => command.Parameters.Add(name, NpgsqlDbType.Integer).Value = value.HasValue ? value.Value : DBNull.Value;
    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value) => command.Parameters.Add(name, NpgsqlDbType.Uuid).Value = value.HasValue ? value.Value : DBNull.Value;
    private static IResult Bad(string code, string message) => Results.BadRequest(new { module = Module, code, message });
    private sealed record ParsedImport(IReadOnlyList<string> Headers, IReadOnlyList<SourceRow> Rows);
    private sealed record SourceRow(string Sheet, int RowNumber, Dictionary<string, string> Values);
    private sealed record ValidationMessage(string Level, string Field, string Message);
}
