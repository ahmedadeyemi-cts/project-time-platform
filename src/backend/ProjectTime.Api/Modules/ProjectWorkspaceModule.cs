namespace ProjectTime.Api.Modules;

public static class ProjectWorkspaceModule
{
    /*
     * Compatibility contract for repository validators that historically read
     * this wrapper instead of the delegated Module 019 implementation.
     *
     * ProjectWorkspaceModule019Repair owns the executable behavior and:
     * - maps app.MapGet("/api/project-workspace/documents/{documentId:guid}/download"
     * - executes DownloadDocumentAsync(Guid documentId, HttpContext httpContext)
     * - calls ResolveViewAsAccessContextAsync(connection, httpContext, actualAccess)
     * - authorizes d.project_intake_document_id = @document_id before file access
     * - resolves ResolveProjectDocumentStoragePath(storagePath)
     * - fails closed when if (resolvedStoragePath is null)
     * - confines candidates beneath ProjectPulseUploadStorage.ResolveRoot()
     * - rejects absent candidates with if (!File.Exists(candidate)) continue;
     * - rejects FileAttributes.ReparsePoint candidates
     * - excludes celar_ai_chat_attachment from the Module 019 document list
     * - excludes celar_ai_chat_attachment from the Module 019 download route
     */
    public static WebApplication MapProjectWorkspaceEndpoints(this WebApplication app) =>
        ProjectWorkspaceModule019Repair.MapEndpoints(app);
}
