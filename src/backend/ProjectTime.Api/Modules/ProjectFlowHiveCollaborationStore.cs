using System.Net.Mail;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace ProjectTime.Api.Modules;

internal static class ProjectFlowHiveCollaborationStore
{
    internal const string Migration="131_flowhive_project_collaboration";
    internal sealed record Contact(Guid ProjectContactId,string DisplayName,string Email,string Phone,string Title,string Organization,string ContactKind,bool IsActive,Guid RowVersion);
    internal sealed record TeamMember(Guid UserId,string DisplayName,string Email,string Phone,string Title,string Role);
    internal sealed record Meeting(Guid MeetingDraftId,string Title,string Agenda,string Location,DateTimeOffset StartsAt,DateTimeOffset EndsAt,string TimezoneName,string[] AttendeeReferences,string Status);
    internal sealed class InputException(string fieldName,string message,int status=400):Exception(message) { internal string Field=>fieldName;internal int Status=>status; }
    internal static async Task<bool> ReadyAsync(NpgsqlConnection c,CancellationToken token)
    {
        await using var q=new NpgsqlCommand("SELECT to_regclass('project_flowhive_contacts') IS NOT NULL AND to_regclass('project_flowhive_meeting_drafts') IS NOT NULL;",c);
        return await q.ExecuteScalarAsync(token) is true;
    }
    internal static async Task<List<TeamMember>> TeamAsync(NpgsqlConnection c,Guid project,CancellationToken token)
    {
        var rows=new List<TeamMember>();
        await using var q=new NpgsqlCommand("""
            WITH members AS (
             SELECT project_manager_user_id AS user_id,'Project Manager' AS role FROM projects WHERE project_id=@project
             UNION ALL SELECT account_executive_user_id,'Account Executive' FROM projects WHERE project_id=@project
             UNION ALL SELECT solution_architect_user_id,'Solution Architect' FROM projects WHERE project_id=@project
             UNION ALL SELECT user_id,'Project team' FROM project_assignments WHERE project_id=@project
               AND (effective_start_date IS NULL OR effective_start_date<=CURRENT_DATE) AND (effective_end_date IS NULL OR effective_end_date>=CURRENT_DATE)
             UNION ALL SELECT user_id,'Planning collaborator' FROM project_planning_collaborators WHERE project_id=@project AND module_code='066'
               AND is_active AND effective_start_date<=CURRENT_DATE AND (effective_end_date IS NULL OR effective_end_date>=CURRENT_DATE)
            )
            SELECT u.user_id,COALESCE(NULLIF(u.display_name,''),u.email,'Team member'),COALESCE(u.email,''),
              COALESCE(to_jsonb(u)->>'business_phone',to_jsonb(u)->>'phone',''),COALESCE(to_jsonb(u)->>'job_title',''),
              string_agg(DISTINCT m.role,', ' ORDER BY m.role)
            FROM members m JOIN app_users u ON u.user_id=m.user_id WHERE u.is_active
            GROUP BY u.user_id,u.display_name,u.email ORDER BY 2;
            """,c);
        q.Parameters.AddWithValue("project",project);await using var reader=await q.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token)) rows.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4),reader.GetString(5)));
        return rows;
    }
    internal static async Task<List<Contact>> ContactsAsync(NpgsqlConnection c,Guid project,CancellationToken token)
    {
        var rows=new List<Contact>();await using var q=new NpgsqlCommand("SELECT project_contact_id,display_name,email,phone,title,organization,contact_kind,is_active,row_version FROM project_flowhive_contacts WHERE project_id=@project ORDER BY is_active DESC,display_name;",c);
        q.Parameters.AddWithValue("project",project);await using var rd=await q.ExecuteReaderAsync(token);
        while(await rd.ReadAsync(token)) rows.Add(new(rd.GetGuid(0),rd.GetString(1),rd.GetString(2),rd.GetString(3),rd.GetString(4),rd.GetString(5),rd.GetString(6),rd.GetBoolean(7),rd.GetGuid(8)));
        return rows;
    }
    internal static string Text(string? raw,string field,int maximum,bool required=false)
    {
        var value=raw?.Trim()??"";
        if(value.Length>maximum || value.Any(char.IsControl) || (required && value.Length<2)) throw new InputException(field,$"Enter {(required ? "a value of 2 to" : "no more than")} {maximum} characters without control characters.");
        return value;
    }
    internal static string Email(string? raw)
    {
        var value=Text(raw,"email",320,true);
        if(!MailAddress.TryCreate(value,out var mail) || mail.Address!=value) throw new InputException("email","Enter one valid email address, without a display-name prefix.");
        return value.ToLowerInvariant();
    }
    internal static async Task<(Guid Id,Guid RowVersion)> SaveContactAsync(NpgsqlConnection c,NpgsqlTransaction transaction,Guid project,Guid actor,FlowHiveContactRequest input,CancellationToken token)
    {
        var name=Text(input.DisplayName,"displayName",200,true);var email=Email(input.Email);
        var phone=Text(input.Phone,"phone",80);var title=Text(input.Title,"title",160);var organization=Text(input.Organization,"organization",200);
        var kind=input.ContactKind??"customer";if(kind is not ("customer" or "vendor" or "partner")) throw new InputException("contactKind","Choose Customer, Vendor or Partner.");
        var id=input.ProjectContactId??Guid.NewGuid();
        if(input.ProjectContactId.HasValue && !input.IsActive)
        {
            await using var assigned=new NpgsqlCommand("""
                SELECT EXISTS(SELECT 1 FROM project_flowhive_working_copies wc
                 CROSS JOIN LATERAL jsonb_array_elements(CASE WHEN jsonb_typeof(wc.working_payload->'assignments')='array' THEN wc.working_payload->'assignments' ELSE '[]'::jsonb END) a
                 WHERE wc.project_id=@project AND a->>'projectContactId'=@contact);
                """,c,transaction);assigned.Parameters.AddWithValue("project",project);assigned.Parameters.AddWithValue("contact",id.ToString());
            if(await assigned.ExecuteScalarAsync(token) is true) throw new InputException("contact","Remove this contact from current WBS assignments before archiving. Saved history is retained.",409);
        }
        var version=Guid.NewGuid();
        var sql=input.ProjectContactId.HasValue ? """
            UPDATE project_flowhive_contacts SET display_name=@name,email=@email,phone=@phone,title=@title,organization=@organization,
              contact_kind=@kind,is_active=@active,row_version=@version,updated_by_user_id=@actor,updated_at=now()
            WHERE project_id=@project AND project_contact_id=@id AND row_version=@expected RETURNING project_contact_id;
            """ : """
            INSERT INTO project_flowhive_contacts(project_contact_id,project_id,display_name,email,phone,title,organization,contact_kind,is_active,row_version,created_by_user_id,updated_by_user_id)
            VALUES(@id,@project,@name,@email,@phone,@title,@organization,@kind,@active,@version,@actor,@actor) RETURNING project_contact_id;
            """;
        await using var q=new NpgsqlCommand(sql,c,transaction);
        q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("project",project);q.Parameters.AddWithValue("name",name);q.Parameters.AddWithValue("email",email);
        q.Parameters.AddWithValue("phone",phone);q.Parameters.AddWithValue("title",title);q.Parameters.AddWithValue("organization",organization);q.Parameters.AddWithValue("kind",kind);
        q.Parameters.AddWithValue("active",input.IsActive);q.Parameters.AddWithValue("version",version);q.Parameters.AddWithValue("actor",actor);
        q.Parameters.Add(new("expected",NpgsqlDbType.Uuid){Value=(object?)input.ExpectedRowVersion??DBNull.Value});
        try { if(await q.ExecuteScalarAsync(token) is not Guid) throw new InputException("contact","This contact changed or is outside this project. Refresh before saving.",409); }
        catch(PostgresException e) when(e.SqlState=="23505") {throw new InputException("email","An active contact with this email already exists in this project.",409);}
        catch(PostgresException e) when(e.ConstraintName=="flowhive_project_contact_limit") {throw new InputException("contacts","This project already has 15 active external contacts. Archive an unused contact before adding another.",409);}
        return(id,version);
    }
    internal static async Task<ProjectFlowHivePlanRequest> ResolveContactsAsync(NpgsqlConnection c,NpgsqlTransaction transaction,Guid project,ProjectFlowHivePlanRequest plan,CancellationToken token)
    {
        if(!(plan.Assignments??[]).Any(a=>a.ProjectContactId.HasValue)) return plan;
        if(!await ReadyAsync(c,token)) throw new InputException("assignments","Project contacts require migration 131 before they can be assigned.",503);
        var contacts=(await ContactsAsync(c,project,token)).Where(x=>x.IsActive).ToDictionary(x=>x.ProjectContactId);
        var seen=new HashSet<string>();var rows=new List<ProjectFlowHivePlanAssignmentInput>();
        foreach(var item in plan.Assignments??[])
        {
            if(item.ProjectContactId is not Guid id){rows.Add(item);continue;}
            if(item.ResourceUserId.HasValue || !contacts.TryGetValue(id,out var contact)) throw new InputException($"WBS {item.TaskWbs}.projectContactId","Select an active external contact belonging to this project.");
            if(!seen.Add(item.TaskWbs+":"+id)) throw new InputException($"WBS {item.TaskWbs}.projectContactId","This contact is assigned more than once to the same task.");
            rows.Add(item with {ResourceDisplayName=contact.DisplayName});
        }
        return plan with {Assignments=rows};
    }
    internal static async Task<List<Meeting>> MeetingsAsync(NpgsqlConnection c,Guid project,CancellationToken token)
    {
        var result=new List<Meeting>();await using var q=new NpgsqlCommand("SELECT meeting_draft_id,title,agenda,location,starts_at,ends_at,timezone_name,attendee_references,status FROM project_flowhive_meeting_drafts WHERE project_id=@project ORDER BY starts_at DESC LIMIT 50;",c);
        q.Parameters.AddWithValue("project",project);await using var rd=await q.ExecuteReaderAsync(token);
        while(await rd.ReadAsync(token))result.Add(new(rd.GetGuid(0),rd.GetString(1),rd.GetString(2),rd.GetString(3),rd.GetFieldValue<DateTimeOffset>(4),rd.GetFieldValue<DateTimeOffset>(5),rd.GetString(6),rd.GetFieldValue<string[]>(7),rd.GetString(8)));
        return result;
    }
    internal static async Task<Dictionary<string,(string Name,string Email)>> AttendeesAsync(NpgsqlConnection c,Guid project,CancellationToken token)
    {
        var people=new Dictionary<string,(string,string)>(StringComparer.OrdinalIgnoreCase);
        foreach(var p in await TeamAsync(c,project,token)) if(MailAddress.TryCreate(p.Email,out var address) && address.Address==p.Email && !p.Email.Any(char.IsControl))people["user:"+p.UserId]=(p.DisplayName,p.Email);
        foreach(var p in await ContactsAsync(c,project,token))if(p.IsActive)people["contact:"+p.ProjectContactId]=(p.DisplayName,p.Email);
        return people;
    }
    internal static async Task<Guid> CreateMeetingAsync(NpgsqlConnection c,NpgsqlTransaction tx,Guid project,Guid actor,FlowHiveMeetingDraftRequest input,CancellationToken token)
    {
        var title=Text(input.Title,"title",240,true);var location=Text(input.Location,"location",300);
        var zone=Text(input.TimezoneName,"timezoneName",100,true);
        try{global::ProjectTime.Api.SafeTimeZones.FindSystemTimeZoneById(zone);}catch(Exception e) when(e is TimeZoneNotFoundException or InvalidTimeZoneException){throw new InputException("timezoneName","Select a recognized time zone.");}
        if(input.StartsAt is null || input.EndsAt is null || input.EndsAt<=input.StartsAt || input.EndsAt>input.StartsAt.Value.AddHours(24))throw new InputException("endsAt","Choose a start and a later finish, no more than 24 hours apart.");
        if((input.Agenda??"").Length>8000)throw new InputException("agenda","Keep the customer-visible agenda under 8,000 characters.");
        var references=(input.AttendeeReferences??[]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var people=await AttendeesAsync(c,project,token);
        if(references.Length is <1 or >50 || references.Any(x=>!people.ContainsKey(x)))throw new InputException("attendees","Choose 1 to 50 current project team members or project contacts with email addresses.");
        var id=Guid.NewGuid();await using var q=new NpgsqlCommand("""
          INSERT INTO project_flowhive_meeting_drafts(meeting_draft_id,project_id,title,agenda,location,starts_at,ends_at,timezone_name,attendee_references,created_by_user_id)
          VALUES(@id,@project,@title,@agenda,@location,@start,@end,@zone,@people,@actor);
          """,c,tx);
        q.Parameters.AddWithValue("id",id);q.Parameters.AddWithValue("project",project);q.Parameters.AddWithValue("title",title);q.Parameters.AddWithValue("agenda",input.Agenda??"");
        q.Parameters.AddWithValue("location",location);q.Parameters.AddWithValue("start",input.StartsAt.Value.ToUniversalTime());q.Parameters.AddWithValue("end",input.EndsAt.Value.ToUniversalTime());
        q.Parameters.AddWithValue("zone",zone);q.Parameters.AddWithValue("people",references);q.Parameters.AddWithValue("actor",actor);await q.ExecuteNonQueryAsync(token);return id;
    }
    internal static string CalendarDraft(Meeting meeting,IEnumerable<(string Name,string Email)> people)
    {
        static string Escape(string value)=>value.Replace("\\","\\\\").Replace("\r","").Replace("\n","\\n").Replace(";","\\;").Replace(",","\\,");
        var lines=new List<string>{"BEGIN:VCALENDAR","VERSION:2.0","PRODID:-//Pulse//FlowHive meeting draft//EN","METHOD:PUBLISH","BEGIN:VEVENT",
         "UID:"+meeting.MeetingDraftId+"@pulse.invalid","DTSTAMP:"+DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'"),
         "DTSTART:"+meeting.StartsAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'"),"DTEND:"+meeting.EndsAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'"),
         "SUMMARY:"+Escape(meeting.Title),"DESCRIPTION:"+Escape(meeting.Agenda),"LOCATION:"+Escape(meeting.Location),"STATUS:TENTATIVE"};
        foreach(var person in people.DistinctBy(p=>p.Email.ToLowerInvariant()))
            lines.Add("ATTENDEE;CN=\""+person.Name.Replace("\"","'").Replace("\r","").Replace("\n","")+"\":mailto:"+person.Email);
        lines.AddRange(["END:VEVENT","END:VCALENDAR"]);
        var result=new StringBuilder();foreach(var line in lines){var bytes=0;foreach(var rune in line.EnumerateRunes()){var size=rune.Utf8SequenceLength;if(bytes+size>73){result.Append("\r\n ");bytes=1;}result.Append(rune);bytes+=size;}result.Append("\r\n");}return result.ToString();
    }

}
public sealed record FlowHiveContactRequest(string? DisplayName,string? Email,string? Phone,string? Title,string? Organization,string? ContactKind,Guid? ProjectContactId=null,Guid? ExpectedRowVersion=null,bool IsActive=true);
public sealed record FlowHiveMeetingDraftRequest(string? Title,string? Agenda,string? Location,DateTimeOffset? StartsAt,DateTimeOffset? EndsAt,string? TimezoneName,string[]? AttendeeReferences);
