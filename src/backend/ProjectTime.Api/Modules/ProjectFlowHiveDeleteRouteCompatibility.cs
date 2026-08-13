using System.Text;
using System.Text.Json;

namespace ProjectTime.Api.Modules;

/// <summary>
/// Preserves the Module 066 DELETE contract while avoiding ASP.NET minimal-API
/// body inference on MapDelete. The framework disables inferred body parameters
/// for DELETE handlers, so this exact overload reads the optional revocation
/// payload explicitly and then invokes the existing governed handler.
/// </summary>
internal static class ProjectFlowHiveDeleteRouteCompatibility
{
    private const int MaximumRequestCharacters = 4_096;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    internal static RouteHandlerBuilder MapDelete(
        this WebApplication app,
        string pattern,
        Func<Guid, Guid, ProjectFlowHiveCustomerShareRevokeRequest, HttpContext, CancellationToken, Task<IResult>> handler)
    {
        return app.MapMethods(
            pattern,
            [HttpMethods.Delete],
            (Func<Guid, Guid, HttpContext, CancellationToken, Task<IResult>>)(async (
                Guid projectId,
                Guid shareId,
                HttpContext context,
                CancellationToken cancellationToken) =>
            {
                var parsed = await ReadRequestAsync(context, cancellationToken);
                if (parsed.Error is not null) return parsed.Error;

                return await handler(
                    projectId,
                    shareId,
                    parsed.Request!,
                    context,
                    cancellationToken);
            }));
    }

    private static async Task<RevokeRequestReadResult> ReadRequestAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is > MaximumRequestCharacters)
        {
            return RevokeRequestReadResult.Fail(Results.BadRequest(new
            {
                status = "validation_failed",
                message = "The customer-share revocation request is too large.",
                stateChanged = false
            }));
        }

        string body;
        using (var reader = new StreamReader(
                   context.Request.Body,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   bufferSize: 1_024,
                   leaveOpen: true))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(body))
            return RevokeRequestReadResult.Success(new ProjectFlowHiveCustomerShareRevokeRequest(null));
        if (body.Length > MaximumRequestCharacters)
        {
            return RevokeRequestReadResult.Fail(Results.BadRequest(new
            {
                status = "validation_failed",
                message = "The customer-share revocation request is too large.",
                stateChanged = false
            }));
        }

        try
        {
            return RevokeRequestReadResult.Success(
                JsonSerializer.Deserialize<ProjectFlowHiveCustomerShareRevokeRequest>(body, Json)
                ?? new ProjectFlowHiveCustomerShareRevokeRequest(null));
        }
        catch (JsonException)
        {
            return RevokeRequestReadResult.Fail(Results.BadRequest(new
            {
                status = "validation_failed",
                message = "The customer-share revocation request is invalid.",
                stateChanged = false
            }));
        }
    }

    private sealed record RevokeRequestReadResult(
        ProjectFlowHiveCustomerShareRevokeRequest? Request,
        IResult? Error)
    {
        internal static RevokeRequestReadResult Success(ProjectFlowHiveCustomerShareRevokeRequest request) =>
            new(request, null);

        internal static RevokeRequestReadResult Fail(IResult error) =>
            new(null, error);
    }
}
