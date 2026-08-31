using System.Security.Cryptography;
using System.Text;

namespace ProjectTime.Api.Modules;

internal sealed class CelarAiTransientFailureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CelarAiTransientFailureMiddleware> _logger;

    public CelarAiTransientFailureMiddleware(
        RequestDelegate next,
        ILogger<CelarAiTransientFailureMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.Equals("/api/celar-ai/v2/chat", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await _next(context);
            if (context.Response.StatusCode is StatusCodes.Status502BadGateway
                or StatusCodes.Status503ServiceUnavailable
                or StatusCodes.Status504GatewayTimeout)
            {
                await WriteEvidenceLimitedAsync(
                    context,
                    originalBody,
                    $"explicit_http_{context.Response.StatusCode}",
                    null);
                return;
            }

            buffer.Position = 0;
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Response.Body = originalBody;
            throw;
        }
        catch (Exception exception)
        {
            await WriteEvidenceLimitedAsync(
                context,
                originalBody,
                exception.GetType().FullName ?? "celar_ai_transient_failure",
                exception);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private async Task WriteEvidenceLimitedAsync(
        HttpContext context,
        Stream originalBody,
        string failureClass,
        Exception? exception)
    {
        var correlationId = context.TraceIdentifier;
        var diagnostic = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(failureClass)))[..12];
        if (exception is null)
        {
            _logger.LogWarning(
                "Celar AI returned governed evidence-limited output after a transient downstream status. CorrelationId={CorrelationId} Diagnostic={Diagnostic}",
                correlationId,
                diagnostic);
        }
        else
        {
            _logger.LogError(
                exception,
                "Celar AI returned governed evidence-limited output after a transient orchestration failure. CorrelationId={CorrelationId} Diagnostic={Diagnostic}",
                correlationId,
                diagnostic);
        }

        context.Response.Body = originalBody;
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = null;
        context.Response.Headers.Remove("Content-Encoding");
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-ProjectPulse-Correlation-Id"] = correlationId;
        await context.Response.WriteAsJsonAsync(new
        {
            module = "011",
            brand = "Celar AI",
            feature = "help_assistant",
            orchestrationContract = "celar_ai_evidence_limited_transient_fallback",
            status = "completed_with_limitations",
            trust = new { classification = "evidence_limited", confidence = 0m, verified = false },
            result = new
            {
                status = "partial",
                correlationId,
                answer = new
                {
                    directConclusion = "Celar AI could not verify the required evidence because a supporting service was temporarily unavailable.",
                    executiveSummary = "No unsupported answer was generated. Retry the request; use the correlation ID with operational evidence if the condition continues.",
                    limitations = new[]
                    {
                        "The request did not complete its governed evidence and provider checks.",
                        "No private document, project record, identity, tool result, or unsupported model statement is being presented as verified."
                    },
                    recommendedActions = new[]
                    {
                        "Retry the request after the supporting service recovers.",
                        "Review Module 013, Module 016, or Module 998 using the returned correlation ID if the failure repeats."
                    },
                    citationIds = Array.Empty<int>(),
                    confidence = 0m,
                    confidenceExplanation = "The evidence path did not complete."
                }
            },
            diagnosticCode = $"CELAR_TRANSIENT_{diagnostic}",
            correlationId,
            stateChanged = false
        }, context.RequestAborted);
    }
}
