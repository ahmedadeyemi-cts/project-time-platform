using Npgsql;

namespace ProjectTime.Api.Modules;

public static partial class Module005ProjectExpenseUploadModule
{
    private static async Task<IResult> RetryAuthorizedNotificationAsync(Guid uploadId, HttpContext context)
    {
        await using var connection = await OpenConnectionAsync();
        var actor = await LoadActorAsync(connection, context);
        if (actor is null) return SessionRequired();

        var upload = await LoadUploadAsync(connection, uploadId);
        if (upload is null)
            return Results.NotFound(new
            {
                status = "upload_not_found",
                message = "The project expense upload was not found."
            });

        var authorization = await AuthorizeExistingUploadActionAsync(connection, null, actor, upload);
        if (authorization is not null) return authorization;

        var result = await DeliverExpenseNotificationAsync(connection, uploadId, actor.ActualUserId);
        return Results.Ok(new
        {
            status = "project_expense_notification_processed",
            uploadId,
            notification = result
        });
    }
}
