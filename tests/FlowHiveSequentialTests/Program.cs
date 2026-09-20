using System.Reflection;
using System.Text.Json;
using ProjectTime.Api.Ai;
using ProjectTime.Api.Modules;
var assertions = 0;
void Check(bool pass, string name) { if (!pass) throw new Exception(name); assertions++; Console.WriteLine("PASS " + name); }
var module025Source = new PulseAiPrivateRetrievedChunk(
    ChunkId: "module025-service-overview",
    DocumentVersionId: Guid.NewGuid(),
    DocumentId: Guid.NewGuid(),
    ProjectId: null,
    ProjectCode: "SOW-TEST-025",
    ProjectName: "Module 025 detailed parser test",
    CustomerName: "Test customer",
    DocumentCategory: "module025_service_overview",
    DocumentVersion: "revision-1",
    Classification: "internal",
    OriginalFileName: "Saved Service Overview",
    CitationAnchor: "Module 025 Service Overview",
    PageNumber: null,
    SheetName: null,
    SectionTitle: "Service Overview",
    Text: "Upgrade Cisco Unified Communications Manager from version 14.0 to version 15.0.",
    SourceSha256: new string('a', 64),
    TextSha256: new string('b', 64),
    LexicalScore: 1m,
    SemanticScore: 1m,
    CombinedScore: 1m,
    ProcessedAt: DateTimeOffset.UtcNow,
    RankOrder: 1,
    SourceType: "module025_saved_service_overview",
    SourceModule: "025");
var module025Retrieval = new PulseAiPrivateRetrievalResult(
    Status: "completed",
    RetrievalMode: "module025_saved_service_overview",
    ResolvedProjectId: null,
    ResolvedProjectCode: "SOW-TEST-025",
    ResolvedProjectName: "Module 025 detailed parser test",
    CandidateCount: 1,
    AuthorizedCandidateCount: 1,
    Chunks: [module025Source],
    MissingEvidence: [],
    Conflicts: [],
    CoverageScore: 1m,
    DataAsOf: DateTimeOffset.UtcNow,
    DiagnosticCode: string.Empty);

var module025Phases = new[] { "Planning", "Architecture and Design", "Implementation", "Testing and Validation", "Operational Handoff" };
var module025Payload = JsonSerializer.Serialize(new
{
    summary = "Upgrade Cisco Unified Communications Manager from version 14.0 to 15.0 through a controlled readiness, architecture, implementation, validation, and operational transition that keeps customer decisions and unknown environment facts explicit.",
    phases = module025Phases.Select((phase, phaseIndex) => new
    {
        name = phase,
        acceptance = new[] { $"The customer and delivery reviewers can trace the {phase.ToLowerInvariant()} phase to retained evidence and its completion decision." },
        tests = new[] { $"Review the retained {phase.ToLowerInvariant()} evidence and confirm that the expected result and decision gate are recorded." },
        customerActions = new[] { "Provide environment facts, authorized access, decisions, and acceptance participation through the approved project process." },
        providerResponsibilities = new[] { "Perform the reviewed technical procedure, protect credentials, retain evidence, and escalate deviations before expanding scope." },
        readinessRequirements = new[] { "Required approvals, inputs, access, backup or rollback evidence, and customer contacts are available before work begins." },
        riskConsiderations = new[] { "Unsupported compatibility, missing entitlement, unavailable access, or an unapproved change condition can pause delivery and require replanning." },
        customerDecisions = new[] { "Confirm the actual CUCM topology, installed options, target compatibility, licensing entitlement, and approved change window." },
        workPackages = Enumerable.Range(1, 2).Select(packageIndex => new
        {
            wbsNumber = $"{phaseIndex + 1}.{packageIndex}",
            title = $"{phase} CUCM work package {packageIndex}",
            outcome = $"Complete technology-specific {phase.ToLowerInvariant()} work package {packageIndex} for Cisco Unified Communications Manager 14.0 to 15.0, record the customer-visible evidence, and stop at the documented decision gate when a customer topology, compatibility, licensing, access, or maintenance-window fact remains unresolved.",
            hours = 8 + phaseIndex + packageIndex,
            roles = new[] { "Cisco Collaboration Engineer", "Solution Architect" },
            dependsOn = phaseIndex == 0 && packageIndex == 1 ? Array.Empty<string>() : new[] { $"{Math.Max(1, phaseIndex)}.{packageIndex}" },
            assumption = true,
            steps = new object[]
            {
                new { text = $"Inspect the approved CUCM inputs for {phase.ToLowerInvariant()} work package {packageIndex}, record the observed result, and resolve or document every blocking discrepancy before proceeding." },
                $"Execute the reviewed {phase.ToLowerInvariant()} procedure for work package {packageIndex}, retain objective before-and-after evidence, and verify the stated completion condition."
            },
            requiredInputs = new[] { "Customer-approved CUCM inventory, compatibility evidence, access plan, and change constraints." },
            deliverables = new[] { $"Documented {phase.ToLowerInvariant()} output and evidence package {packageIndex}." },
            acceptance = packageIndex == 1
                ? new[] { $"Record the task-specific completion decision for {phase.ToLowerInvariant()} work package {packageIndex}." }
                : Array.Empty<string>(),
            customerActions = packageIndex == 1
                ? new[] { $"Confirm the task-specific customer decision for {phase.ToLowerInvariant()} work package {packageIndex}." }
                : Array.Empty<string>()
        })
    }),
    milestones = new[]
    {
        new
        {
            name = "CUCM production upgrade decision gate",
            description = "Confirm that the Cisco Unified Communications Manager production cluster is ready for the approved upgrade window and that every blocking readiness condition has a documented disposition.",
            proposedTiming = "After readiness and design approval, before production implementation begins.",
            acceptanceEvidence = new[] { "Approved readiness record, implementation decision, rollback authority, and customer change-window confirmation are retained." },
            assumption = true
        }
    },
    roles = new[] { "Solution Architect", "Cisco Collaboration Engineer", "Project Manager" },
    assumptions = new[] { "Technical procedures and effort remain proposals until the Solution Architect validates the customer environment." },
    risks = new[] { "Unsupported components or unresolved customer prerequisites can prevent a safe upgrade." },
    outOfScope = new[] { "Work outside the confirmed CUCM upgrade boundary requires separate review and approval." },
    questions = new[] { "What is the confirmed customer topology, node count, licensing state, integration inventory, and maintenance window?" },
    conflicts = Array.Empty<string>(),
    confidence = 0.82m,
    confidenceExplanation = "The saved Service Overview establishes the requested upgrade boundary; customer-specific facts still require validation."
});


