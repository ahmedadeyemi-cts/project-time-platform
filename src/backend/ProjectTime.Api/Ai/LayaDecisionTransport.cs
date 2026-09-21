using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace ProjectTime.Api.Ai;

public sealed class LayaDecisionFailure(string code, int status = 503) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

/// <summary>Two fixed Test-only paths on the existing approved Celar host.
/// Reuses the existing runtime approval, bearer credential, DNS validation and
/// address-pin policy. No configurable URL, proxy, redirect, retry or cloud fallback.
/// </summary>
public static class LayaDecisionTransport
{
    private static readonly string[] Paths = ["/v1/decisions/health", "/v1/decisions/document-type"];

    public static bool DeploymentAllowed()
    {
        var release = ProjectPulseAiReleaseRuntimePolicy.RequireValid();
        return !release.IsCandidate
            && string.Equals(Environment.GetEnvironmentVariable("PROJECTPULSE_ENVIRONMENT"), "test", StringComparison.OrdinalIgnoreCase)
            && PulseAiExternalHttpsRuntimePolicy.Evaluate().Active;
    }

    public static async Task<JsonObject> SendAsync(string? text, CancellationToken cancellationToken)
    {
        if (!DeploymentAllowed()) throw new LayaDecisionFailure("decision_deployment_not_allowed", 423);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(text is null ? 6 : 15));
        var token = deadline.Token;
        var snapshot = PulseAiExternalHttpsRuntimePolicy.Evaluate();
        if (snapshot.ReadinessEndpoint is null) throw new LayaDecisionFailure("decision_runtime_not_configured");
        var endpoint = new Uri(snapshot.ReadinessEndpoint, Paths[text is null ? 0 : 1]);
        bool Allowed(Uri? uri) => uri is not null && uri.Scheme == "https" && uri.IsDefaultPort
            && uri.Host == PulseAiExternalHttpsRuntimePolicy.ApprovedHost
            && Paths.Contains(uri.AbsolutePath, StringComparer.Ordinal)
            && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0;
        if (!Allowed(endpoint)) throw new LayaDecisionFailure("decision_endpoint_rejected");
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(4),
            ConnectCallback = async (context, ct) =>
            {
                if (!DeploymentAllowed() || !Allowed(context.InitialRequestMessage.RequestUri)
                    || context.DnsEndPoint.Host != PulseAiExternalHttpsRuntimePolicy.ApprovedHost
                    || context.DnsEndPoint.Port != 443)
                    throw new HttpRequestException("decision_endpoint_rejected");
                // Validate the SAME approved host through its existing policy. This
                // new capability explicitly authorizes only the two paths above.
                var addresses = await PulseAiExternalHttpsRuntimePolicy.ResolveConnectAddressesAsync(snapshot.ReadinessEndpoint, ct);
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(address, 443, ct);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (SocketException) { socket.Dispose(); }
                    catch { socket.Dispose(); throw; }
                }
                throw new HttpRequestException("decision_connect_failed");
            }
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(text is null ? HttpMethod.Get : HttpMethod.Post, endpoint);
        if (text is not null) request.Content = JsonContent.Create(new { text });
        var bearer = Environment.GetEnvironmentVariable("PROJECTPULSE_PRIVATE_INFERENCE_BEARER_TOKEN")?.Trim();
        if (string.IsNullOrEmpty(bearer) || bearer.Length < 32)
            throw new LayaDecisionFailure("decision_runtime_not_configured");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        request.Headers.Add("X-Pulse-AI-Privacy-Boundary", PulseAiPrivateRuntimePolicy.PrivacyBoundary);
        request.Headers.Add("X-Pulse-AI-Feature", "module064_document_classification");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.Content.Headers.ContentLength is > 16384)
                throw new LayaDecisionFailure("decision_invalid_response", 502);
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var bytes = new byte[2048];
            int count;
            while ((count = await stream.ReadAsync(bytes, token)) > 0)
            {
                if (buffer.Length + count > 16384) throw new LayaDecisionFailure("decision_invalid_response", 502);
                buffer.Write(bytes, 0, count);
            }
            var body = JsonNode.Parse(buffer.ToArray()) as JsonObject
                ?? throw new LayaDecisionFailure("decision_invalid_response", 502);
            if (!response.IsSuccessStatusCode)
            {
                var code = body["error"]?["code"]?.GetValue<string>();
                if (code == "decision_input_exceeds_model_budget") throw new LayaDecisionFailure(code, 422);
                if (code == "decision_busy") throw new LayaDecisionFailure(code, 503);
                if (code == "decision_timeout") throw new LayaDecisionFailure(code, 504);
                throw new LayaDecisionFailure("decision_unavailable", 503);
            }
            if (body["ok"]?.GetValue<bool>() != true
                || body["model_revision"]?.GetValue<string>() != LayaDecisionContract.Revision)
                throw new LayaDecisionFailure("decision_invalid_response", 502);
            return body;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new LayaDecisionFailure("decision_timeout", 504); }
        catch (HttpRequestException) { throw new LayaDecisionFailure("decision_unavailable", 503); }
        catch (System.Text.Json.JsonException) { throw new LayaDecisionFailure("decision_invalid_response", 502); }
    }
}
