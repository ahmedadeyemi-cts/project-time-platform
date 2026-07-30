from __future__ import annotations

import os
import subprocess
from pathlib import Path

original = os.environ["ORIGINAL_HEAD"]

unique_files = [
    ".github/workflows/pulse-ai-private-runtime-activation-ci.yml",
    "database/migrations/052_document_intelligence_runtime.sql",
    "database/rollback/052_document_intelligence_runtime_rollback.sql",
    "docs/modules/module-011-pulse-ai/PRIVATE-RUNTIME-ACTIVATION.md",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeRepository.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeService.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateDocumentRuntimeWorker.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateEmbeddingClient.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateMalwareScanner.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateOcrClient.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeContracts.cs",
    "src/backend/ProjectTime.Api/Ai/PulseAiPrivateRuntimeSourceResolver.cs",
    "src/backend/ProjectTime.Api/Modules/PulseAiPrivateRuntimeModule.cs",
    "src/frontend/project-time-web/scripts/validate-module-011-private-runtime-activation.mjs",
    "src/frontend/project-time-web/src/PulseAiPrivateRuntimeWorkbench.jsx",
    "src/frontend/project-time-web/src/pulse-ai-private-runtime-workbench.css",
    "tests/test-pulse-ai-private-document-runtime-migration-052.sh",
]
subprocess.run(["git", "checkout", original, "--", *unique_files], check=True)

services_path = Path("src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs")
services = services_path.read_text()
http_anchor = '        services.AddHttpClient("ProjectPulseAi");\n'
http_registration = '''        services.AddHttpClient("PulseAiPrivateOcr", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
        });
        services.AddHttpClient("PulseAiPrivateEmbedding", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(3);
        });
'''
if 'AddHttpClient("PulseAiPrivateOcr"' not in services:
    if http_anchor not in services:
        raise SystemExit("ProjectPulse AI HTTP-client anchor was not found.")
    services = services.replace(http_anchor, http_anchor + http_registration, 1)

service_anchor = '        services.AddSingleton<PulseAiPrivateDocumentPipelineService>();\n'
runtime_registration = '''        services.AddSingleton<PulseAiPrivateRuntimeSourceResolver>();
        services.AddSingleton<PulseAiPrivateMalwareScanner>();
        services.AddSingleton<PulseAiPrivateOcrClient>();
        services.AddSingleton<PulseAiPrivateEmbeddingClient>();
        services.AddSingleton<PulseAiPrivateDocumentRuntimeRepository>();
        services.AddSingleton<PulseAiPrivateDocumentRuntimeService>();
        services.AddHostedService<PulseAiPrivateDocumentRuntimeWorker>();
'''
if 'AddSingleton<PulseAiPrivateRuntimeSourceResolver>()' not in services:
    if service_anchor not in services:
        raise SystemExit("Private document pipeline service anchor was not found.")
    services = services.replace(service_anchor, service_anchor + runtime_registration, 1)
services_path.write_text(services)

pipeline_path = Path("src/backend/ProjectTime.Api/Modules/PulseAiPrivateDocumentPipelineModule.cs")
pipeline = pipeline_path.read_text()
if "endpoints.MapPulseAiPrivateRuntimeEndpoints();" not in pipeline:
    anchor = "        return endpoints;"
    if pipeline.count(anchor) != 1:
        raise SystemExit("Private document pipeline return anchor was not unique.")
    pipeline = pipeline.replace(anchor, "        endpoints.MapPulseAiPrivateRuntimeEndpoints();\n" + anchor, 1)
pipeline_path.write_text(pipeline)

mount_path = Path("src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx")
mount = mount_path.read_text()
import_anchor = "import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';\n"
if "import PulseAiPrivateRuntimeWorkbench" not in mount:
    if import_anchor not in mount:
        raise SystemExit("Private document workbench import anchor was not found.")
    mount = mount.replace(import_anchor, import_anchor + "import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';\n", 1)

if "PulseAiPrivateRuntimeWorkbench" not in mount.split("};", 1)[0]:
    export_anchor = "  PulseAiPrivateDocumentPipelineWorkbench\n};"
    if export_anchor in mount:
        mount = mount.replace(export_anchor, "  PulseAiPrivateDocumentPipelineWorkbench,\n  PulseAiPrivateRuntimeWorkbench\n};", 1)
    else:
        export_anchor = "  PulseAiPrivateDocumentPipelineWorkbench\n}"
        if export_anchor not in mount:
            raise SystemExit("Private document workbench export anchor was not found.")
        mount = mount.replace(export_anchor, "  PulseAiPrivateDocumentPipelineWorkbench,\n  PulseAiPrivateRuntimeWorkbench\n}", 1)

if "<PulseAiPrivateRuntimeWorkbench />" not in mount:
    render_anchor = "      <PulseAiMissionControl />\n"
    if render_anchor not in mount:
        raise SystemExit("Pulse AI mission control render anchor was not found.")
    mount = mount.replace(render_anchor, render_anchor + "      <PulseAiPrivateRuntimeWorkbench />\n", 1)
mount_path.write_text(mount)

print("PR279_RUNTIME_RECONCILIATION_PATCH=PASS")
