using ProjectTime.Api.Ai;

namespace ProjectTime.Api.Modules;

public static class PulseAiPrivateDocumentPipelineModule
{
    public static IEndpointRouteBuilder MapPulseAiPrivateDocumentPipelineEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/celar-ai/v1/documents/pipeline/readiness",
            (Func<HttpContext, PulseAiPrivateDocumentPipelineService, CancellationToken, Task<IResult>>)GetReadinessAsync);
        endpoints.MapGet(
            "/api/celar-ai/v1/documents/inventory",
            (Func<HttpContext, PulseAiPrivateDocumentPipelineService, CancellationToken, Task<IResult>>)GetInventoryAsync);
        endpoints.MapGet(
            "/api/celar-ai/v1/documents/{documentId:guid}/processing-preview",
            (Func<Guid, HttpContext, PulseAiPrivateDocumentPipelineService, CancellationToken, Task<IResult>>)GetProcessingPreviewAsync);

        // Transport-only compatibility routes for already deployed callers.
        endpoints.MapGet(
            "/api/pulse-ai/v1/documents/pipeline/readiness",
            (Func<HttpContext, PulseAiPrivateDocumentPipelineService, CancellationToken, Task<IResult>>)GetReadinessAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/documents/inventory",
            (Func<HttpContext, PulseAiPrivateDocumentPipelineService, CancellationToken, Task<IResult>>)GetInventoryAsync);
        endpoints.MapGet(
            "/api/pulse-ai/v1/documents/{documentId:guid}/processing-preview",
            (Func<Guid, HttpContext, PulseAiPrivateDocumentPipelineService, CancellationToken, Task<IResult>>)GetProcessingPreviewAsync);

        endpoints.MapPulseAiPrivateRuntimeEndpoints();
        return endpoints;
    }

    private static async Task<IResult> GetReadinessAsync(
        HttpContext context,
        PulseAiPrivateDocumentPipelineService pipeline,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();

        var readiness = await pipeline.GetReadinessAsync(
            effectiveUserId.Value,
            cancellationToken);

        return Results.Ok(new
        {
            module = "011",
            feature = "private_document_processing_and_permission_scoped_indexing",
            status = readiness.Status,
            contractVersion = PulseAiPrivateDocumentPipelinePolicy.ContractVersion,
            access = AccessEvidence(context, effectiveUserId.Value),
            readiness,
            processingStages = new[]
            {
                new { order = 1, stage = "authorize", state = "implemented", detail = "Resolve the effective user and project scope before retrieving document metadata or storage references." },
                new { order = 2, stage = "admit", state = "implemented_preview", detail = "Enforce upload-root confinement, allowlisted formats, file signatures, size limits, archive expansion limits, and malware-scan attestation." },
                new { order = 3, stage = "extract", state = "implemented_preview", detail = "Extract PDF, DOCX, PPTX, XLSX, HTML, XML, CSV, Markdown, JSON, and text content inside the private Pulse runtime." },
                new { order = 4, stage = "ocr", state = readiness.OcrEndpointConfigured ? "configured_not_executed" : "not_configured", detail = "Route image-only documents to a separately approved private OCR service." },
                new { order = 5, stage = "chunk", state = "implemented_preview", detail = "Create deterministic citation-preserving chunks with overlap, checksums, page or sheet anchors, and token estimates." },
                new { order = 6, stage = "embed", state = readiness.PrivateEmbeddingEndpointConfigured ? "configured_execution_locked" : "not_configured", detail = "Generate embeddings only through the approved private embedding endpoint." },
                new { order = 7, stage = "index", state = readiness.PrivateVectorIndexConfigured ? "configured_write_locked" : "not_configured", detail = "Write only permission-scoped records carrying required security and citation metadata." },
                new { order = 8, stage = "evaluate", state = "contract_defined", detail = "Run retrieval, citation, authorization, freshness, and revocation tests before production activation." }
            },
            locked = new
            {
                databaseWrites = true,
                extractionStatusMutation = true,
                aiContextSummaryMutation = true,
                embeddingExecution = true,
                vectorIndexWrites = true,
                ocrExecution = true,
                externalProviderCalls = true,
                training = true,
                deployment = true
            },
            stateChanged = false,
            databaseChanged = false,
            externalProviderCalled = false
        });
    }

    private static async Task<IResult> GetInventoryAsync(
        HttpContext context,
        PulseAiPrivateDocumentPipelineService pipeline,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();

        var projectCode = Query(context, "projectCode", 100);
        var category = Query(context, "category", 80);
        var extractionStatus = Query(context, "extractionStatus", 60);
        var limit = int.TryParse(context.Request.Query["limit"], out var parsedLimit)
            ? Math.Clamp(parsedLimit, 1, 500)
            : 100;

        var inventory = await pipeline.ListInventoryAsync(
            effectiveUserId.Value,
            projectCode,
            category,
            extractionStatus,
            limit,
            cancellationToken);

        return Results.Ok(new
        {
            module = "011",
            feature = "permission_aware_private_document_inventory",
            status = "authorized_inventory_loaded",
            contractVersion = PulseAiPrivateDocumentPipelinePolicy.ContractVersion,
            access = AccessEvidence(context, effectiveUserId.Value),
            filters = new
            {
                projectCode,
                category,
                extractionStatus,
                limit
            },
            summary = new
            {
                documentCount = inventory.Count,
                supportedCount = inventory.Count(item => item.SupportedByNativePipeline),
                storedFileAvailableCount = inventory.Count(item => item.StoredFileExists),
                admittedForPreviewCount = inventory.Count(item => item.ProductionAdmissionReady),
                existingContextReadyCount = inventory.Count(item => item.ExistingContextSummaryReady),
                sowCount = inventory.Count(item => item.DocumentCategory.Equals("sow", StringComparison.OrdinalIgnoreCase)),
                gsdCount = inventory.Count(item => item.DocumentCategory.Equals("gsd", StringComparison.OrdinalIgnoreCase))
            },
            documents = inventory.Select(item => item.ToPublicEvidence()).ToArray(),
            privacy = new
            {
                boundary = PulseAiPrivateDocumentPipelinePolicy.PrivacyBoundary,
                storagePathsReturned = false,
                rawDocumentTextReturned = false,
                contextSummariesReturned = false,
                embeddingsReturned = false,
                externalProviderCalled = false
            },
            rules = new[]
            {
                "Inventory is filtered by the effective user's authorized project scope before metadata is returned.",
                "Only active engineering-visible project documents are eligible for this private AI pipeline.",
                "A supported file extension alone does not admit a document; path, signature, size, archive, and malware controls must also pass.",
                "Multiple SOW or GSD versions require an explicit authoritative-version decision before production indexing."
            },
            generatedAt = DateTimeOffset.UtcNow,
            stateChanged = false
        });
    }

    private static async Task<IResult> GetProcessingPreviewAsync(
        Guid documentId,
        HttpContext context,
        PulseAiPrivateDocumentPipelineService pipeline,
        CancellationToken cancellationToken)
    {
        var effectiveUserId = EffectiveUserId(context);
        if (effectiveUserId is null) return SessionRequired();

        var result = await pipeline.BuildProcessingPreviewAsync(
            effectiveUserId.Value,
            documentId,
            cancellationToken);

        if (result is null)
        {
            return Results.Json(new
            {
                module = "011",
                status = "document_not_found_or_not_authorized",
                message = "The requested document was not found in the current effective user's authorized private document scope."
            }, statusCode: StatusCodes.Status404NotFound);
        }

        return Results.Ok(new
        {
            module = "011",
            feature = "private_document_processing_preview",
            status = result.Status,
            access = AccessEvidence(context, effectiveUserId.Value),
            preview = result.ToPublicEvidence(),
            productionSequence = new[]
            {
                "Obtain a verifiable malware-scan result and reject unsafe or unsupported files.",
                "Extract text privately while preserving page, slide, worksheet, heading, and section anchors.",
                "Identify image-only material and route it only to the approved private OCR adapter.",
                "Normalize and chunk text with deterministic IDs, overlap, checksums, and source citations.",
                "Resolve document version authority and retain supersession evidence.",
                "Generate embeddings through the private embedding endpoint.",
                "Write security-filtered records to the approved hybrid/vector index.",
                "Run authorization, citation, retrieval, freshness, conflict, and revocation evaluations.",
                "Only then mark the exact document version ready for Timesheet, Help/Search, or FlowHive retrieval."
            },
            nonNegotiableControls = new[]
            {
                "Raw extracted text, private chunks, and embeddings are not returned by this endpoint.",
                "Raw document content is not sent to Claude, OpenAI, or another public external provider.",
                "No extraction, chunk, summary, embedding, or index record is persisted by this preview.",
                "A document cannot be considered production-ready based only on file extension or upload success.",
                "Revoking project or document access must also revoke retrieval eligibility without retraining a model."
            },
            stateChanged = false,
            databaseChanged = false,
            embeddingExecuted = false,
            vectorIndexChanged = false,
            externalProviderCalled = false
        });
    }

    private static Guid? EffectiveUserId(HttpContext context)
    {
        if (context.Items.TryGetValue("ProjectPulseEffectiveUserId", out var effective)
            && effective is Guid effectiveUserId)
        {
            return effectiveUserId;
        }

        if (context.Items.TryGetValue("ProjectPulseSessionUserId", out var session)
            && session is Guid sessionUserId)
        {
            return sessionUserId;
        }

        return null;
    }

    private static Guid? ActualUserId(HttpContext context)
    {
        if (context.Items.TryGetValue("ProjectPulseActualUserId", out var actual)
            && actual is Guid actualUserId)
        {
            return actualUserId;
        }

        if (context.Items.TryGetValue("ProjectPulseSessionUserId", out var session)
            && session is Guid sessionUserId)
        {
            return sessionUserId;
        }

        return null;
    }

    private static object AccessEvidence(HttpContext context, Guid effectiveUserId)
    {
        var actualUserId = ActualUserId(context) ?? effectiveUserId;
        var isViewAs = actualUserId != effectiveUserId
            || (context.Items.TryGetValue("ProjectPulseIsViewAs", out var value)
                && value is bool active
                && active);
        return new
        {
            actualUserId,
            effectiveUserId,
            isViewAs,
            mode = isViewAs ? "administrator_read_only_view_as" : "current_user",
            mutationAuthorityTransferred = false,
            serverAuthorized = true
        };
    }

    private static string Query(HttpContext context, string key, int maximumLength)
    {
        var value = context.Request.Query[key].ToString().Trim();
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static IResult SessionRequired() =>
        Results.Json(new
        {
            module = "011",
            status = "session_required",
            message = "A valid Pulse session is required."
        }, statusCode: StatusCodes.Status401Unauthorized);
}
