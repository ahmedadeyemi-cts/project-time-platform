namespace ProjectTime.Api.Modules;

public static class ProjectWorkspaceModule
{
    public static WebApplication MapProjectWorkspaceEndpoints(this WebApplication app) =>
        ProjectWorkspaceModule019Repair.MapEndpoints(app);
}
