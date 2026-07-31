from pathlib import Path


def replace_once(path: Path, old: str, new: str, label: str) -> None:
    source = path.read_text()
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one source block, found {count}")
    path.write_text(source.replace(old, new, 1))


executor_path = Path("src/backend/ProjectTime.Api/Ai/PulseAiSystemToolExecutor.cs")
service_path = Path("src/backend/ProjectTime.Api/Ai/PulseAiSystemIntelligenceService.cs")
services_path = Path("src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs")
validator_path = Path("src/frontend/project-time-web/scripts/validate-module-011-system-intelligence-package.mjs")
documentation_path = Path("docs/modules/module-011-pulse-ai/SYSTEM-INTELLIGENCE-AND-TROUBLESHOOTING.md")
documentation_mirror_path = Path("src/frontend/project-time-web/container-context/docs/modules/module-011-pulse-ai/SYSTEM-INTELLIGENCE-AND-TROUBLESHOOTING.md")

replace_once(
    executor_path,
    '''            var target = BuildTarget(context, definition.Path);
            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            ForwardSessionHeaders(context, request);''',
    '''            if (!TryBuildTrustedTarget(definition.Path, options, out var target, out var targetDiagnostic))
            {
                stopwatch.Stop();
                return Result(
                    definition,
                    "skipped",
                    0,
                    stopwatch,
                    0,
                    "tool_origin_rejected",
                    string.Empty,
                    [targetDiagnostic],
                    observedAt);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, target);
            ForwardSessionHeaders(context, request);''',
    "trusted target call",
)

replace_once(
    executor_path,
    '''    private static Uri BuildTarget(HttpContext context, string relativePath)
    {
        var queryIndex = relativePath.IndexOf('?');
        var path = queryIndex >= 0 ? relativePath[..queryIndex] : relativePath;
        var query = queryIndex >= 0 ? relativePath[(queryIndex + 1)..] : string.Empty;
        var builder = new UriBuilder(
            context.Request.Scheme,
            context.Request.Host.Host,
            context.Request.Host.Port ?? -1,
            path)
        {
            Query = query
        };
        return builder.Uri;
    }''',
    '''    private static bool TryBuildTrustedTarget(
        string relativePath,
        PulseAiSystemIntelligenceOptions options,
        out Uri target,
        out string diagnostic)
    {
        target = null!;
        diagnostic = "The governed same-origin tool base URI is not configured.";
        var configured = Environment.GetEnvironmentVariable(
            "PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI")?.Trim();
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var trustedBase))
        {
            diagnostic = "The governed same-origin tool base URI is missing or malformed.";
            return false;
        }

        var isHttps = string.Equals(
            trustedBase.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);
        var isLoopbackHttp = string.Equals(
                trustedBase.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase)
            && trustedBase.IsLoopback;
        if ((!isHttps && !isLoopbackHttp)
            || !string.IsNullOrEmpty(trustedBase.UserInfo)
            || !string.IsNullOrEmpty(trustedBase.Query)
            || !string.IsNullOrEmpty(trustedBase.Fragment)
            || !string.IsNullOrEmpty(trustedBase.AbsolutePath.Trim('/')))
        {
            diagnostic = "The governed same-origin tool base URI must be an HTTPS origin, or an explicit loopback HTTP development origin, without credentials, path, query, or fragment.";
            return false;
        }

        if (!AllowedSameOriginHost(trustedBase, options.AllowedSameOriginHosts))
        {
            diagnostic = "The configured same-origin tool base URI is outside PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST.";
            return false;
        }

        var normalizedPath = relativePath.StartsWith("/", StringComparison.Ordinal)
            ? relativePath
            : $"/{relativePath}";
        target = new Uri(trustedBase, normalizedPath);
        if (!string.Equals(target.Scheme, trustedBase.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(target.IdnHost, trustedBase.IdnHost, StringComparison.OrdinalIgnoreCase)
            || target.Port != trustedBase.Port)
        {
            target = null!;
            diagnostic = "The resolved tool target escaped the configured same-origin authority.";
            return false;
        }

        diagnostic = string.Empty;
        return true;
    }

    private static bool AllowedSameOriginHost(
        Uri trustedBase,
        IReadOnlyList<string> allowedHosts)
    {
        if (allowedHosts.Count == 0) return false;
        var expectedAuthority = TrustedAuthority(trustedBase);
        foreach (var rawValue in allowedHosts)
        {
            var candidate = rawValue?.Trim() ?? string.Empty;
            if (candidate.Length == 0) continue;
            if (candidate.Contains("://", StringComparison.Ordinal)
                && Uri.TryCreate(candidate, UriKind.Absolute, out var candidateUri))
            {
                candidate = TrustedAuthority(candidateUri);
            }
            else
            {
                candidate = candidate.TrimEnd('/').ToLowerInvariant();
            }

            if (string.Equals(candidate, expectedAuthority, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string TrustedAuthority(Uri value) =>
        value.IsDefaultPort
            ? value.IdnHost.ToLowerInvariant()
            : $"{value.IdnHost.ToLowerInvariant()}:{value.Port}";''',
    "trusted target implementation",
)

