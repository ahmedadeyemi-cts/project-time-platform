using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProjectTime.Api.Modules;

// Narrow compatibility for cleared OPTIONAL fields, not a global relaxed JSON policy.
internal static class ProjectFlowHiveRequestReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal sealed record ReadResult(ProjectFlowHiveWorkingCopyRequest? Request, IResult? Error);
    internal static ReadResult ParseWorkingCopy(string text, string correlation = "")
    {
        try
        {
            var root=JsonNode.Parse(text, new JsonNodeOptions { PropertyNameCaseInsensitive=true });
            if(root is not JsonObject body || body["plan"] is not JsonObject plan)
                return Invalid("plan","A project working-copy plan is required.",correlation);
            ClearOptional(body,"expectedRowVersion");
            foreach(var key in new[]{"projectId","planId","projectStartDate","projectEndDate","celarAiConfidence"}) ClearOptional(plan,key);
            if(plan["tasks"] is JsonArray tasks) foreach(var row in tasks.OfType<JsonObject>())
                foreach(var key in new[]{"clientTaskId","canonicalTaskId","constraintDate","estimatedStartDate","estimatedFinishDate"}) ClearOptional(row,key);
            if(plan["assignments"] is JsonArray assignments) foreach(var row in assignments.OfType<JsonObject>())
                foreach(var key in new[]{"resourceUserId","projectContactId"}) ClearOptional(row,key);
            if(plan["milestones"] is JsonArray milestones) foreach(var row in milestones.OfType<JsonObject>()) ClearOptional(row,"targetDate");
            foreach(var key in new[]{"tasks","dependencies","assignments","milestones"})
                if(plan[key] is JsonArray rows)
                    for(var i=0;i<rows.Count;i++) if(rows[i] is not JsonObject) return Invalid($"$.plan.{key}[{i}]","Each entry must be an object, not an empty or scalar row.",correlation);
            var result=body.Deserialize<ProjectFlowHiveWorkingCopyRequest>(Json);
            return result?.Plan is null ? Invalid("plan","A project working-copy plan is required.",correlation) : new(result,null);
        }
        catch(JsonException error) { return Invalid(error.Path ?? "$", string.IsNullOrWhiteSpace(text) ? "The request body was empty. Your edits have not been saved; retry the save." : "Check the date, number, list or identity in this field. Blank optional dates are allowed; working-day durations must be whole numbers.",correlation); }
        catch(ArgumentException) { return Invalid("$","The request contains duplicate or invalid JSON fields.",correlation); }
    }
    internal static async Task<ReadResult> ReadAsync(HttpContext context,CancellationToken token)
    {
        if(!context.Request.HasJsonContentType()) return new(null,Results.Json(new {status="json_body_required",message="Send the working copy as JSON.",stateChanged=false},statusCode:415));
        // Security inspection may have buffered the body before this authorized handler.
        if(context.Request.Body.CanSeek) context.Request.Body.Position=0;
        using var reader=new StreamReader(context.Request.Body,leaveOpen:true);
        var body=new StringBuilder();var buffer=new char[8192];int read;
        while((read=await reader.ReadAsync(buffer.AsMemory(),token))>0)
        {
            if(body.Length+read>2_000_000) return new(null,Results.Json(new {status="request_body_too_large",message="The plan exceeds the working-copy request limit.",stateChanged=false},statusCode:413));
            body.Append(buffer,0,read);
        }
        return ParseWorkingCopy(body.ToString(),context.TraceIdentifier);
    }
    internal sealed record ContactReadResult(FlowHiveContactRequest? Request, IResult? Error);
    internal static ContactReadResult ParseContact(string text, string correlation = "")
    {
        try
        {
            var node=JsonNode.Parse(text,new JsonNodeOptions {PropertyNameCaseInsensitive=true});
            if(node is not JsonObject body) return new(null,Invalid("contact","Enter the contact information as a JSON object.",correlation).Error);
            ClearOptional(body,"projectContactId"); ClearOptional(body,"expectedRowVersion");
            return new(body.Deserialize<FlowHiveContactRequest>(Json),null);
        }
        catch(JsonException error) { return new(null,Invalid(error.Path ?? "contact","Check this contact field. Names, email, phone, title and organization must be text; saved contact identifiers must be valid.",correlation).Error); }
        catch(ArgumentException) { return new(null,Invalid("contact","The contact request contains duplicate fields.",correlation).Error); }
    }
    internal static async Task<ContactReadResult> ReadContactAsync(HttpContext context,CancellationToken token)
    {
        if(!context.Request.HasJsonContentType()) return new(null,Results.Json(new {message="Send contact information as JSON.",stateChanged=false},statusCode:415));
        if(context.Request.Body.CanSeek) context.Request.Body.Position=0;
        using var reader=new StreamReader(context.Request.Body,leaveOpen:true);
        var body=new StringBuilder(); var buffer=new char[4096]; int read;
        while((read=await reader.ReadAsync(buffer.AsMemory(),token))>0)
        {
            if(body.Length+read>16000) return new(null,Results.Json(new {message="The contact information exceeds the request limit.",stateChanged=false},statusCode:413));
            body.Append(buffer,0,read);
        }
        return ParseContact(body.ToString(),context.TraceIdentifier);
    }
    private static void ClearOptional(JsonObject row,string key)
    { if(row[key] is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text)) row[key]=null; }
    private static ReadResult Invalid(string path,string message,string correlation) => new(null,Results.BadRequest(new {
        status="invalid_plan_field",message="The changes were not saved. Correct the indicated field and try again.",
        issues=new[]{new {code="invalid_plan_field",severity="error",path,message}},correlationId=correlation,stateChanged=false }));
}
