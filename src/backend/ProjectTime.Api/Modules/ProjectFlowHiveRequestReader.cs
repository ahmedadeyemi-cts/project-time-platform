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
        catch(JsonException error) { return Invalid(error.Path ?? "$","Check the date, number, list or identity in this field. Blank optional dates are allowed; working-day durations must be whole numbers.",correlation); }
        catch(ArgumentException) { return Invalid("$","The request contains duplicate or invalid JSON fields.",correlation); }
    }
    internal static async Task<ReadResult> ReadAsync(HttpContext context,CancellationToken token)
    {
        if(!context.Request.HasJsonContentType()) return new(null,Results.Json(new {status="json_body_required",message="Send the working copy as JSON.",stateChanged=false},statusCode:415));
        using var reader=new StreamReader(context.Request.Body,leaveOpen:true);
        var body=new StringBuilder();var buffer=new char[8192];int read;
        while((read=await reader.ReadAsync(buffer.AsMemory(),token))>0)
        {
            if(body.Length+read>2_000_000) return new(null,Results.Json(new {status="request_body_too_large",message="The plan exceeds the working-copy request limit.",stateChanged=false},statusCode:413));
            body.Append(buffer,0,read);
        }
        return ParseWorkingCopy(body.ToString(),context.TraceIdentifier);
    }
    private static void ClearOptional(JsonObject row,string key)
    { if(row[key] is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrWhiteSpace(text)) row[key]=null; }
    private static ReadResult Invalid(string path,string message,string correlation) => new(null,Results.BadRequest(new {
        status="invalid_plan_field",message="The working copy was not saved. Correct the indicated field and try again.",
        issues=new[]{new {code="invalid_plan_field",severity="error",path,message}},correlationId=correlation,stateChanged=false }));
}
