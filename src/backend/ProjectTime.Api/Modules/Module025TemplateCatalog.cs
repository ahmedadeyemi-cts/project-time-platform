using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

public static partial class Module025SowGsdModule
{
    private const string TemplateCandidateMigration = "117_module025_template_candidates";
    private const int TemplateRequestLimit = 6 * 1024 * 1024;
    private const string TemplateVisibilitySql = """
        (@administrator OR candidate.organization_visible OR candidate.owner_user_id=@user_id OR EXISTS (
            SELECT 1 FROM reporting_relationships relationship
            WHERE relationship.employee_user_id=@user_id
              AND (relationship.manager_user_id=candidate.owner_user_id OR relationship.team_lead_user_id=candidate.owner_user_id)
              AND relationship.effective_start_date<=CURRENT_DATE
              AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE)))
        """;

    private static void MapModule025TemplateCatalogEndpoints(WebApplication app)
    {
        app.MapGet("/api/module025/sow-gsd/templates", (Func<HttpContext, CancellationToken, Task<IResult>>)TemplateCatalogAsync);
        app.MapPost("/api/module025/sow-gsd/templates", (Func<HttpContext, CancellationToken, Task<IResult>>)StageTemplateCandidateAsync);
        app.MapGet("/api/module025/sow-gsd/templates/{versionId:guid}/file", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DownloadTemplateCandidateAsync);
        app.MapGet("/api/module025/sow-gsd/templates/{versionId:guid}/preview", (Func<Guid, string?, HttpContext, CancellationToken, Task<IResult>>)PreviewTemplateCandidateAsync);
    }