replace_once(
    services_path,
    '''        services.AddHttpClient("PulseAiSystemTools", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(75);
        });''',
    '''        services.AddHttpClient("PulseAiSystemTools", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(75);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        });''',
    "redirect-safe tool client",
)

replace_once(
    service_path,
    '''        var privateRag = await _privateRag.GetReadinessAsync(cancellationToken);
        var apis = _apiCatalog.List(limit: options.MaximumApiResults);''',
    '''        var privateRag = await _privateRag.GetReadinessAsync(cancellationToken);
        IReadOnlyList<PulseAiSystemApiDescriptor> apis = access.CanViewApis
            ? _apiCatalog.List(limit: options.MaximumApiResults)
            : Array.Empty<PulseAiSystemApiDescriptor>();''',
    "readiness API permission",
)

replace_once(
    service_path,
    '''            liveApiCatalog = new
            {
                summary = _apiCatalog.Summary(apis),
                endpointDataSourceReadAtRequestTime = true,
                sourceCodeDocumentationUsedAsRuntimeAuthority = false
            },''',
    '''            liveApiCatalog = new
            {
                authorized = access.CanViewApis,
                summary = access.CanViewApis ? _apiCatalog.Summary(apis) : null,
                unauthorizedReason = access.CanViewApis
                    ? string.Empty
                    : "VIEW_PULSE_AI_API_INVENTORY is required for route and endpoint metadata.",
                endpointDataSourceReadAtRequestTime = access.CanViewApis,
                sourceCodeDocumentationUsedAsRuntimeAuthority = false
            },''',
    "readiness API summary permission",
)

replace_once(
    service_path,
    '''        var plan = PulseAiSystemKnowledgeCatalog.Analyze(question);
        var requestedMode = NormalizeMode(request.Mode, plan.Mode);
        var conversation = await _repository.EnsureConversationAsync(
            request.ConversationId,
            actualUserId,
            effectiveUserId,
            requestedMode,
            cancellationToken);
        var conversationId = conversation?.ConversationId ?? request.ConversationId ?? Guid.NewGuid();
        var persisted = conversation is not null;''',
    '''        var plan = PulseAiSystemKnowledgeCatalog.Analyze(question);
        var requestedMode = NormalizeMode(request.Mode, plan.Mode);
        var persistenceAuthorized = actualUserId == effectiveUserId
            && access.CanViewConversations;
        var conversation = persistenceAuthorized
            ? await _repository.EnsureConversationAsync(
                request.ConversationId,
                actualUserId,
                effectiveUserId,
                requestedMode,
                cancellationToken)
            : null;
        var conversationId = conversation?.ConversationId
            ?? (persistenceAuthorized ? request.ConversationId : null)
            ?? Guid.NewGuid();
        var persisted = persistenceAuthorized && conversation is not null;''',
    "conversation persistence authority",
)

replace_once(
    service_path,
    '''        try
        {
            var apiLimit = Math.Clamp(''',
    '''        try
        {
            var accessWarnings = new List<string>();
            var apiLimit = Math.Clamp(''',
    "access warning initialization",
)

replace_once(
    service_path,
    '''            if (request.IncludeApiInventory && (plan.WantsApiInventory || access.CanViewApis))''',
    '''            if (request.IncludeApiInventory && access.CanViewApis)''',
    "question API inventory permission",
)

replace_once(
    service_path,
    '''                }
            }

            var maximumTools = Math.Clamp(''',
    '''                }
            }
            else if (request.IncludeApiInventory
                && plan.WantsApiInventory
                && !access.CanViewApis)
            {
                accessWarnings.Add(
                    "API inventory evidence was not included because the current effective user lacks VIEW_PULSE_AI_API_INVENTORY.");
            }

            var maximumTools = Math.Clamp(''',
    "API inventory warning",
)

replace_once(
    service_path,
    '''            foreach (var toolResult in toolResults)
            {
                await _repository.SaveToolEventAsync(
                    inquiryRunId,
                    toolResult,
                    options.PersistToolResponseBodies,
                    cancellationToken);
            }''',
    '''            if (persisted)
            {
                foreach (var toolResult in toolResults)
                {
                    await _repository.SaveToolEventAsync(
                        inquiryRunId,
                        toolResult,
                        options.PersistToolResponseBodies,
                        cancellationToken);
                }
            }''',
    "tool event persistence authority",
)

replace_once(
    service_path,
    '''            var warnings = new List<string>();''',
    '''            var warnings = new List<string>(accessWarnings);''',
    "authorized warning propagation",
)

replace_once(
    service_path,
    '''            await _repository.CompleteInquiryRunAsync(
                inquiryRunId,
                assistantMessage.MessageId,
                assistantStatus,
                selectedTools,
                toolResults,
                relevantApis.Count,
                finalAnswer.Confidence,
                string.Empty,
                cancellationToken);''',
    '''            if (persisted)
            {
                await _repository.CompleteInquiryRunAsync(
                    inquiryRunId,
                    assistantMessage.MessageId,
                    assistantStatus,
                    selectedTools,
                    toolResults,
                    relevantApis.Count,
                    finalAnswer.Confidence,
                    string.Empty,
                    cancellationToken);
            }''',
    "successful inquiry persistence authority",
)

