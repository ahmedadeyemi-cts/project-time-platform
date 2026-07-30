from pathlib import Path

# Preserve all current-main registrations, then re-add PR #279's runtime-only composition exactly once.
services_path = Path('src/backend/ProjectTime.Api/Ai/ProjectPulseAiServiceCollectionExtensions.cs')
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
        raise SystemExit('ProjectPulse AI HTTP-client anchor was not found.')
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
        raise SystemExit('Private document pipeline service anchor was not found.')
    services = services.replace(service_anchor, service_anchor + runtime_registration, 1)
services_path.write_text(services)

pipeline_path = Path('src/backend/ProjectTime.Api/Modules/PulseAiPrivateDocumentPipelineModule.cs')
pipeline = pipeline_path.read_text()
if 'endpoints.MapPulseAiPrivateRuntimeEndpoints();' not in pipeline:
    anchor = '        return endpoints;'
    if pipeline.count(anchor) != 1:
        raise SystemExit('Private document pipeline return anchor was not unique.')
    pipeline = pipeline.replace(anchor, '        endpoints.MapPulseAiPrivateRuntimeEndpoints();\n' + anchor, 1)
pipeline_path.write_text(pipeline)

mount_path = Path('src/frontend/project-time-web/src/WorkTaskBuilderPanel.jsx')
mount = mount_path.read_text()
import_anchor = "import PulseAiPrivateDocumentPipelineWorkbench from './PulseAiPrivateDocumentPipelineWorkbench.jsx';\n"
if "import PulseAiPrivateRuntimeWorkbench" not in mount:
    if import_anchor not in mount:
        raise SystemExit('Private document workbench import anchor was not found.')
    mount = mount.replace(import_anchor, import_anchor + "import PulseAiPrivateRuntimeWorkbench from './PulseAiPrivateRuntimeWorkbench.jsx';\n", 1)

if 'PulseAiPrivateRuntimeWorkbench' not in mount.split('};', 1)[0]:
    export_anchor = '  PulseAiPrivateDocumentPipelineWorkbench\n};'
    if export_anchor not in mount:
        raise SystemExit('Private document workbench export anchor was not found.')
    mount = mount.replace(export_anchor, '  PulseAiPrivateDocumentPipelineWorkbench,\n  PulseAiPrivateRuntimeWorkbench\n};', 1)

if '<PulseAiPrivateRuntimeWorkbench />' not in mount:
    render_anchor = '      <PulseAiMissionControl />\n'
    if render_anchor not in mount:
        raise SystemExit('Pulse AI mission control render anchor was not found.')
    mount = mount.replace(render_anchor, render_anchor + '      <PulseAiPrivateRuntimeWorkbench />\n', 1)
mount_path.write_text(mount)

workflow_path = Path('.github/workflows/pulse-ai-private-runtime-activation-ci.yml')
workflow = workflow_path.read_text()
if "EXPECTED_BASE='56dd3df" in workflow:
    raise SystemExit('PR #279 workflow regressed to its obsolete fixed base.')
for marker in ['git fetch origin main --no-tags', 'git diff --name-only origin/main...HEAD', 'PULSE_AI_PRIVATE_RUNTIME_BASE=$CURRENT_MAIN']:
    if marker not in workflow:
        raise SystemExit(f'Missing current-main source-isolation marker: {marker}')

print('PR279_POST_PR284_RECONCILIATION=PASS')
