"""Closed-target, synthetic private-runtime diagnostic. Never print response text."""
import json
import os
import time
import urllib.error
import urllib.request

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None

token = os.environ["SOW_DIAGNOSTIC_TOKEN"].strip()
if len(token) < 32:
    raise SystemExit("Configured Protected Test runtime credential is unavailable")
endpoint = "https://celarai.onenecklab.com/v1/chat/completions"
fields = {"wbs":"1.1", "name":"...", "description":"...", "estimatedDurationDays":1,
    "estimatedHours":8, "requiredRoles":["..."], "predecessors":[], "citationIds":[1],
    "isAssumption":True, "phase":"Plan", "detailedSteps":["...","..."], "inputs":["..."],
    "outputs":["..."], "acceptanceCriteria":["..."], "validationSteps":["..."],
    "customerResponsibilities":["..."], "usSignalResponsibilities":["..."], "prerequisites":["..."],
    "risks":["..."], "openQuestions":["..."]}
body = json.dumps({"model":"gemma3:4b", "stream":False, "max_tokens":4096,
    "temperature":0.05, "response_format":{"type":"json_object"}, "messages":[
      {"role":"system", "content":"Return only JSON with objective and tasks. Create exactly two distinct, detailed Plan-phase work packages for a hypothetical Cisco Unified Communications Manager 14 to 15 upgrade. Each description must be at least 80 characters. Use WBS 1.1 and 1.2. Every task must include every field in this contract: " + json.dumps(fields) + ". Include at least two ordered detailedSteps, customer/provider responsibilities, prerequisites, measurable acceptance and validation, risks, positive estimatedHours, and distinct deliverables. Cite the requested scope as citation 1; label proposed procedures and estimates as assumptions. Do not invent customer topology or completed work."},
      {"role":"user", "content":"Synthetic diagnostic only. Source 1 establishes this hypothetical request: upgrade Cisco Unified Communications Manager from 14 to 15. Generate the Plan phase only. Put unknown quantities, topology, compatibility, licensing and maintenance windows in openQuestions."}
    ]}).encode()
request = urllib.request.Request(endpoint, data=body, headers={"Authorization":"Bearer " + token,
    "Content-Type":"application/json", "X-Pulse-AI-Privacy-Boundary":"private_pulse_runtime_only",
    "X-Pulse-AI-Feature":"sow_gsd_planning", "X-Pulse-AI-External-Escalation":"false"})
started = time.monotonic()
try:
    response = urllib.request.build_opener(NoRedirect).open(request, timeout=3630)
except urllib.error.HTTPError as error:
    response = error
except Exception as error:
    print(json.dumps({"result":"transport_failure", "exceptionType":type(error).__name__, "elapsedSeconds":round(time.monotonic()-started)}))
    raise SystemExit(1)
with response:
    raw = response.read(1000001)
    report = {"httpStatus":response.status, "elapsedSeconds":round(time.monotonic()-started), "responseBytes":len(raw)}
    approved = {"gemma3:4b", "qwen3:4b-instruct", "llama3.2:3b"}
    report["selectedModel"] = response.headers.get("X-Celar-Local-Model") if response.headers.get("X-Celar-Local-Model") in approved else None
    report["attemptedModels"] = [m for m in response.headers.get("X-Celar-Local-Models-Attempted", "").split(",") if m in approved]
    try:
        data = json.loads(raw)
        code = data.get("error", {}).get("code")
        allowed = {"private_runtime_timeout", "private_runtime_unavailable", "private_runtime_response_invalid", "safety_refusal", "unauthorized", "privacy_boundary_required"}
        allowed.update("private_runtime_http_" + str(s) for s in [400,401,403,404,408,422,429,500,502,503,504])
        report["errorCode"] = code if code in allowed else None
        choices = data.get("choices", [])
        if choices:
            finish = choices[0].get("finish_reason")
            report["finishReason"] = finish if finish in {"stop","length","content_filter"} else "other"
            content = choices[0].get("message", {}).get("content", "")
            report["contentCharacters"] = len(content)
            plan = json.loads(content)
            tasks = plan.get("tasks", [])
            report["taskCount"] = len(tasks) if isinstance(tasks, list) else None
            if isinstance(tasks, list):
                report["taskFieldCoverage"] = [{"missingRequiredFields":[k for k in fields if k not in task],
                    "descriptionCharacters":len(task.get("description", "")), "stepCount":len(task.get("detailedSteps", [])),
                    "outputCount":len(task.get("outputs", []))} for task in tasks[:10] if isinstance(task, dict)]
    except (ValueError, TypeError, AttributeError):
        report["parseStatus"] = "invalid_json_or_shape"
    print(json.dumps(report))
    if response.status != 200:
        raise SystemExit(1)
