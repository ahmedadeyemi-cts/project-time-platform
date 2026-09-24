using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Admin-managed canonical SOW template library for Module 025 SOW reference
/// sources (Phase 1). Templates are imported into the app (no live SharePoint),
/// contain no customer data, and are stored text-only. Every endpoint is gated by
/// the <see cref="Module025ReferenceSourcePolicy"/> dark-launch kill-switch: when
/// OFF the endpoints report disabled (404) so behaviour is identical to before.
/// Create/edit/replace/delete are administrator-only; list/detail require Module
/// 025 generation authority and non-administrators only ever see active records.
/// </summary>
public static class Module025CanonicalReferenceModule
{
    private const string RoutePrefix = "/api/module025/canonical-references";
    private static readonly byte[] ZipMagic = { 0x50, 0x4B, 0x03, 0x04 };

    public static WebApplication MapModule025CanonicalReferenceEndpoints(this WebApplication app)
    {
        app.MapGet(RoutePrefix, (Func<string?, HttpContext, CancellationToken, Task<IResult>>)ListAsync);
        app.MapGet(RoutePrefix + "/{id:guid}", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)GetAsync);
        app.MapPost(RoutePrefix, (Func<HttpContext, CancellationToken, Task<IResult>>)CreateAsync);
        app.MapPut(RoutePrefix + "/{id:guid}", (Func<Guid, Module025CanonicalReferenceUpdateRequest, HttpContext, CancellationToken, Task<IResult>>)UpdateAsync);
        app.MapPost(RoutePrefix + "/{id:guid}/replace", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)ReplaceAsync);
        app.MapDelete(RoutePrefix + "/{id:guid}", (Func<Guid, HttpContext, CancellationToken, Task<IResult>>)DeleteAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(string? active, HttpContext context, CancellationToken cancellationToken)
    {
        if (!Module025ReferenceSourcePolicy.Enabled) return Disabled();
        var opened = await Module025SowGsdModule.OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await SchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await Module025SowGsdModule.ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return Module025SowGsdModule.SessionRequired();
        if (!await HasReferenceAuthorityAsync(access, context, cancellationToken)) return Module025SowGsdModule.Forbidden("module025_reference_read");

        // Author view is limited to active references; only an administrator's
        // management view may list inactive templates.
        var activeOnly = !access.IsAdministrator || string.Equals(active, "true", StringComparison.OrdinalIgnoreCase);
        const string sql = """
            SELECT id, label, COALESCE(project_name,''), COALESCE(original_filename,''), COALESCE(source_sha256,''),
                   char_length(source_text), active, created_at, updated_at
            FROM module025_canonical_references
            WHERE (@active_only = FALSE OR active = TRUE)
            ORDER BY active DESC, updated_at DESC
            LIMIT 500;
            """;
        var rows = new List<object>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("active_only", activeOnly);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                id = reader.GetGuid(0),
                label = reader.GetString(1),
                projectName = reader.GetString(2),
                originalFilename = reader.GetString(3),
                sourceSha256 = reader.GetString(4),
                sourceTextLength = reader.GetInt32(5),
                active = reader.GetBoolean(6),
                createdAt = reader.GetFieldValue<DateTimeOffset>(7),
                updatedAt = reader.GetFieldValue<DateTimeOffset>(8)
            });
        }
        return Results.Ok(new { status = "module025_canonical_references_loaded", scope = access.IsAdministrator && !activeOnly ? "management" : "author", count = rows.Count, references = rows });
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, CancellationToken cancellationToken)
    {
        if (!Module025ReferenceSourcePolicy.Enabled) return Disabled();
        var opened = await Module025SowGsdModule.OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await SchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await Module025SowGsdModule.ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return Module025SowGsdModule.SessionRequired();
        if (!await HasReferenceAuthorityAsync(access, context, cancellationToken)) return Module025SowGsdModule.Forbidden("module025_reference_read");

        const string sql = """
            SELECT id, label, COALESCE(project_name,''), COALESCE(original_filename,''), COALESCE(source_sha256,''),
                   source_text, active, created_at, updated_at
            FROM module025_canonical_references WHERE id=@id;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { status = "module025_reference_not_found" });
        var active = reader.GetBoolean(6);
        // Non-administrators may only view active (author-selectable) references.
        if (!active && !access.IsAdministrator) return Results.NotFound(new { status = "module025_reference_not_found" });
        return Results.Ok(new
        {
            status = "module025_canonical_reference",
            id = reader.GetGuid(0),
            label = reader.GetString(1),
            projectName = reader.GetString(2),
            originalFilename = reader.GetString(3),
            sourceSha256 = reader.GetString(4),
            extractedTextPreview = reader.GetString(5),
            sourceTextLength = reader.GetString(5).Length,
            active,
            createdAt = reader.GetFieldValue<DateTimeOffset>(7),
            updatedAt = reader.GetFieldValue<DateTimeOffset>(8)
        });
    }

    private static async Task<IResult> CreateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        if (!Module025ReferenceSourcePolicy.Enabled) return Disabled();
        if (!Module025SowGsdModule.SameOrigin(context)) return Module025SowGsdModule.OriginRejected();
        var opened = await Module025SowGsdModule.OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await SchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await Module025SowGsdModule.ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return Module025SowGsdModule.SessionRequired();
        if (!IsAdmin(access)) return Module025SowGsdModule.Forbidden("module025_reference_admin");

        if (!context.Request.HasFormContentType) return Results.BadRequest(new { status = "multipart_required", message = "Upload the template .docx as multipart/form-data." });
        var form = await context.Request.ReadFormAsync(cancellationToken);
        var label = Clean(form["label"], Module025ReferenceSourcePolicy.MaximumLabelCharacters);
        if (label.Length == 0) return Results.BadRequest(new { status = "label_required", message = "Provide a label for the canonical reference." });
        var projectName = Clean(form["projectName"], Module025ReferenceSourcePolicy.MaximumProjectNameCharacters);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        var extraction = await ExtractDocxAsync(file, context, cancellationToken);
        if (extraction.Error is not null) return extraction.Error;

        var id = Guid.NewGuid();
        const string insert = """
            INSERT INTO module025_canonical_references(id, label, project_name, source_text, original_filename, source_sha256, active, created_by, updated_by)
            VALUES(@id, @label, @project_name, @source_text, @original_filename, @source_sha256, TRUE, @actor, @actor)
            RETURNING created_at, updated_at;
            """;
        await using var command = new NpgsqlCommand(insert, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("label", label);
        command.Parameters.AddWithValue("project_name", projectName.Length == 0 ? DBNull.Value : projectName);
        command.Parameters.AddWithValue("source_text", extraction.Text!);
        command.Parameters.AddWithValue("original_filename", (object?)extraction.OriginalFilename ?? DBNull.Value);
        command.Parameters.AddWithValue("source_sha256", (object?)extraction.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("actor", access.ActualUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return Results.Created($"{RoutePrefix}/{id:D}", new { status = "module025_canonical_reference_created", id, label, projectName, active = true, sourceTextLength = extraction.Text!.Length });
    }

    private static async Task<IResult> UpdateAsync(Guid id, Module025CanonicalReferenceUpdateRequest request, HttpContext context, CancellationToken cancellationToken)
    {
        if (!Module025ReferenceSourcePolicy.Enabled) return Disabled();
        if (!Module025SowGsdModule.SameOrigin(context)) return Module025SowGsdModule.OriginRejected();
        var opened = await Module025SowGsdModule.OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await SchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await Module025SowGsdModule.ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return Module025SowGsdModule.SessionRequired();
        if (!IsAdmin(access)) return Module025SowGsdModule.Forbidden("module025_reference_admin");

        var label = request.Label is null ? null : Clean(request.Label, Module025ReferenceSourcePolicy.MaximumLabelCharacters);
        if (request.Label is not null && label!.Length == 0) return Results.BadRequest(new { status = "label_required", message = "The label cannot be blank." });
        var projectName = request.ProjectName is null ? null : Clean(request.ProjectName, Module025ReferenceSourcePolicy.MaximumProjectNameCharacters);
        var sourceText = request.SourceText is null ? null : Clean(request.SourceText, Module025ReferenceSourcePolicy.MaximumReferenceTextCharacters);
        if (request.SourceText is not null && sourceText!.Length < 20) return Results.BadRequest(new { status = "source_text_too_short", message = "The reference text must contain meaningful content." });
        if (request.SourceText is { Length: > Module025ReferenceSourcePolicy.MaximumReferenceTextCharacters })
            return Results.BadRequest(new { status = "source_text_too_long", message = "Reference text must be at most 30,000 characters. No text was truncated." });

        const string update = """
            UPDATE module025_canonical_references
            SET label = COALESCE(@label, label),
                project_name = CASE WHEN @project_name_set THEN @project_name ELSE project_name END,
                source_text = COALESCE(@source_text, source_text),
                source_sha256 = CASE WHEN @source_text IS NULL THEN source_sha256 ELSE NULL END,
                original_filename = CASE WHEN @source_text IS NULL THEN original_filename ELSE original_filename END,
                active = COALESCE(@active, active),
                updated_at = now(), updated_by = @actor
            WHERE id = @id
            RETURNING id, active, updated_at;
            """;
        await using var command = new NpgsqlCommand(update, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("label", (object?)label ?? DBNull.Value);
        command.Parameters.AddWithValue("project_name_set", request.ProjectName is not null);
        command.Parameters.AddWithValue("project_name", (object?)(projectName is { Length: > 0 } ? projectName : null) ?? DBNull.Value);
        command.Parameters.AddWithValue("source_text", (object?)sourceText ?? DBNull.Value);
        command.Parameters.AddWithValue("active", request.Active.HasValue ? request.Active.Value : (object)DBNull.Value);
        command.Parameters.AddWithValue("actor", access.ActualUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { status = "module025_reference_not_found" });
        return Results.Ok(new { status = "module025_canonical_reference_updated", id = reader.GetGuid(0), active = reader.GetBoolean(1), updatedAt = reader.GetFieldValue<DateTimeOffset>(2) });
    }

    private static async Task<IResult> ReplaceAsync(Guid id, HttpContext context, CancellationToken cancellationToken)
    {
        if (!Module025ReferenceSourcePolicy.Enabled) return Disabled();
        if (!Module025SowGsdModule.SameOrigin(context)) return Module025SowGsdModule.OriginRejected();
        var opened = await Module025SowGsdModule.OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await SchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await Module025SowGsdModule.ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return Module025SowGsdModule.SessionRequired();
        if (!IsAdmin(access)) return Module025SowGsdModule.Forbidden("module025_reference_admin");

        if (!context.Request.HasFormContentType) return Results.BadRequest(new { status = "multipart_required", message = "Upload the replacement .docx as multipart/form-data." });
        var form = await context.Request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        var extraction = await ExtractDocxAsync(file, context, cancellationToken);
        if (extraction.Error is not null) return extraction.Error;

        const string update = """
            UPDATE module025_canonical_references
            SET source_text=@source_text, original_filename=@original_filename, source_sha256=@source_sha256,
                updated_at=now(), updated_by=@actor
            WHERE id=@id
            RETURNING id, updated_at;
            """;
        await using var command = new NpgsqlCommand(update, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("source_text", extraction.Text!);
        command.Parameters.AddWithValue("original_filename", (object?)extraction.OriginalFilename ?? DBNull.Value);
        command.Parameters.AddWithValue("source_sha256", (object?)extraction.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("actor", access.ActualUserId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound(new { status = "module025_reference_not_found" });
        return Results.Ok(new { status = "module025_canonical_reference_replaced", id = reader.GetGuid(0), updatedAt = reader.GetFieldValue<DateTimeOffset>(1), sourceTextLength = extraction.Text!.Length });
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext context, CancellationToken cancellationToken)
    {
        if (!Module025ReferenceSourcePolicy.Enabled) return Disabled();
        if (!Module025SowGsdModule.SameOrigin(context)) return Module025SowGsdModule.OriginRejected();
        var opened = await Module025SowGsdModule.OpenConnectionAsync(context, cancellationToken);
        if (opened.Error is not null) return opened.Error;
        await using var connection = opened.Connection!;
        if (!await SchemaReadyAsync(connection, cancellationToken)) return MigrationRequired();
        var access = await Module025SowGsdModule.ResolveAccessAsync(connection, context, cancellationToken);
        if (access is null) return Module025SowGsdModule.SessionRequired();
        if (!IsAdmin(access)) return Module025SowGsdModule.Forbidden("module025_reference_admin");

        await using var command = new NpgsqlCommand("DELETE FROM module025_canonical_references WHERE id=@id RETURNING id;", connection);
        command.Parameters.AddWithValue("id", id);
        if (await command.ExecuteScalarAsync(cancellationToken) is null) return Results.NotFound(new { status = "module025_reference_not_found" });
        return Results.Ok(new { status = "module025_canonical_reference_deleted", id });
    }

    // Validates the upload by magic bytes + docx container shape, enforces the
    // size cap, and extracts text through the governed private extractor while
    // honoring its extraction-safety policy. Text-only: the file is deleted after.
    private static async Task<(string? Text, string? OriginalFilename, string? Sha256, IResult? Error)> ExtractDocxAsync(
        IFormFile? file, HttpContext context, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return (null, null, null, Results.BadRequest(new { status = "file_required", message = "Attach a template .docx file." }));
        var pipeline = PulseAiDocumentPipelineOptions.FromEnvironment();
        if (file.Length > pipeline.MaximumFileBytes)
            return (null, null, null, Results.Json(new { status = "file_too_large", message = $"The template must be at most {pipeline.MaximumFileBytes} bytes." }, statusCode: StatusCodes.Status413PayloadTooLarge));
        var originalName = Clean(file.FileName, 260);
        if (!originalName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            return (null, null, null, DocxRejected());

        byte[] bytes;
        await using (var input = file.OpenReadStream())
        await using (var buffer = new MemoryStream())
        {
            await CopyBoundedAsync(input, buffer, pipeline.MaximumFileBytes, cancellationToken);
            bytes = buffer.ToArray();
        }
        if (!IsDocx(bytes, file.ContentType)) return (null, null, null, DocxRejected());

        var root = ProjectPulseUploadStorage.ResolveRoot();
        var token = Guid.NewGuid().ToString("N");
        var folder = Path.Combine(root, "module025", "canonical-references", token);
        var storedFileName = token + ".docx";
        var storedPath = Path.Combine(folder, storedFileName);
        if (!IsConfined(root, storedPath)) return (null, null, null, StorageUnavailable());
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(storedPath, bytes, cancellationToken);
            var documentId = Guid.NewGuid();
            var source = new PulseAiAuthorizedDocumentSource(
                DocumentId: documentId,
                ProjectId: null,
                ProjectCode: string.Empty,
                ProjectName: "Module 025 canonical reference template",
                CustomerName: string.Empty,
                DocumentType: "module025_canonical_reference",
                DocumentCategory: "module025_canonical_reference",
                OriginalFileName: originalName,
                StoredFileName: storedFileName,
                StoragePath: storedPath,
                ContentType: file.ContentType,
                SizeBytes: bytes.Length,
                EngineeringVisible: false,
                AiTimesheetContextEnabled: false,
                ExtractionStatus: "not_started",
                ExistingContextSummaryReady: false,
                ContextLastProcessedAt: null,
                UploadedAt: DateTimeOffset.UtcNow,
                UploadSource: "module025_canonical_reference_admin",
                AccessScope: "administrator_only",
                Classification: "internal_template",
                RoleCodes: []);
            var extractor = context.RequestServices.GetRequiredService<PulseAiPrivateDocumentExtractionService>();
            var result = await extractor.ExtractAsync(source, pipeline, cancellationToken);
            if (!result.ExtractionSucceeded)
                return (null, null, null, Results.UnprocessableEntity(new { status = "module025_reference_extraction_failed", message = "The template could not be safely extracted. Confirm the document is a valid .docx and that extraction is enabled.", diagnostics = result.Blockers }));
            var text = string.Join("\n\n", result.Sections.Select(section => section.Text).Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
            if (text.Length == 0)
                return (null, null, null, Results.UnprocessableEntity(new { status = "module025_reference_empty", message = "No usable text was extracted from the template." }));
            if (text.Length > Module025ReferenceSourcePolicy.MaximumReferenceTextCharacters)
                text = text[..Module025ReferenceSourcePolicy.MaximumReferenceTextCharacters];
            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return (text, originalName, sha256, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            return (null, null, null, StorageUnavailable());
        }
        finally
        {
            TryDelete(storedPath);
            TryDeleteDirectory(folder);
        }
    }

    private static bool IsDocx(byte[] bytes, string? contentType)
    {
        if (bytes.Length < 4 || !bytes.AsSpan(0, 4).SequenceEqual(ZipMagic)) return false;
        if (!string.IsNullOrWhiteSpace(contentType)
            && !contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
            && !contentType.Contains("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            && !contentType.Contains("application/zip", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var hasContentTypes = archive.GetEntry("[Content_Types].xml") is not null;
            var hasWordDocument = archive.Entries.Any(entry => entry.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase));
            return hasContentTypes && hasWordDocument;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes) throw new InvalidOperationException("upload_exceeds_maximum_bytes");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task<bool> HasReferenceAuthorityAsync(Module025AccessContext access, HttpContext context, CancellationToken cancellationToken)
    {
        var grant = access.IsProtectedTestUatRoleFixture ? Module025ProtectedTestUatAccess.CurrentWorkerGrant() : null;
        var currentAccess = await context.RequestServices.GetRequiredService<PulseAiPrivateRagRepository>()
            .LoadAccessAsync(access.EffectiveUserId, cancellationToken);
        return Module025SowGsdModule.HasGenerationAuthority(access, currentAccess, grant);
    }

    private static bool IsAdmin(Module025AccessContext access) => access.IsAdministrator && !access.IsViewAs;

    private static async Task<bool> SchemaReadyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass('public.module025_canonical_references') IS NOT NULL;", connection);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static bool IsConfined(string root, string candidate)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(candidate).StartsWith(normalizedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ } }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { /* best effort */ } }

    private static string Clean(string? value, int maximum)
    {
        var clean = value?.Trim() ?? string.Empty;
        return clean.Length <= maximum ? clean : clean[..maximum];
    }

    private static IResult Disabled() => Results.NotFound(new { status = "module025_reference_sources_disabled" });
    private static IResult MigrationRequired() => Results.Json(new { status = "module025_reference_scope_migration_required", migration = "125_module025_canonical_references", message = "Apply the Module 025 canonical-reference migration before using this workflow." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    private static IResult DocxRejected() => Results.BadRequest(new { status = "invalid_document_type", message = "Only Microsoft Word .docx templates are accepted." });
    private static IResult StorageUnavailable() => Results.Json(new { status = "module025_reference_storage_unavailable", message = "The template could not be staged for extraction." }, statusCode: StatusCodes.Status503ServiceUnavailable);
}

/// <summary>
/// Edit request for a canonical reference. Every field is optional; only supplied
/// fields are changed. The client submits an identifier and edit fields only.
/// </summary>
public sealed record Module025CanonicalReferenceUpdateRequest(
    string? Label = null,
    string? ProjectName = null,
    string? SourceText = null,
    bool? Active = null);
