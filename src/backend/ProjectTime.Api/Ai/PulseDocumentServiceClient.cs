using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ProjectTime.Api.Ai;

/// <summary>Bounded document adapter with no redirects, retries, or scanner fallback.</summary>
public sealed class PulseDocumentServiceClient : IDisposable
{
    public const long MaximumUploadBytes = 32L * 1024 * 1024;
    private readonly PulseDocumentServiceOptions _options;
    private readonly HttpClient _client;
    public PulseDocumentServiceClient(PulseDocumentServiceOptions options) : this(options, CreateHandler(options)) { }
    // Dependency-injected handlers are used only by internal callers and offline tests.
    public PulseDocumentServiceClient(PulseDocumentServiceOptions options, HttpMessageHandler handler)
    {
        _options = options;
        _client = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
    }
    public PulseDocumentServiceClient(PulseDocumentServiceOptions options, HttpClient client)
    { _options = options; _client = client; }
    public void Dispose() => _client.Dispose();
    public static SocketsHttpHandler CreateHandler(PulseDocumentServiceOptions options) => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        AutomaticDecompression = DecompressionMethods.None,
        MaxResponseHeadersLength = 16, MaxConnectionsPerServer = 2,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        // Check DNS at connection time; TLS still verifies the original hostname.
        ConnectCallback = async (context, token) =>
        {
            if (!options.Valid || context.DnsEndPoint.Host != options.ExpectedHost || context.DnsEndPoint.Port != 443)
                throw new PulseDocumentServiceException("document_service_connection_rejected");
            var addresses = await Dns.GetHostAddressesAsync(options.ExpectedHost, token);
            if (!PulseDocumentServiceOptions.AddressesApproved(addresses))
                throw new PulseDocumentServiceException("document_service_dns_rejected");
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, 443), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (SocketException) { socket.Dispose(); }
                catch { socket.Dispose(); throw; }
            }
            throw new PulseDocumentServiceException("document_service_connection_failed");
        }
    };
    public async Task<PulseAiPrivateMalwareScanResult> ScanAsync(string path, int seconds, CancellationToken token)
    {
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            stop.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 120)));
            var received = await UploadAsync(path, "/v1/scan", null, stop.Token);
            using var json = received.Json;
            return ValidateScan(json.RootElement, received.Hash, received.Bytes, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (Handled(error))
        {
            return new("scan_failed", false, false, PulseDocumentServiceOptions.Provider,
                "", Code(error), "", "", DateTimeOffset.UtcNow);
        }
    }
    public async Task<PulseAiPrivateOcrResult> ExtractAsync(PulseAiAuthorizedDocumentSource source,
        PulseAiDocumentPipelineOptions pipeline, CancellationToken token)
    {
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            stop.CancelAfter(TimeSpan.FromSeconds(240));
            var received = await UploadAsync(source.StoragePath, "/v1/extract", source, stop.Token);
            using var json = received.Json;
            var root = json.RootElement;
            Require(Text(root, "sha256") == received.Hash, "document_service_content_mismatch");
            Require(Text(root, "documentId") == source.DocumentId.ToString("D"), "document_service_identity_mismatch");
            Require(Text(root, "model") == PulseDocumentServiceOptions.OcrModel, "document_service_model_mismatch");
            Require(root.TryGetProperty("scan", out var scan), "document_service_scan_missing");
            var receipt = ValidateScan(scan, received.Hash, received.Bytes, DateTimeOffset.UtcNow);
            Require(receipt.Clean && !receipt.Infected, "document_service_not_clean");
            var sections = ValidatePages(root, pipeline);
            return new("private_ocr_completed", PulseDocumentServiceOptions.Provider, PulseDocumentServiceOptions.OcrModel,
                sections, root.GetProperty("pages").GetArrayLength(), sections.Sum(x => x.CharacterCount),
                [], "", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (Handled(error))
        {
            return new("private_ocr_failed", PulseDocumentServiceOptions.Provider, PulseDocumentServiceOptions.OcrModel,
                [], 0, 0, [], Code(error), DateTimeOffset.UtcNow);
        }
    }
    public async Task<(bool Ready, string Diagnostic)> ProbeAsync(CancellationToken token)
    {
        try
        {
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            stop.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, _options.Endpoint("/health"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
            request.Headers.Add("X-Pulse-AI-Privacy-Boundary", PulseAiPrivateRuntimePolicy.PrivacyBoundary);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop.Token);
            Require(response.IsSuccessStatusCode, "document_service_unavailable");
            using var json = await ReadJsonAsync(response, 64 * 1024, stop.Token);
            var root = json.RootElement;
            Require(Text(root, "status") == "ready" && Text(root, "scanner") == "clamav"
                && Text(root, "ocrModel") == PulseDocumentServiceOptions.OcrModel, "document_service_readiness_invalid");
            Require(root.TryGetProperty("modelProviderRequired", out var required)
                && required.ValueKind == JsonValueKind.False, "document_service_model_dependency_rejected");
            Require(root.TryGetProperty("engine", out var engine) && engine.ValueKind == JsonValueKind.Object,
                "document_service_engine_missing");
            _ = ValidateEngine(engine, DateTimeOffset.UtcNow);
            return (true, "document_service_ready");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (Handled(error)) { return (false, Code(error)); }
    }
    private async Task<(JsonDocument Json, string Hash, long Bytes)> UploadAsync(string path, string route,
        PulseAiAuthorizedDocumentSource? source, CancellationToken token)
    {
        var endpoint = _options.Endpoint(route);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = stream.Length;
        Require(bytes is > 0 and <= MaximumUploadBytes, "document_service_input_size_rejected");
        if (source is not null) Require(source.SizeBytes == bytes, "document_service_source_size_changed");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var hashBuffer = new byte[128 * 1024]; long hashed = 0; int hashRead;
        while ((hashRead = await stream.ReadAsync(hashBuffer, token)) != 0)
        {
            hashed += hashRead;
            Require(hashed <= bytes && hashed <= MaximumUploadBytes, "document_service_source_size_changed");
            digest.AppendData(hashBuffer, 0, hashRead);
        }
        Require(hashed == bytes, "document_service_source_size_changed");
        var hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
        Require(stream.Position == bytes && stream.Length == bytes, "document_service_source_size_changed");
        stream.Position = 0;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
        request.Headers.Add("X-Pulse-AI-Privacy-Boundary", PulseAiPrivateRuntimePolicy.PrivacyBoundary);
        request.Headers.Add("X-Pulse-Content-SHA256", hash);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "document.bin");
        if (source is not null)
        {
            Require(source.DocumentCategory.Length <= 128, "document_service_category_invalid");
            form.Add(new StringContent(PulseDocumentServiceOptions.OcrModel), "model");
            form.Add(new StringContent(source.DocumentId.ToString("D")), "documentId");
            form.Add(new StringContent(source.DocumentCategory), "documentCategory");
        }
        request.Content = form;
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
            throw new PulseDocumentServiceException(response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => "document_service_busy",
                HttpStatusCode.ServiceUnavailable => "document_service_unavailable",
                HttpStatusCode.GatewayTimeout or HttpStatusCode.RequestTimeout => "document_service_deadline_exceeded",
                HttpStatusCode.Conflict => "document_service_content_mismatch",
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "document_service_authentication_rejected",
                _ => "document_service_request_rejected"
            });
        return (await ReadJsonAsync(response, route == "/v1/scan" ? 64 * 1024 : 6_100_000, token), hash, bytes);
    }
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        Require(response.Content.Headers.ContentType?.MediaType == "application/json", "document_service_response_type_invalid");
        Require(response.Content.Headers.ContentEncoding.Count == 0, "document_service_response_encoding_rejected");
        Require(response.Content.Headers.ContentLength is not long length || length <= limit, "document_service_response_too_large");
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream();
        var block = new byte[16384];
        int read;
        while ((read = await body.ReadAsync(block, token)) != 0)
        {
            Require(buffer.Length + read <= limit, "document_service_response_too_large");
            buffer.Write(block, 0, read);
        }
        var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 16 });
        try { NoDuplicateKeys(json.RootElement); }
        catch { json.Dispose(); throw; }
        return json;
    }
    public static PulseAiPrivateMalwareScanResult ValidateScan(JsonElement root, string hash, long bytes, DateTimeOffset now)
    {
        Require(root.ValueKind == JsonValueKind.Object, "document_service_scan_invalid");
        Require(Text(root, "scanner") == "clamav" && Text(root, "sha256") == hash, "document_service_scan_identity_invalid");
        Require(root.TryGetProperty("sizeBytes", out var size) && size.TryGetInt64(out var count) && count == bytes,
            "document_service_scan_size_mismatch");
        Require(root.TryGetProperty("clean", out var clean) && clean.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "document_service_verdict_invalid");
        Require(root.TryGetProperty("infected", out var infected) && infected.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "document_service_verdict_invalid");
        var isClean = clean.GetBoolean(); var isInfected = infected.GetBoolean();
        var status = Text(root, "status");
        Require(isClean != isInfected && status == (isClean ? "clean" : "infected"), "document_service_verdict_invalid");
        Require(root.TryGetProperty("engine", out var engine) && engine.ValueKind == JsonValueKind.Object,
            "document_service_engine_missing");
        var (version, signatures, updated) = ValidateEngine(engine, now);
        var evidence = Hash($"pulse-documents-v1|{hash}|{bytes}|{status}|{version}|{signatures}|{updated:O}|{now:O}");
        return new(status, isClean, isInfected, PulseDocumentServiceOptions.Provider, signatures,
            isInfected ? "malware_detected" : "", hash, evidence, now);
    }
    private static (string Version, string Signatures, DateTimeOffset Updated) ValidateEngine(JsonElement engine, DateTimeOffset now)
    {
        var version = Text(engine, "version"); var signatures = Text(engine, "signatures");
        Require(Regex.IsMatch(version, @"^[0-9][A-Za-z0-9.+~-]{0,63}\z")
            && Regex.IsMatch(signatures, @"^daily-[0-9]{1,10}\z"), "document_service_engine_invalid");
        Require(DateTimeOffset.TryParse(Text(engine, "updated_at"), System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var updated) && now - updated <= TimeSpan.FromHours(48)
            && updated - now <= TimeSpan.FromMinutes(5), "document_service_signatures_stale_or_unknown");
        return (version, signatures, updated);
    }
    public static IReadOnlyList<PulseAiExtractedSection> ValidatePages(JsonElement root, PulseAiDocumentPipelineOptions options)
    {
        Require(root.TryGetProperty("pages", out var pages) && pages.ValueKind == JsonValueKind.Array,
            "document_service_pages_invalid");
        Require(pages.GetArrayLength() >= 1 && pages.GetArrayLength() <= Math.Min(50, options.MaximumPages)
            && pages.GetArrayLength() <= options.MaximumSections, "document_service_page_limit");
        var sections = new List<PulseAiExtractedSection>(); var count = 0; var pageNumber = 0;
        foreach (var page in pages.EnumerateArray())
        {
            ++pageNumber;
            Require(page.ValueKind == JsonValueKind.Object && page.TryGetProperty("pageNumber", out var number)
                && number.TryGetInt32(out var n) && n == pageNumber, "document_service_page_identity_invalid");
            Require(page.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String,
                "document_service_text_invalid");
            var value = text.GetString()!;
            count += value.Length;
            Require(count <= Math.Min(1_000_000, options.MaximumCharacters), "document_service_text_limit");
            if (string.IsNullOrWhiteSpace(value)) continue;
            sections.Add(new(sections.Count, $"page:{pageNumber}", $"OCR page {pageNumber}", value,
                pageNumber, null, value.Length, Hash(value)));
        }
        Require(sections.Count > 0, "document_service_text_empty");
        return sections;
    }
    private static void NoDuplicateKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                Require(keys.Add(property.Name), "document_service_duplicate_response_field");
                NoDuplicateKeys(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) NoDuplicateKeys(item);
    }
    private static string Text(JsonElement root, string key) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void Require(bool value, string code)
    { if (!value) throw new PulseDocumentServiceException(code); }
    private static bool Handled(Exception e) => e is PulseDocumentServiceException or HttpRequestException
        or IOException or UnauthorizedAccessException or JsonException or OperationCanceledException
        or InvalidOperationException or FormatException;
    private static string Code(Exception e) => e switch
    {
        PulseDocumentServiceException boundary => boundary.Code,
        OperationCanceledException => "document_service_deadline_exceeded",
        JsonException or InvalidOperationException or FormatException => "document_service_response_invalid",
        HttpRequestException => "document_service_transport_failed",
        _ => "document_service_source_unavailable"
    };
}