    private static async Task<IResult> TemplateCatalogAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        var schemaReady = await TemplateCatalogSchemaReadyAsync(connection, cancellationToken);
        var candidates = new List<object>();
        if (schemaReady)
        {
            await using var command = new NpgsqlCommand($"""
                SELECT template_version_id, owner_display_name, owner_team_name, document_kind, customer_program,
                       version_number, label, change_notes, file_name, content_sha256, octet_length(file_content),
                       created_at, validation_json::text, organization_visible
                FROM module025_template_candidates candidate
                WHERE {TemplateVisibilitySql}
                ORDER BY created_at DESC LIMIT 200;
                """, connection);
            AddTemplateScope(command, access);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) candidates.Add(new
            {
                versionId = reader.GetGuid(0), ownerDisplayName = reader.GetString(1), teamName = reader.GetString(2),
                documentKind = reader.GetString(3), customerProgram = reader.GetString(4), version = reader.GetInt32(5),
                label = reader.GetString(6), changeNotes = reader.GetString(7), fileName = reader.GetString(8),
                sha256 = reader.GetString(9), sizeBytes = reader.GetInt32(10), createdAt = reader.GetFieldValue<DateTimeOffset>(11),
                validation = ParseJson(reader.GetString(12), JsonValueKind.Object), organizationVisible = reader.GetBoolean(13),
                status = "awaiting_mapping", usedForExports = false
            });
        }
        return Results.Ok(new
        {
            status = "module025_template_catalog", schemaReady,
            migration = schemaReady ? null : TemplateCandidateMigration,
            canStage = schemaReady && await HasTemplateManagementScopeAsync(connection, access, cancellationToken), canActivate = false,
            maximumFileBytes = Module025TemplatePackage.MaximumFileBytes,
            catalogLimit = 200,
            activeExporters = new[]
            {
                new { key = "sow_document", label = "SOW document", programs = "Standard, Toyota and Hyundai", implementation = "Built-in document layout", readiness = "Current export format. Uploaded Word templates require approved field mappings before use." },
                new { key = "standard_gsd", label = "Standard GSD", programs = "Standard", implementation = "Embedded standard workbook", readiness = "Current exporter populates the embedded workbook and maintains formulas in code. Arbitrary uploaded formulas are not yet supported." },
                new { key = "haea_gsd", label = "Toyota / Hyundai GSD", programs = "Toyota and Hyundai", implementation = "Built-in workbook layout", readiness = "Current workbook is generated by code. Customer originals and verified input mappings are required before replacing it." }
            },
            candidates,
            activationRequirements = new[] { "Agree writable cells or document fields", "Validate formulas, task capacity, regular and after-hours allocations", "Verify populated SOW/GSD outputs", "Pin approved template versions to future document packages" },
            stateChanged = false
        });
    }

    private static async Task<IResult> StageTemplateCandidateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!SameOrigin(context)) return OriginRejected();
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        if (!await HasTemplateManagementScopeAsync(connection, access, cancellationToken)) return Forbidden(access.IsViewAs ? "view_as_read_only" : "module025_template_manager");
        if (!await TemplateCatalogSchemaReadyAsync(connection, cancellationToken)) return TemplateCatalogMigrationRequired();

        // Read manually with a hard bound, including chunked requests, before decoding base64.
        if (context.Request.ContentLength > TemplateRequestLimit) return TemplateUploadTooLarge();
        using var body = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await context.Request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (body.Length + count > TemplateRequestLimit) return TemplateUploadTooLarge();
            await body.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        Module025TemplateUploadRequest? request;
        try { request = JsonSerializer.Deserialize<Module025TemplateUploadRequest>(body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web)); }
        catch (JsonException) { return Results.BadRequest(new { message = "Enter the template details and select an original .docx or .xlsx file." }); }
        if (request is null || request.DocumentKind is not ("sow" or "gsd") || request.CustomerProgram is not ("standard" or "toyota" or "hyundai")
            || string.IsNullOrWhiteSpace(request.Label) || request.Label.Length > 160
            || string.IsNullOrWhiteSpace(request.ChangeNotes) || request.ChangeNotes.Length > 2000)
            return Results.BadRequest(new { message = "Select SOW or GSD and a customer program. Enter a label (160 characters maximum) and change notes (2,000 characters maximum)." });
        byte[] content;
        try { content = Convert.FromBase64String(request.ContentBase64 ?? string.Empty); }
        catch (FormatException) { return Results.BadRequest(new { message = "The uploaded file could not be decoded. Select the file again." }); }
        var validation = Module025TemplatePackage.Validate(request.DocumentKind, request.FileName, content);
        if (!validation.Valid) return Results.BadRequest(new { status = "template_package_invalid", message = validation.Message });

        var versionId = Guid.NewGuid();
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Keep per-owner version assignment and duplicate detection atomic.
        await using (var guard = new NpgsqlCommand("SELECT pg_advisory_xact_lock(250117);", connection, transaction))
            await guard.ExecuteNonQueryAsync(cancellationToken);
        await using (var duplicate = new NpgsqlCommand("SELECT template_version_id FROM module025_template_candidates WHERE owner_user_id=@owner AND document_kind=@kind AND customer_program=@program AND content_sha256=@hash", connection, transaction))
        {
            duplicate.Parameters.AddWithValue("owner", access.EffectiveUserId);
            duplicate.Parameters.AddWithValue("kind", request.DocumentKind);
            duplicate.Parameters.AddWithValue("program", request.CustomerProgram);
            duplicate.Parameters.AddWithValue("hash", sha256);
            if (await duplicate.ExecuteScalarAsync(cancellationToken) is Guid existing)
                return Results.Conflict(new { status = "template_already_staged", versionId = existing, message = "This exact file is already retained for this program. Upload a changed original to create a new version." });
        }
        await using var insert = new NpgsqlCommand("""
            INSERT INTO module025_template_candidates
                (template_version_id,owner_user_id,owner_display_name,owner_team_name,organization_visible,document_kind,
                 customer_program,version_number,label,change_notes,file_name,content_sha256,file_content,validation_json)
            SELECT @id,@owner,@owner_name,@team,@organization,@kind,@program,
                   COALESCE(MAX(version_number),0)+1,@label,@notes,@file,@hash,@content,@validation
            FROM module025_template_candidates WHERE owner_user_id=@owner AND document_kind=@kind AND customer_program=@program
            RETURNING version_number;
            """, connection, transaction);
        insert.Parameters.AddWithValue("id", versionId);
        insert.Parameters.AddWithValue("owner", access.EffectiveUserId);
        insert.Parameters.AddWithValue("owner_name", access.DisplayName);
        insert.Parameters.AddWithValue("team", Clean(access.TeamName, 255));
        insert.Parameters.AddWithValue("organization", access.IsAdministrator);
        insert.Parameters.AddWithValue("kind", request.DocumentKind);
        insert.Parameters.AddWithValue("program", request.CustomerProgram);
        insert.Parameters.AddWithValue("label", request.Label.Trim());
        insert.Parameters.AddWithValue("notes", request.ChangeNotes.Trim());
        insert.Parameters.AddWithValue("file", request.FileName!);
        insert.Parameters.AddWithValue("hash", sha256);
        insert.Parameters.AddWithValue("content", NpgsqlDbType.Bytea, content);
        insert.Parameters.AddWithValue("validation", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(new { packageValidated = true, formulaCount = validation.FormulaCount, worksheetCount = validation.WorksheetCount, mappingValidated = false, outputVerified = false }));
        var version = (int)(await insert.ExecuteScalarAsync(cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { status = "template_staged", versionId, version, sha256, usedForExports = false, stateChanged = true, message = "Original retained as an immutable review candidate. Current document exports are unchanged." });
    }

    private static async Task<IResult> DownloadTemplateCandidateAsync(Guid versionId, HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        if (!await TemplateCatalogSchemaReadyAsync(connection, cancellationToken)) return TemplateCatalogMigrationRequired();
        await using var command = new NpgsqlCommand($"SELECT file_name,document_kind,file_content FROM module025_template_candidates candidate WHERE template_version_id=@id AND {TemplateVisibilitySql};", connection);
        command.Parameters.AddWithValue("id", versionId);
        AddTemplateScope(command, access);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound();
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(reader.GetFieldValue<byte[]>(2), reader.GetString(1) == "sow"
            ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", reader.GetString(0));
    }

    private static async Task<IResult> PreviewTemplateCandidateAsync(Guid versionId, string? sheetId, HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeViewAsync(context);
        if (authorization is not null) return authorization;
        var opened = await OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        var access = await ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return SessionRequired();
        if (!await TemplateCatalogSchemaReadyAsync(connection, cancellationToken)) return TemplateCatalogMigrationRequired();
        await using var command = new NpgsqlCommand($"""
            SELECT file_name,document_kind,file_content,content_sha256,version_number,label,customer_program
            FROM module025_template_candidates candidate WHERE template_version_id=@id AND {TemplateVisibilitySql};
            """, connection);
        command.Parameters.AddWithValue("id", versionId);
        AddTemplateScope(command, access);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound();
        var content = reader.GetFieldValue<byte[]>(2);
        var hash = reader.GetString(3);
        if (!string.Equals(hash, Convert.ToHexStringLower(SHA256.HashData(content)), StringComparison.Ordinal))
            return Results.Conflict(new { status = "template_integrity_mismatch", message = "This stored original does not match its retained fingerprint. Contact an administrator before using it." });
        var preview = Module025TemplatePackage.Preview(reader.GetString(1), reader.GetString(0), content, sheetId);
        if (!preview.Valid) return Results.UnprocessableEntity(new { status = "template_preview_unavailable", message = preview.Message });
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Ok(new
        {
            status = "template_content_preview", versionId, version = reader.GetInt32(4), label = reader.GetString(5),
            fileName = reader.GetString(0), documentKind = reader.GetString(1), customerProgram = reader.GetString(6),
            sha256 = hash, originalHashVerified = true, usedForExports = false, canActivate = false, preview, stateChanged = false
        });
    }

    internal static bool CanStageTemplate(Module025AccessContext access) => !access.IsViewAs
        && (access.IsAdministrator || access.IsManager && access.VisibleSolutionArchitectIds.Any(id => id != access.EffectiveUserId));

    private static async Task<bool> HasTemplateManagementScopeAsync(NpgsqlConnection connection, Module025AccessContext access, CancellationToken cancellationToken)
    {
        if (!CanStageTemplate(access)) return false;
        if (access.IsAdministrator) return true;
        // Read visibility through a team-lead relationship does not grant the
        // manager's authority to maintain the team's master template candidates.
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS(SELECT 1 FROM reporting_relationships relationship
              WHERE relationship.manager_user_id=@manager
                AND relationship.employee_user_id=ANY(@visible)
                AND relationship.effective_start_date<=CURRENT_DATE
                AND (relationship.effective_end_date IS NULL OR relationship.effective_end_date>=CURRENT_DATE));
            """, connection);
        command.Parameters.AddWithValue("manager", access.EffectiveUserId);
        command.Parameters.AddWithValue("visible", access.VisibleSolutionArchitectIds.Where(id => id != access.EffectiveUserId).ToArray());
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
    private static void AddTemplateScope(NpgsqlCommand command, Module025AccessContext access)
    {
        command.Parameters.AddWithValue("administrator", access.IsAdministrator);
        command.Parameters.AddWithValue("user_id", access.EffectiveUserId);
    }
    private static async Task<bool> TemplateCatalogSchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.module025_template_candidates') IS NOT NULL;", connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }
    private static IResult TemplateCatalogMigrationRequired() => Results.Json(new { status = "module025_template_migration_required", migration = TemplateCandidateMigration, message = "Template storage is not available until its database migration is installed." }, statusCode: 503);
    private static IResult TemplateUploadTooLarge() => Results.Json(new { message = "Template files must be 4 MB or smaller." }, statusCode: 413);
}

internal sealed record Module025TemplateUploadRequest(string? FileName, string? ContentBase64, string? DocumentKind, string? CustomerProgram, string? Label, string? ChangeNotes);
internal sealed record Module025TemplatePackageValidation(bool Valid, string Message, int FormulaCount = 0, int WorksheetCount = 0);
internal sealed record Module025TemplatePreviewSheet(string Id, string Name, string Visibility);
internal sealed record Module025TemplatePreviewCell(string Address, string Value, string ValueType, string Formula, string FormulaKind, string FormulaRange);
internal sealed record Module025TemplatePreviewBlock(string Kind, string Text, string Style, IReadOnlyList<IReadOnlyList<string>> Rows);
internal sealed record Module025TemplatePreview(bool Valid, string Message, string DocumentKind,
    IReadOnlyList<Module025TemplatePreviewSheet> Sheets, string? SelectedSheetId,
    IReadOnlyList<Module025TemplatePreviewCell> Cells, IReadOnlyList<Module025TemplatePreviewBlock> Blocks,
    bool Truncated, IReadOnlyList<string> Notices);

/// <summary>Structural staging validation, not approval of mappings, formulas, or generated output.</summary>
internal static class Module025TemplatePackage
{
    internal const int MaximumFileBytes = 4 * 1024 * 1024;
    private const long MaximumExpandedBytes = 32 * 1024 * 1024;
    private const long MaximumPartBytes = 8 * 1024 * 1024;
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace DocumentRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    internal static Module025TemplatePreview Preview(string kind, string? fileName, byte[] content, string? sheetId = null)
    {
        Module025TemplatePreview Unavailable(string message) => new(false, message, kind, [], null, [], [], false, []);
        var validated = Validate(kind, fileName, content);
        if (!validated.Valid) return Unavailable(validated.Message);
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            XDocument? ReadPart(string name)
            {
                var entry = archive.GetEntry(name);
                if (entry is null) return null;
                using var source = entry.Open();
                using var reader = XmlReader.Create(source, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumPartBytes });
                return XDocument.Load(reader);
            }

            var budget = new TemplatePreviewBudget();
            if (kind == "sow")
            {
                var body = ReadPart("word/document.xml")?.Root?.Element(Word + "body");
                if (body is null) return Unavailable("The Word document has no readable body.");
                var blocks = new List<Module025TemplatePreviewBlock>();
                var elements = body.Descendants().Where(node => (node.Name == Word + "p" || node.Name == Word + "tbl") && !node.Ancestors(Word + "tbl").Any()).Take(201).ToArray();
                if (elements.Length > 200) budget.Truncated = true;
                var tableCellBudget = 600;
                foreach (var node in elements.Take(200))
                {
                    if (node.Name == Word + "p")
                    {
                        blocks.Add(new("paragraph", budget.Text(WordText(node), 2500), budget.Text(node.Element(Word + "pPr")?.Element(Word + "pStyle")?.Attribute(Word + "val")?.Value ?? "", 120), []));
                        continue;
                    }
                    var rows = new List<IReadOnlyList<string>>();
                    foreach (var row in node.Elements(Word + "tr"))
                    {
                        if (rows.Count >= 100 || tableCellBudget <= 0) { budget.Truncated = true; break; }
                        var tableCells = row.Elements(Word + "tc").Take(Math.Min(tableCellBudget, 20) + 1).ToArray();
                        var allowed = Math.Min(tableCellBudget, 20);
                        if (tableCells.Length > allowed) budget.Truncated = true;
                        var values = tableCells.Take(allowed).Select(cell => budget.Text(string.Join("\n", cell.Descendants(Word + "p").Select(WordText)), 1200)).ToArray();
                        tableCellBudget -= values.Length;
                        rows.Add(values);
                    }
                    blocks.Add(new("table", "", "", rows));
                }
                return new(true, "Original document content preview", kind, [], null, [], blocks, budget.Truncated,
                    ["Content is shown as text and tables. Page layout, fonts, images, headers, footers, and tracked changes are not rendered.", "This is the retained original, before field mapping or sample data population. Download it to inspect the complete Word layout."]);
            }

            var workbook = ReadPart("xl/workbook.xml");
            var workbookSheets = workbook?.Root?.Element(Spreadsheet + "sheets")?.Elements(Spreadsheet + "sheet").Take(201).ToArray() ?? [];
            if (workbookSheets.Length == 0) return Unavailable("The workbook does not declare any sheets to preview.");
            var sheetIds = workbookSheets.Select(sheet => sheet.Attribute("sheetId")?.Value ?? "").ToArray();
            if (sheetIds.Any(id => id.Length is 0 or > 80) || sheetIds.Distinct(StringComparer.Ordinal).Count() != sheetIds.Length)
                return Unavailable("The workbook has missing, oversized, or duplicate sheet identifiers.");
            if (workbookSheets.Length > 200) budget.Truncated = true;
            var sheets = workbookSheets.Take(200).Select(sheet => new Module025TemplatePreviewSheet(
                budget.Text(sheet.Attribute("sheetId")?.Value ?? "", 80), budget.Text(sheet.Attribute("name")?.Value ?? "Untitled sheet", 200),
                sheet.Attribute("state")?.Value is "hidden" or "veryHidden" ? sheet.Attribute("state")!.Value : "visible")).ToArray();
            var selected = string.IsNullOrEmpty(sheetId)
                ? workbookSheets.Take(200).FirstOrDefault(sheet => sheet.Attribute("state")?.Value is null or "visible") ?? workbookSheets[0]
                : workbookSheets.Take(200).FirstOrDefault(sheet => sheet.Attribute("sheetId")?.Value == sheetId);
            if (selected is null) return Unavailable("Select a sheet listed in this template version.");
            var selectedId = selected.Attribute("sheetId")?.Value ?? "";
            var relationshipId = selected.Attribute(DocumentRelationship + "id")?.Value;
            var relationships = ReadPart("xl/_rels/workbook.xml.rels");
            var relationship = relationships?.Root?.Elements().FirstOrDefault(node => node.Attribute("Id")?.Value == relationshipId
                && node.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
            var worksheetPath = ResolveWorkbookPart(relationship?.Attribute("Target")?.Value);
            if (worksheetPath is null) return Unavailable("This sheet does not have a supported worksheet relationship.");
            var worksheet = ReadPart(worksheetPath);
            if (worksheet?.Root?.Name != Spreadsheet + "worksheet") return Unavailable("The selected worksheet could not be read from the original package.");
            // Strings and formulas remain plain text. No Office renderer, formula evaluator, or external resource is invoked.
            var sharedStrings = ReadPart("xl/sharedStrings.xml")?.Root?.Elements(Spreadsheet + "si").Select(SpreadsheetText).ToArray() ?? [];
            var cells = worksheet.Root.Element(Spreadsheet + "sheetData")?.Elements(Spreadsheet + "row").SelectMany(row => row.Elements(Spreadsheet + "c"))
                .Where(cell => cell.Element(Spreadsheet + "v") is not null || cell.Element(Spreadsheet + "is") is not null || cell.Element(Spreadsheet + "f") is not null).Take(301).ToArray() ?? [];
            if (cells.Length > 300) budget.Truncated = true;
            var previewCells = new List<Module025TemplatePreviewCell>();
            foreach (var cell in cells.Take(300))
            {
                var type = cell.Attribute("t")?.Value ?? "n";
                var stored = cell.Element(Spreadsheet + "v")?.Value ?? "";
                var value = type switch
                {
                    "s" => int.TryParse(stored, out var index) && index >= 0 && index < sharedStrings.Length ? sharedStrings[index] : "[Shared string unavailable]",
                    "inlineStr" => SpreadsheetText(cell.Element(Spreadsheet + "is")),
                    "b" => stored == "1" ? "TRUE" : stored == "0" ? "FALSE" : stored,
                    _ => stored
                };
                var formula = cell.Element(Spreadsheet + "f");
                var formulaKind = formula is null ? "" : formula.Attribute("t")?.Value ?? "normal";
                var formulaText = formula?.Value ?? "";
                if (formula is not null && formulaText.Length == 0 && formulaKind == "shared") formulaText = $"Shared formula group {formula.Attribute("si")?.Value ?? "unspecified"}";
                previewCells.Add(new(budget.Text(cell.Attribute("r")?.Value ?? "", 40), budget.Text(value, 2000), type,
                    budget.Text(formulaText, 2000), budget.Text(formulaKind, 80), budget.Text(formula?.Attribute("ref")?.Value ?? "", 100)));
            }
            return new(true, "Original workbook content preview", kind, sheets, selectedId, previewCells, [], budget.Truncated,
                ["Up to 300 cells with stored values or formulas are shown for the selected sheet. Empty cells, formatting, merged-cell layout, charts, and images are not rendered.",
                 "Formula results are cached values saved in the original file, not recalculated or verified. Numbers and dates may appear as raw Excel values.",
                 "This original is awaiting writable-cell mapping and populated-output review; it is not active for GSD exports."]);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or ArgumentException)
        { return Unavailable("The original could not be previewed safely. Download it for review and contact an administrator if the problem persists."); }
    }

    private static string WordText(XElement node) => string.Concat(node.Descendants().Select(part =>
        part.Name == Word + "t" ? part.Value : part.Name == Word + "tab" ? "\t" : part.Name == Word + "br" ? "\n" : ""));
    private static string SpreadsheetText(XElement? node) => node is null ? "" : string.Concat(node.Descendants(Spreadsheet + "t")
        .Where(text => !text.Ancestors(Spreadsheet + "rPh").Any()).Select(text => text.Value));
    private static string? ResolveWorkbookPart(string? target)
    {
        if (string.IsNullOrEmpty(target) || target.Contains('\\') || target.Contains('%') || target.Contains(':') || target.StartsWith("//")) return null;
        var segments = new List<string>();
        foreach (var part in (target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target).Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == "..") { if (segments.Count == 0) return null; segments.RemoveAt(segments.Count - 1); }
            else segments.Add(part);
        }
        var path = string.Join('/', segments);
        return path.StartsWith("xl/", StringComparison.Ordinal) && path.EndsWith(".xml", StringComparison.Ordinal) ? path : null;
    }
    private sealed class TemplatePreviewBudget
    {
        private int remainingCharacters = 80000;
        internal bool Truncated { get; set; }
        internal string Text(string text, int limit)
        {
            var count = Math.Min(text.Length, Math.Min(limit, remainingCharacters));
            if (count > 0 && count < text.Length && char.IsHighSurrogate(text[count - 1]) && char.IsLowSurrogate(text[count])) count--;
            remainingCharacters -= count;
            if (count < text.Length) Truncated = true;
            return text[..count];
        }
    }
    internal static Module025TemplatePackageValidation Validate(string kind, string? fileName, byte[] content)
    {
        Module025TemplatePackageValidation Invalid(string reason) => new(false, reason);
        if (kind is not ("sow" or "gsd")) return Invalid("Select SOW or GSD.");
        var suffix = kind == "sow" ? ".docx" : ".xlsx";
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 200 || fileName.Any(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c))
            || !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return Invalid($"Select an original {suffix} file with a valid file name. Macro-enabled files are not accepted.");
        if (content.Length == 0 || content.Length > MaximumFileBytes) return Invalid("Template files must be between 1 byte and 4 MB.");
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            if (archive.Entries.Count is 0 or > 1024) return Invalid("The document package contains too many parts or is empty.");
            long expanded = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var xmlParts = new Dictionary<string, XDocument>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName;
                if (name.StartsWith('/') || name.Contains('\\') || name.Split('/').Any(part => part is ".." or ".") || !names.Add(name)) return Invalid("The document package contains invalid or duplicate paths.");
                expanded += entry.Length;
                if (entry.Length > MaximumPartBytes || expanded > MaximumExpandedBytes) return Invalid("The expanded document exceeds the template size limit.");
                if (name.Contains("vba", StringComparison.OrdinalIgnoreCase) || name.Contains("activex", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("embeddings/", StringComparison.OrdinalIgnoreCase) || name.Contains("externallinks/", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("connections", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return Invalid("Remove macros, embedded objects, external data connections, and active content before staging the template.");
                // Read every entry to enforce decompression integrity, even non-XML media parts.
                using var source = entry.Open();
                using var part = new MemoryStream();
                var buffer = new byte[16384];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (part.Length + read > MaximumPartBytes || part.Length + read > entry.Length) return Invalid("A document part exceeded its declared size.");
                    part.Write(buffer, 0, read);
                }
                if (part.Length != entry.Length) return Invalid("The document package is incomplete.");
                if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) continue;
                part.Position = 0;
                using var xmlReader = XmlReader.Create(part, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumPartBytes });
                var xml = XDocument.Load(xmlReader);
                if (name.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) && xml.Descendants().Any(node =>
                        string.Equals(node.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase)
                        || node.Attribute("Target")?.Value is { } target && (target.StartsWith("//") || target.Contains('\\') || Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri)))
                    return Invalid("Remove external document links and data relationships before staging the template.");
                if (xml.Descendants().Any(node => node.Name.LocalName is "oleObject" or "altChunk" or "object")) return Invalid("Remove embedded or active objects before staging the template.");
                xmlParts.Add(name, xml);
            }
            var mainPart = kind == "sow" ? "word/document.xml" : "xl/workbook.xml";
            var expectedContentType = kind == "sow" ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
            if (!xmlParts.TryGetValue("[Content_Types].xml", out var types) || !xmlParts.ContainsKey(mainPart)
                || !xmlParts.ContainsKey("_rels/.rels") || !types.Descendants().Any(node => node.Name.LocalName == "Override"
                    && node.Attribute("PartName")?.Value == "/" + mainPart && node.Attribute("ContentType")?.Value == expectedContentType))
                return Invalid("This file is not a supported Word document or Excel workbook package.");
            var expectedRoot = kind == "sow" ? XName.Get("document", "http://schemas.openxmlformats.org/wordprocessingml/2006/main") : XName.Get("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            if (xmlParts[mainPart].Root?.Name != expectedRoot) return Invalid("The document's primary XML part is invalid.");
            if (types.Descendants().Any(node => (node.Attribute("ContentType")?.Value ?? "").Contains("macroEnabled", StringComparison.OrdinalIgnoreCase))) return Invalid("Macro-enabled templates are not accepted.");
            var worksheets = xmlParts.Where(pair => pair.Key.StartsWith("xl/worksheets/", StringComparison.Ordinal) && pair.Key.EndsWith(".xml", StringComparison.Ordinal)).ToArray();
            if (kind == "gsd" && worksheets.Length == 0) return Invalid("The workbook must include at least one worksheet.");
            // Formula cells are counted but never recalculated or modified. Originals remain byte-for-byte identical.
            return new(true, "Package structure validated; mapping and output review remain required.", worksheets.Sum(pair => pair.Value.Descendants().Count(node => node.Name.LocalName == "f")), worksheets.Length);
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or ArgumentException)
        { return Invalid("The file is damaged, encrypted, or not a supported Office Open XML document."); }
    }
}