var parser = typeof(PulseAiPrivateRagService).GetMethod("ParseModule025DetailedPlan", BindingFlags.Static | BindingFlags.NonPublic)!;
var template = (PulseAiPrivateFlowHivePlan)parser.Invoke(null, [module025Payload, module025Retrieval])!;
var project = Guid.NewGuid();
var sow = new ProjectPlanningDocumentEvidence(project, module025Source.DocumentId, "sow", "Fixture SOW.pdf", "ready", "",
    module025Source.DocumentVersionId, "canonical", "ready", Guid.NewGuid(), "sow", "active", "local_file", "fixture.pdf",
    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, 1, module025Source.SourceSha256, module025Source.DocumentVersion, true);
var gsd = sow with { DocumentId=Guid.NewGuid(), ActiveVersionId=Guid.NewGuid(), Category="gsd", WorkRegisterDocumentType="gsd" };
var docs = new ProjectPlanningDocumentResolution("fixture", [sow,gsd], sow, gsd, [], [sow,gsd], [], [], 0, [], []);
var evidence = module025Retrieval with { Chunks = [module025Source with { RankOrder=7 }, module025Source with {
    ChunkId="gsd-design", DocumentId=gsd.DocumentId, DocumentVersionId=gsd.ActiveVersionId!.Value, RankOrder=9,
    SectionTitle="Design", Text="Integrations must remain operational after the upgrade." }] };
var request = new PulseAiPrivateModelRequest("project_flowhive_plan", "planning", "detailed", "Return valid JSON matching PulseAiPrivateFlowHivePlan.", "CUCM upgrade", [], "FlowHive", 1024, 0.1m, "fixture");
var saves = new List<FlowHiveSequentialState>();
FlowHiveSequentialExecution Execution(FlowHiveSequentialState? saved=null) => new(docs, saved, (state, ct) => { ct.ThrowIfCancellationRequested(); saves.Add(state); return Task.CompletedTask; });
PulseAiPrivateModelResult Result(string content) => new("private_model_completed", "synthetic", "fixture", content, 123, content.Length, "", DateTimeOffset.UtcNow);
string Payload(string phase) => JsonSerializer.Serialize(template with { Tasks=template.Tasks.Where(t=>t.Phase==phase)
    .Select((t,i)=>t with { CitationIds=i==0 ? [1] : [2], Predecessors=i==0 ? (phase=="Plan" ? [] : [$"{Array.IndexOf(FlowHiveSequentialExecution.Phases,phase)}.2"]) : [$"{Array.IndexOf(FlowHiveSequentialExecution.Phases,phase)+1}.1"] }).ToArray(), Milestones=[], CitationIds=[1,2] });
