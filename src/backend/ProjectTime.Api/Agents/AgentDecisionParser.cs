using System.Text.Json;

namespace ProjectTime.Api.Agents;

public static class AgentDecisionParser
{
    public static AgentDecision Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 8192)
            throw new AgentBoundaryException("agent_decision_invalid");
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name) || property.Name is not ("kind" or "tool" or "handoff" or "note")
                    || property.Value.ValueKind != JsonValueKind.String) throw new JsonException();
            string Read(string key) => root.TryGetProperty(key, out var value) ? value.GetString()! : "";
            var kind = Read("kind") switch
            {
                "read" => AgentDecisionKind.Read, "ask" => AgentDecisionKind.Ask,
                "handoff" => AgentDecisionKind.Handoff, "finish" => AgentDecisionKind.Finish,
                _ => throw new JsonException()
            };
            var result = new AgentDecision(kind, Read("tool"), Read("handoff"), Read("note"));
            Validate(result);
            return result;
        }
        catch (JsonException) { throw new AgentBoundaryException("agent_decision_invalid"); }
    }

    public static void Validate(AgentDecision value)
    {
        if (value.Tool.Length > 80 || value.Handoff.Length > 80 || value.Note.Length > 2000
            || !Enum.IsDefined(value.Kind)) throw new AgentBoundaryException("agent_decision_invalid");
        var valid = value.Kind switch
        {
            AgentDecisionKind.Read => value.Tool.Length > 0 && value.Handoff.Length == 0,
            AgentDecisionKind.Handoff => value.Handoff.Length > 0 && value.Tool.Length == 0
                && !string.IsNullOrWhiteSpace(value.Note),
            AgentDecisionKind.Ask or AgentDecisionKind.Finish => value.Tool.Length == 0
                && value.Handoff.Length == 0 && !string.IsNullOrWhiteSpace(value.Note),
            _ => false
        };
        if (!valid) throw new AgentBoundaryException("agent_decision_invalid");
    }
}
