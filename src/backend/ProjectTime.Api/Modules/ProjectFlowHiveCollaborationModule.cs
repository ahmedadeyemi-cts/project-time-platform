using Npgsql;
using System.Text;

namespace ProjectTime.Api.Modules;

internal static partial class ProjectFlowHiveEnterpriseModule
{
    private static void MapCollaborationEndpoints(WebApplication app)
    {
        app.MapGet("/api/project-flowhive/projects/{projectId:guid}/collaboration",(Func<Guid,HttpContext,CancellationToken,Task<IResult>>)GetCollaborationAsync);
        app.MapPost("/api/project-flowhive/projects/{projectId:guid}/contacts",(Func<Guid,FlowHiveContactRequest,HttpContext,CancellationToken,Task<IResult>>)SaveProjectContactAsync);
        app.MapPost("/api/project-flowhive/projects/{projectId:guid}/meeting-drafts",(Func<Guid,FlowHiveMeetingDraftRequest,HttpContext,CancellationToken,Task<IResult>>)CreateMeetingDraftAsync);
        app.MapGet("/api/project-flowhive/projects/{projectId:guid}/meeting-drafts/{meetingId:guid}/calendar",(Func<Guid,Guid,HttpContext,CancellationToken,Task<IResult>>)ExportMeetingDraftAsync);
    }
    private static IResult CollaborationMigrationRequired()=>Results.Json(new {status="collaboration_migration_required",message="Project contacts and meeting drafts require migration 131. Project team and existing WBS features remain available.",requiredMigration=ProjectFlowHiveCollaborationStore.Migration,stateChanged=false},statusCode:503);
    private static IResult CollaborationInput(ProjectFlowHiveCollaborationStore.InputException error)=>Results.Json(new {
        status="invalid_collaboration_field",message=error.Message,issues=new[]{new{path=error.Field,message=error.Message,severity="error"}},stateChanged=false},statusCode:error.Status);
    private static async Task<IResult> GetCollaborationAsync(Guid projectId,HttpContext context,CancellationToken token)
    {
        var opened=await OpenAuthorizedAsync(projectId,context,FlowHiveAccessRequirement.View,token);
        if(opened.Error is not null)return opened.Error;await using var c=opened.Connection!;
        var ready=await ProjectFlowHiveCollaborationStore.ReadyAsync(c,token);
        var team=await ProjectFlowHiveCollaborationStore.TeamAsync(c,projectId,token);
        var contacts=ready?await ProjectFlowHiveCollaborationStore.ContactsAsync(c,projectId,token):[];
        var meetings=ready?await ProjectFlowHiveCollaborationStore.MeetingsAsync(c,projectId,token):[];
        return Results.Ok(new {projectId,ready,team,contacts,meetingDrafts=meetings,maxActiveContacts=15,
            canManage=ready && opened.Access!.CanAdministerPlanner && !opened.Access.IsViewAs,
            requiredMigration=ready?null:ProjectFlowHiveCollaborationStore.Migration,
            liveInvitationsAvailable=false,externalContactCreatesAccount=false,stateChanged=false});
    }
    private static async Task<IResult> SaveProjectContactAsync(Guid projectId,FlowHiveContactRequest request,HttpContext context,CancellationToken token)
    {
        var opened=await OpenAuthorizedAsync(projectId,context,FlowHiveAccessRequirement.AdministerPlanner,token);
        if(opened.Error is not null)return opened.Error;await using var c=opened.Connection!;
        if(!await ProjectFlowHiveCollaborationStore.ReadyAsync(c,token))return CollaborationMigrationRequired();
        await using var tx=await c.BeginTransactionAsync(token);
        if(!await ProjectFlowHiveLifecycle.LockActiveAsync(c,tx,projectId,token))return ProjectFlowHiveLifecycle.Archived();
        try
        {
            var saved=await ProjectFlowHiveCollaborationStore.SaveContactAsync(c,tx,projectId,opened.Access!.ActualUserId,request,token);
            await InsertAuditAsync(c,tx,projectId,null,null,request.ProjectContactId.HasValue?"project_contact_updated":"project_contact_created",opened.Access,
                new {projectContactId=saved.Id,request.IsActive,accountCreated=false},context.TraceIdentifier,token);
            await tx.CommitAsync(token);
            return Results.Ok(new {projectId,projectContactId=saved.Id,rowVersion=saved.RowVersion,stateChanged=true,accountCreated=false,invitationSent=false});
        }
        catch(ProjectFlowHiveCollaborationStore.InputException error){return CollaborationInput(error);}
    }
    private static async Task<IResult> CreateMeetingDraftAsync(Guid projectId,FlowHiveMeetingDraftRequest request,HttpContext context,CancellationToken token)
    {
        var opened=await OpenAuthorizedAsync(projectId,context,FlowHiveAccessRequirement.AdministerPlanner,token);
        if(opened.Error is not null)return opened.Error;await using var c=opened.Connection!;
        if(!await ProjectFlowHiveCollaborationStore.ReadyAsync(c,token))return CollaborationMigrationRequired();
        await using var tx=await c.BeginTransactionAsync(token);
        if(!await ProjectFlowHiveLifecycle.LockActiveAsync(c,tx,projectId,token))return ProjectFlowHiveLifecycle.Archived();
        try
        {
            var id=await ProjectFlowHiveCollaborationStore.CreateMeetingAsync(c,tx,projectId,opened.Access!.ActualUserId,request,token);
            await InsertAuditAsync(c,tx,projectId,null,null,"meeting_draft_created",opened.Access,new {meetingDraftId=id,invitationSent=false},context.TraceIdentifier,token);
            await tx.CommitAsync(token);
            return Results.Ok(new {projectId,meetingDraftId=id,status="draft",invitationSent=false,message="Meeting draft saved. No calendar event or invitation has been sent. Review the calendar draft in your calendar application before sending.",stateChanged=true});
        }
        catch(ProjectFlowHiveCollaborationStore.InputException error){return CollaborationInput(error);}
    }
    private static async Task<IResult> ExportMeetingDraftAsync(Guid projectId,Guid meetingId,HttpContext context,CancellationToken token)
    {
        var opened=await OpenAuthorizedAsync(projectId,context,FlowHiveAccessRequirement.AdministerPlanner,token);
        if(opened.Error is not null)return opened.Error;await using var c=opened.Connection!;
        if(!await ProjectFlowHiveCollaborationStore.ReadyAsync(c,token))return CollaborationMigrationRequired();
        var meetings=await ProjectFlowHiveCollaborationStore.MeetingsAsync(c,projectId,token);
        var meeting=meetings.SingleOrDefault(m=>m.MeetingDraftId==meetingId);
        if(meeting is null)return Results.NotFound(new {message="This meeting draft is not available in the selected project."});
        var people=await ProjectFlowHiveCollaborationStore.AttendeesAsync(c,projectId,token);
        if(meeting.AttendeeReferences.Any(key=>!people.ContainsKey(key)))return Results.Conflict(new {message="The attendee list has changed. Create a new draft with current contacts before exporting."});
        context.Response.Headers.CacheControl="no-store";
        return Results.File(Encoding.UTF8.GetBytes(ProjectFlowHiveCollaborationStore.CalendarDraft(meeting,meeting.AttendeeReferences.Select(key=>people[key]))),"text/calendar; charset=utf-8","project-meeting-draft.ics");
    }
}