string Phase(PulseAiPrivateModelRequest r) => FlowHiveSequentialExecution.Phases.Single(p=>r.UserInstruction.Contains($": {p}."));
var execution = Execution(); var calls = new List<string>(); string? snapshot = null;
var completed = await PulseAiPrivateRagService.GenerateFlowHiveSequentialAsync(request, evidence, execution, (r,ct) => {
    var phase = Phase(r); var index = Array.IndexOf(FlowHiveSequentialExecution.Phases,phase);
    Check(calls.Count==index, "sequential phase order " + phase);
    Check(saves.Last().Phases.Take(index).All(p=>p.Status=="completed"), "previous phases committed before " + phase);
    Check(saves.Last().Phases[index].Status=="processing", "timer saved before provider " + phase);
    var current = JsonSerializer.Serialize(r.Sources); snapshot ??= current;
    Check(current==snapshot, "same pinned SOW/GSD for " + phase);
    Check(r.SystemInstruction.Contains("Service Overview or Scope") && r.UserInstruction.Contains(FlowHiveSequentialExecution.PhasePurpose(phase)), "phase scope and purpose " + phase);
    if(index>0) Check(r.SystemInstruction.Contains($"{index}.2"), "prior outputs available " + phase);
    calls.Add(phase); return Task.FromResult(Result(Payload(phase)));
}, default);
Check(completed.Succeeded && calls.Count==5, "five phases assemble successfully");
var plan=JsonSerializer.Deserialize<PulseAiPrivateFlowHivePlan>(completed.Content)!;
Check(plan.Tasks.Count==10 && plan.Tasks.All(t=>t.EstimatedHours>0 && t.EstimatedDurationDays>0), "complete executable WBS retains positive estimates");
Check(plan.Tasks.Count(t=>t.CitationIds.Contains(2))==5, "GSD citations preserved through shared detail validation");
Check(plan.Tasks.Single(t=>t.Wbs=="3.1").Predecessors.Contains("2.2"), "cross-phase dependencies preserved");
Check(execution.State.Phases.All(p=>p.CompletedAt>=p.StartedAt), "all phase timers finish");
var serialized=JsonSerializer.Serialize(execution.State);
var resumed=Execution(JsonSerializer.Deserialize<FlowHiveSequentialState>(serialized));
var resumedResult=await PulseAiPrivateRagService.GenerateFlowHiveSequentialAsync(request,evidence,resumed,(_,_)=>throw new Exception("completed phase regenerated"),default);
Check(resumedResult.Succeeded, "completed checkpoints survive JSON round trip without model calls");
var partial=execution.State with { Phases=execution.State.Phases.Select((p,i)=>i<2?p:new FlowHivePhaseState(p.Phase)).ToArray() };
var restored=Execution(partial); var resumedCalls=new List<string>();
await PulseAiPrivateRagService.GenerateFlowHiveSequentialAsync(request,evidence,restored,(r,_)=>{resumedCalls.Add(Phase(r));return Task.FromResult(Result(Payload(Phase(r))));},default);
Check(resumedCalls.SequenceEqual(new[]{"Implement","Validate","Release"}), "restart resumes third phase without repeating first two");
var failed=Execution(partial); var failedCalls=0;
for(var provider=0;provider<5;provider++) {
 var failure=await PulseAiPrivateRagService.GenerateFlowHiveSequentialAsync(request,evidence,failed,(_,_)=>{failedCalls++;return Task.FromResult(new PulseAiPrivateModelResult("private_model_failed","fixture","fixture","",0,0,"private_model_http_503",DateTimeOffset.UtcNow));},default);
 Check(!failure.Succeeded && failure.Content=="", "failure never presents partial plan " + provider);
}
Check(failedCalls==4 && failed.State.Phases[3].Attempts==0, "provider attempts bounded and later phases never start");
var invalid=Execution(); var invalidCalls=0;
var rejected=await PulseAiPrivateRagService.GenerateFlowHiveSequentialAsync(request,evidence,invalid,(r,_)=>{invalidCalls++;return Task.FromResult(Result(Payload(Phase(r)).Replace("[2]","[99]")));},default);
Check(!rejected.Succeeded && invalidCalls==2 && invalid.State.Phases[1].Attempts==0, "invalid source citation fails with one schema repair and no next phase");
var changed=Execution(partial with { SourceFingerprint="changed" });
try { await PulseAiPrivateRagService.GenerateFlowHiveSequentialAsync(request,evidence,changed,(_,_)=>throw new Exception("stale source used"),default); throw new Exception("stale source accepted"); }
catch(InvalidOperationException e) when(e.Message=="flowhive_phase_source_changed") { Check(true,"source revision invalidates checkpoints"); }
using var cancel=new CancellationTokenSource(); var interrupted=Execution();
try { await PulseAiPrivateRagService.GenerateFlowHiveSequentialAsync(request,evidence,interrupted,(_,ct)=>{cancel.Cancel();ct.ThrowIfCancellationRequested();throw new Exception();},cancel.Token);throw new Exception("cancel ignored"); }
catch(OperationCanceledException) { Check(interrupted.State.Phases[1].Attempts==0,"cancel prevents later phases"); }
var safeProgress=JsonSerializer.Serialize(execution.State.Progress(false,null));
Check(!safeProgress.Contains("SourceSha256") && !safeProgress.Contains("Description") && !safeProgress.Contains("CUCM"), "progress excludes raw documents and partial WBS content");
var stopped=JsonSerializer.Serialize(interrupted.State.Progress(true,DateTimeOffset.UtcNow));
Check(stopped.Contains("stopped"), "terminal progress freezes running stage");
Check(typeof(CelarAiComposeRequest).GetProperty("FlowHiveExecution", BindingFlags.Instance|BindingFlags.NonPublic)!.GetCustomAttributes<System.Text.Json.Serialization.JsonIgnoreAttribute>().Any(), "internal checkpoint cannot be serialized to clients");
Console.WriteLine($"FLOWHIVE_SEQUENTIAL_ASSERTIONS_PASSED={assertions}");