replace_once(
    service_path,
    '''            await _repository.CompleteInquiryRunAsync(
                inquiryRunId,
                assistantMessage.MessageId,
                "failed",
                [],
                [],
                0,
                0m,
                Diagnostic(exception),
                cancellationToken);''',
    '''            if (persisted)
            {
                await _repository.CompleteInquiryRunAsync(
                    inquiryRunId,
                    assistantMessage.MessageId,
                    "failed",
                    [],
                    [],
                    0,
                    0m,
                    Diagnostic(exception),
                    cancellationToken);
            }''',
    "failed inquiry persistence authority",
)

replace_once(
    validator_path,
    '''    'ValidRelativeApiPath',
    'Uri.TryCreate(path, UriKind.Absolute',
    'cleanPath.StartsWith("/api/"',
    'ForwardSessionHeaders'
  ])
  && !s.executor.includes('request.Url')
  && !s.executor.includes('request.Endpoint'),''',
    '''    'ValidRelativeApiPath',
    'Uri.TryCreate(path, UriKind.Absolute',
    'cleanPath.StartsWith("/api/"',
    'TryBuildTrustedTarget',
    'PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI',
    'AllowedSameOriginHosts',
    'tool_origin_rejected',
    'ForwardSessionHeaders'
  ])
  && s.executor.indexOf('TryBuildTrustedTarget(definition.Path')
    < s.executor.indexOf('ForwardSessionHeaders(context, request)')
  && !s.executor.includes('context.Request.Host.Host')
  && !s.executor.includes('request.Url')
  && !s.executor.includes('request.Endpoint')
  && all(s.services, ['AllowAutoRedirect = false','UseCookies = false']),''',
    "trusted origin validator",
)

replace_once(
    validator_path,
    '''assert('VIEW_AS_BOUNDARY',
  s.module.includes('identities.Value.Actual != identities.Value.Effective')
  && s.module.includes('ViewAsMutationBlocked')
  && s.module.includes('mutationAuthorityTransferred = false')
  && s.documentation.includes('View-As does not transfer conversation or retest mutation authority'),
  'View-As cannot create another user’s conversation or run a safe retest');''',
    '''assert('VIEW_AS_BOUNDARY',
  s.module.includes('identities.Value.Actual != identities.Value.Effective')
  && s.module.includes('ViewAsMutationBlocked')
  && s.module.includes('mutationAuthorityTransferred = false')
  && s.service.includes('actualUserId == effectiveUserId')
  && s.service.includes('access.CanViewConversations')
  && s.documentation.includes('View-As does not transfer conversation or retest mutation authority'),
  'View-As cannot create another user’s conversation, persist inquiry evidence, or run a safe retest');

assert('PERMISSION_SCOPED_SYSTEM_EVIDENCE',
  all(s.service, [
    'IReadOnlyList<PulseAiSystemApiDescriptor> apis = access.CanViewApis',
    'summary = access.CanViewApis ? _apiCatalog.Summary(apis) : null',
    'request.IncludeApiInventory && access.CanViewApis',
    'lacks VIEW_PULSE_AI_API_INVENTORY',
    'var persistenceAuthorized = actualUserId == effectiveUserId',
    '&& access.CanViewConversations',
    'if (persisted)',
    'SaveToolEventAsync',
    'CompleteInquiryRunAsync'
  ])
  && !s.service.includes('plan.WantsApiInventory || access.CanViewApis'),
  'readiness, question API inventory, and durable conversation/tool evidence require their dedicated permissions');''',
    "permission and persistence validator",
)

documentation = documentation_path.read_text()
if "## Trusted same-origin tool destination" not in documentation:
    documentation += '''

## Trusted same-origin tool destination

Credential-bearing System Intelligence tool requests do not trust the browser or proxy `Host` header. The API requires `PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_BASE_URI` to identify the trusted internal application origin and requires that exact host or host-and-port in `PROJECTPULSE_PULSE_AI_SYSTEM_TOOL_HOST_ALLOWLIST`. HTTPS is required except for an explicitly configured loopback development URI. Missing, malformed, or non-allowlisted configuration fails closed before Authorization, Cookie, session, or View-As headers are attached. The dedicated HTTP client does not follow redirects and does not retain a cookie container.

API inventory evidence requires `VIEW_PULSE_AI_API_INVENTORY`, including the readiness summary and question-driven catalog lookup. Durable conversations, messages, inquiry runs, and tool events require `VIEW_PULSE_AI_CONVERSATION_HISTORY` and are disabled whenever the actual and effective user differ during View-As.
'''
    documentation_path.write_text(documentation)

documentation_mirror_path.parent.mkdir(parents=True, exist_ok=True)
documentation_mirror_path.write_text(documentation_path.read_text())
print("PR323_SECURITY_REVIEW_PATCH=APPLIED")
