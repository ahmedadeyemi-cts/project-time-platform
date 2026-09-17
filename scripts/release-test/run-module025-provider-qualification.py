#!/usr/bin/env python3
"""One manually approved Test-only provider call in an isolated temporary job.

Reads the existing API configuration; copies only DB/encryption and the chosen
cloud provider settings into the job. Never prints secrets, changes the API,
runs migrations, calls FlowHive, or automatically repeats an inference.
"""
import base64
import copy
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time

API_VERSION = '2024-03-01'
DB_NAMES = {'ConnectionStrings__DefaultConnection', 'ConnectionStrings__ProjectPulse',
    'ConnectionStrings__ProjectTime', 'PROJECTPULSE_CONNECTION_STRING', 'PROJECTTIME_DATABASE_CONNECTION',
    'PROJECTPULSE_DB_CONNECTION', 'PROJECTTIME_DB_CONNECTION',
    'PTP_DB_HOST', 'PTP_DB_PORT', 'PTP_DB_NAME', 'PTP_DB_USER', 'PTP_DB_PASSWORD'}
KEY_NAMES = {'PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY', 'PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID',
    'PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_RING'}
EXTERNAL_POLICY_NAMES = {'PROJECTPULSE_AI_ALLOW_SANITIZED_EXTERNAL_ESCALATION',
    'PROJECTPULSE_CELAR_AI_SANITIZED_EXTERNAL_FALLBACK_ENABLED'}
# Run 35172706135 stopped before job/start; recover only this exact owned job.
PRIOR_SCOPE = '35172706135-1'
PRIOR_SOURCE = '0c1b3503759c27495476e06f1f285174e8dfb23b'


def require(condition, code):
    if not condition:
        raise RuntimeError(code)


def az(*args, timeout=90):
    # Azure diagnostics can contain request payloads. Return only closed errors.
    result = subprocess.run(['az', *args, '--only-show-errors', '-o', 'json'],
        capture_output=True, text=True, timeout=timeout, check=False)
    require(result.returncode == 0, 'azure_operation_failed_' + args[0].replace('-', '_'))
    if args[:4] == ('containerapp', 'job', 'logs', 'show'):
        decoder, remaining, entries = json.JSONDecoder(), result.stdout.strip(), []
        while remaining:
            value, consumed = decoder.raw_decode(remaining)
            entries.extend(value if isinstance(value, list) else [value])
            remaining = remaining[consumed:].lstrip()
        return entries
    return json.loads(result.stdout) if result.stdout.strip() else None


def selected_environment(api, provider):
    containers = api['properties']['template']['containers']
    require(len(containers) == 1, 'single_test_api_container_required')
    existing = containers[0].get('env', [])
    values = {item['name']: item for item in existing}
    require(values.get('PROJECTPULSE_ENVIRONMENT', {}).get('value') == 'test', 'test_api_environment_required')
    prefix = 'PROJECTPULSE_' + provider.upper() + '_'
    approved = DB_NAMES | KEY_NAMES | EXTERNAL_POLICY_NAMES | {'PROJECTPULSE_ENVIRONMENT', 'PROJECTPULSE_AI_' + provider.upper() + '_ENABLED'}
    approved |= {prefix + name for name in ['MODEL', 'ENDPOINT', 'API_VERSION', 'APPROVED_MODELS', 'ORGANIZATION', 'PROJECT']}
    result = [dict(item) for item in existing if item['name'] in approved]
    require(any(item['name'] in DB_NAMES for item in result), 'test_database_configuration_missing')
    require(any(item['name'] in KEY_NAMES - {'PROJECTPULSE_AI_SECRET_ENCRYPTION_KEY_ID'} for item in result), 'module064_encryption_configuration_missing')
    result.append({'name': 'MODULE025_QUALIFICATION_LOG_CHUNKS', 'value': 'true'})
    environment_contract(result)
    return result


def require_existing_external_policy(environment):
    values = {item['name']: item for item in environment}
    for name in sorted(EXTERNAL_POLICY_NAMES):
        require(name in values, 'sanitized_external_policy_missing')
        item = values[name]
        require(not item.get('secretRef') and isinstance(item.get('value'), str), 'sanitized_external_policy_unverifiable')
        require(item['value'].strip().lower() == 'true', 'sanitized_external_policy_disabled')


def closed_error(error):
    return str(error) if type(error) is RuntimeError and re.fullmatch(r'[a-z0-9_]{1,100}', str(error)) else 'qualification_infrastructure_' + type(error).__name__


def build_payload(api, provider, image, name, scope, secrets):
    env = selected_environment(api, provider)
    environment_id = api['properties']['managedEnvironmentId']
    require('/providers/microsoft.app/managedenvironments/' in environment_id.lower(), 'test_network_identity_missing')
    server = image.split('/')[0]
    require(re.fullmatch(r'[a-z0-9]+\.azurecr\.io/module025-qualification@sha256:[a-f0-9]{64}', image), 'immutable_qualification_image_required')
    registries = api['properties']['configuration'].get('registries', [])
    registry = next((item for item in registries if item.get('server') == server), None)
    registry_identity = os.environ.get('AZURE_CELAR_MIGRATOR_IDENTITY_RESOURCE_ID') or (registry or {}).get('identity', '')
    require(registry_identity.startswith('/subscriptions/'), 'existing_registry_managed_identity_required')
    identities = {registry_identity: {}}
    required_refs = {item['secretRef'] for item in env if item.get('secretRef')}
    secret_map = {item['name']: item for item in secrets}
    selected_secrets = []
    for ref in sorted(required_refs):
        secret = secret_map.get(ref)
        require(secret is not None, 'required_test_secret_unavailable')
        if secret.get('keyVaultUrl') and secret.get('identity', '').startswith('/subscriptions/'):
            identity = secret.get('identity', '')
            require(identity.startswith('/subscriptions/'), 'existing_keyvault_managed_identity_required')
            identities[identity] = {}
            selected_secrets.append({'name': ref, 'keyVaultUrl': secret['keyVaultUrl'], 'identity': identity})
        else:
            require(bool(secret.get('value')), 'required_test_secret_value_unavailable')
            selected_secrets.append({'name': ref, 'value': secret['value']})
    existing_ids = {key.lower() for key in api.get('identity', {}).get('userAssignedIdentities', {})}
    require(all(key.lower() in existing_ids for key in identities), 'identity_must_already_belong_to_test_api')
    return {'location': api['location'], 'identity': {'type': 'UserAssigned', 'userAssignedIdentities': identities},
        'tags': {'projectpulse-scope': 'module025-one-phase-test', 'projectpulse-run': scope, 'projectpulse-source': os.environ['GITHUB_SHA']},
        'properties': {'environmentId': environment_id,
            'configuration': {'triggerType': 'Manual', 'replicaTimeout': 240, 'replicaRetryLimit': 0,
                'manualTriggerConfig': {'replicaCompletionCount': 1, 'parallelism': 1},
                'registries': [{'server': server, 'identity': registry_identity}], 'secrets': selected_secrets},
            'template': {'containers': [{'name': name, 'image': image,
                'command': ['dotnet'], 'args': ['FlowHiveDetailedPlannerTests.dll', '--qualify-sow-provider', provider, '--use-module064-store'],
                'env': env, 'resources': {'cpu': 0.5, 'memory': '1Gi'}}]}}}


def environment_contract(entries):
    """Compare runtime bindings, not Azure's optional null fields or list order.

    Secret references remain distinct from literal values. Duplicate names,
    unknown populated fields and order-dependent variable expansion fail closed.
    """
    require(isinstance(entries, list), 'qualification_job_env_invalid')
    result = {}
    for item in entries:
        name = item.get('name')
        require(isinstance(name, str) and name and name not in result, 'qualification_job_env_names_invalid')
        require(not any(value is not None for key, value in item.items()
            if key not in ('name', 'value', 'secretRef')), 'qualification_job_env_fields_invalid')
        ref, value = item.get('secretRef'), item.get('value')
        if ref:
            require(isinstance(ref, str) and value in (None, ''), 'qualification_job_env_binding_invalid')
            result[name] = ('secretRef', ref)
        else:
            require(ref in (None, '') and (value is None or isinstance(value, str)), 'qualification_job_env_binding_invalid')
            require('$(' not in (value or ''), 'qualification_job_env_expansion_unsupported')
            result[name] = ('value', value or '')
    return result


def verify_job_ownership(job, payload):
    require(job.get('tags') == payload['tags'], 'qualification_job_ownership_mismatch')
    actual, expected = job['properties'], payload['properties']
    require(actual['environmentId'].lower() == expected['environmentId'].lower(), 'qualification_job_environment_mismatch')
    containers = actual['template']['containers']
    require(len(containers) == 1, 'qualification_job_container_count_mismatch')
    for key in ['name', 'image']:
        require(containers[0].get(key) == expected['template']['containers'][0][key], 'qualification_job_' + key + '_mismatch')


def api_template_contract(template):
    """Normalize only named environment bindings; all other fields stay exact.

    Azure may reorder bindings or add null value/secretRef keys. Do not hide
    image, command, resource, probe, scaling, volume or unknown-field changes.
    """
    result = copy.deepcopy(template)
    for container in result.get('containers', []):
        container['env'] = environment_contract(container.get('env', []))
    return result


def api_template_difference(before, after):
    """Closed categories only: no environment names, values or secret refs."""
    first, second = api_template_contract(before), api_template_contract(after)
    categories = []
    if first == second:
        return categories
    if len(first.get('containers', [])) != len(second.get('containers', [])):
        categories.append('container_count')
    for a, b in zip(first.get('containers', []), second.get('containers', [])):
        for field in sorted(a.keys() | b.keys()):
            if a.get(field) != b.get(field):
                categories.append('container_' + field if field in (
                    'name', 'image', 'command', 'args', 'env', 'resources', 'probes', 'volumeMounts')
                    else 'container_other')
    if {k: v for k, v in first.items() if k != 'containers'} != {k: v for k, v in second.items() if k != 'containers'}:
        categories.append('template_other')
    return sorted(set(categories)) or ['template_structure']


def api_template_change_details(before, after):
    """Schema field paths and types only; never configuration values or names."""
    first, second = api_template_contract(before), api_template_contract(after)
    missing = object()
    def kind(value):
        if value is missing: return 'missing'
        if value is None: return 'null'
        if isinstance(value, bool): return 'boolean'
        if isinstance(value, (int, float)): return 'number'
        if isinstance(value, str): return 'string'
        if isinstance(value, list): return 'array'
        return 'object'
    changes = []
    def fields(a, b, prefix, known):
        for field in sorted(a.keys() | b.keys()):
            old, new = a.get(field, missing), b.get(field, missing)
            if old != new:
                changes.append({'field': prefix + (field if field in known else 'unknown_field'),
                    'beforeType': kind(old), 'afterType': kind(new)})
    fields({k:v for k,v in first.items() if k != 'containers'},
           {k:v for k,v in second.items() if k != 'containers'}, 'template.',
           {'revisionSuffix', 'initContainers', 'scale', 'volumes', 'terminationGracePeriodSeconds', 'serviceBinds'})
    a, b = first.get('containers', []), second.get('containers', [])
    if len(a) != len(b):
        changes.append({'field': 'template.containers', 'beforeType': 'array', 'afterType': 'array', 'countChanged': True})
    for index, (old, new) in enumerate(zip(a, b)):
        fields(old, new, f'template.containers[{index}].',
            {'name', 'image', 'command', 'args', 'env', 'resources', 'probes', 'volumeMounts', 'securityContext'})
    return changes


def compare_api(before, after):
    first, second = before['properties'], after['properties']
    categories = api_template_difference(first['template'], second['template'])
    return {'revisionUnchanged': first['latestRevisionName'] == second['latestRevisionName'],
        'templateUnchanged': not categories, 'changedCategories': categories,
        'changes': api_template_change_details(first['template'], second['template'])}


def require_stable_api(before, after, report, stage):
    comparison = compare_api(before, after)
    report[stage] = comparison
    require(comparison['revisionUnchanged'], 'api_revision_changed_' + stage)
    require(comparison['templateUnchanged'], 'api_template_changed_' + stage)


def verify_job(job, payload):
    verify_job_ownership(job, payload)
    actual, expected = job['properties'], payload['properties']
    for key in ['triggerType', 'replicaRetryLimit', 'replicaTimeout', 'manualTriggerConfig']:
        require(actual['configuration'].get(key) == expected['configuration'][key], 'qualification_job_execution_contract_mismatch')
    container, wanted = actual['template']['containers'][0], expected['template']['containers'][0]
    for key in ['command', 'args']:
        require(container.get(key) == wanted[key], 'qualification_job_' + key + '_mismatch')
    env, wanted_env = environment_contract(container.get('env')), environment_contract(wanted['env'])
    require(env.keys() == wanted_env.keys(), 'qualification_job_env_names_mismatch')
    require(env == wanted_env, 'qualification_job_env_bindings_mismatch')


def cleanup_prior_unstarted_job(api, registry, resource_group):
    """No inference or broad sweep: remove only the recorded, never-started job."""
    name = 'm025q-' + PRIOR_SCOPE
    jobs = az('containerapp', 'job', 'list', '-g', resource_group)
    if not any(item['name'] == name for item in jobs):
        return 'already_absent'
    job = az('containerapp', 'job', 'show', '-g', resource_group, '-n', name)
    digest = az('acr', 'repository', 'show', '-n', registry,
        '--image', 'module025-qualification:' + PRIOR_SOURCE + '-' + PRIOR_SCOPE)['digest']
    expected = {'tags': {'projectpulse-scope': 'module025-one-phase-test',
        'projectpulse-run': PRIOR_SCOPE, 'projectpulse-source': PRIOR_SOURCE},
        'properties': {'environmentId': api['properties']['managedEnvironmentId'],
            'template': {'containers': [{'name': name,
                'image': registry + '.azurecr.io/module025-qualification@' + digest}]}}}
    verify_job_ownership(job, expected)
    require(job['properties']['configuration']['triggerType'] == 'Manual', 'prior_qualification_job_not_manual')
    require(not az('containerapp', 'job', 'execution', 'list', '-g', resource_group, '-n', name),
        'prior_qualification_execution_requires_review')
    az('containerapp', 'job', 'delete', '-g', resource_group, '-n', name, '--yes')
    require(not any(item['name'] == name for item in az('containerapp', 'job', 'list', '-g', resource_group)),
        'prior_qualification_cleanup_not_verified')
    return 'verified'


def decode_report(logs):
    lines = logs if isinstance(logs, list) else [logs]
    chunks, expected = {}, None
    for line in lines:
        message = line.get('Log', line.get('message', '')) if isinstance(line, dict) else str(line)
        match = re.search(r'MODULE025_QUALIFICATION_CHUNK:(\d+):(\d+):([A-Za-z0-9+/=]+)', message)
        if not match:
            continue
        index, count = int(match[1]), int(match[2])
        require(1 <= index <= count <= 100, 'invalid_qualification_evidence_size')
        require(expected is None or expected == count, 'conflicting_qualification_evidence')
        require(index not in chunks or chunks[index] == match[3], 'conflicting_qualification_chunk')
        expected = count
        chunks[index] = match[3]
    require(expected and len(chunks) == expected, 'qualification_evidence_incomplete')
    report = json.loads(base64.b64decode(''.join(chunks[i] for i in range(1, expected + 1)), validate=True))
    require(isinstance(report, dict) and isinstance(report.get('passed'), bool), 'qualification_report_invalid')
    return report


def main():
    out = Path(os.environ['RUNNER_TEMP']) / 'module025-qualification-evidence'
    out.mkdir(mode=0o700, exist_ok=True)
    report = {'passed': False, 'called': False, 'fullLifecyclePassed': False, 'productionMutation': False}
    payload, attempted, job_uri, api_before, api_uri = None, False, '', None, ''
    resource_group = os.environ.get('AZURE_RESOURCE_GROUP', '')
    api_name = os.environ.get('AZURE_API_APP', '')
    registry = os.environ.get('AZURE_ACR_NAME', '')
    provider = os.environ.get('QUALIFICATION_PROVIDER', '')
    name = ''
    try:
        report.update({'stage': 'validate_inputs', 'provider': provider, 'sourceSha': os.environ.get('GITHUB_SHA', '')})
        require(os.environ.get('GITHUB_EVENT_NAME') == 'workflow_dispatch' and os.environ.get('GITHUB_REF') == 'refs/heads/main', 'manual_main_only')
        require(os.environ.get('GITHUB_RUN_ATTEMPT') == '1', 'automatic_repeat_not_allowed')
        require(provider in ('claude', 'openai'), 'unsupported_provider')
        require(re.fullmatch(r'[a-zA-Z0-9_-]+', resource_group) and re.fullmatch(r'[a-zA-Z0-9-]+', api_name) and re.fullmatch(r'[a-z0-9]+', registry), 'protected_test_resource_inputs_required')
        require(re.fullmatch(r'[a-f0-9]{40}', os.environ.get('GITHUB_SHA', '')), 'exact_source_required')
        scope = os.environ['GITHUB_RUN_ID'] + '-1'
        require(re.fullmatch(r'\d+-1', scope), 'run_identity_required')
        name = 'm025q-' + scope
        report['stage'] = 'read_test_configuration'
        subscription = az('account', 'show')['id']
        resource_id = f'/subscriptions/{subscription}/resourceGroups/{resource_group}/providers/Microsoft.App/containerApps/{api_name}'
        api_uri = 'https://management.azure.com' + resource_id + '?api-version=' + API_VERSION
        # Use the same raw ARM schema before and after the job. CLI extension
        # projections must not be compared with a different SDK representation.
        api_before = az('rest', '--method', 'get', '--uri', api_uri)
        require(api_before['id'].lower() == resource_id.lower(), 'exact_test_resource_required')
        report['apiSnapshotProtocol'] = {'method': 'GET', 'apiVersion': API_VERSION}
        environment = selected_environment(api_before, provider)
        # Inherit existing permission; never enable it inside the qualifier.
        # A missing/disabled policy must fail before building or starting a job.
        require_existing_external_policy(environment)
        report['externalPolicyInherited'] = True
        report['stage'] = 'preflight_before_build'
        require_stable_api(api_before, az('rest', '--method', 'get', '--uri', api_uri), report, 'before_build')
        report['stage'] = 'cleanup_recorded_prior_job'
        report['priorTemporaryJobCleanup'] = cleanup_prior_unstarted_job(api_before, registry, resource_group)
        report['stage'] = 'build_qualification_image'
        tag = 'module025-qualification:' + os.environ['GITHUB_SHA'] + '-' + scope
        az('acr', 'build', '-r', registry, '-t', tag, '--no-logs', str(Path(os.environ['RUNNER_TEMP']) / 'module025-qualification-image'), timeout=480)
        digest = az('acr', 'repository', 'show', '-n', registry, '--image', tag)['digest']
        image = registry + '.azurecr.io/module025-qualification@' + digest
        report['stage'] = 'prepare_isolated_configuration'
        secret_response = az('containerapp', 'secret', 'list', '-g', resource_group, '-n', api_name, '--show-values')
        # Azure can put Key Vault references only in the app configuration.
        secret_map = {item['name']: dict(item) for item in api_before['properties']['configuration'].get('secrets', [])}
        for item in secret_response:
            secret_map.setdefault(item['name'], {}).update(item)
        payload = build_payload(api_before, provider, image, name, scope, list(secret_map.values()))
        secret_map.clear()
        secret_response = None
        existing = az('containerapp', 'job', 'list', '-g', resource_group)
        require(not any(item['name'] == name for item in existing), 'qualification_job_already_exists')
        job_uri = f'https://management.azure.com/subscriptions/{subscription}/resourceGroups/{resource_group}/providers/Microsoft.App/jobs/{name}?api-version={API_VERSION}'
        report['stage'] = 'create_temporary_job'
        with tempfile.TemporaryDirectory(prefix='m025q-', dir=os.environ['RUNNER_TEMP']) as directory:
            file = Path(directory) / 'job.json'
            file.write_text(json.dumps(payload)); file.chmod(0o600)
            attempted = True
            az('rest', '--method', 'put', '--uri', job_uri, '--body', '@' + str(file))
        # Do not retain plaintext secret values after the job has accepted them.
        payload['properties']['configuration']['secrets'] = []
        report['stage'] = 'verify_temporary_job'
        deadline = time.monotonic() + 120
        while True:
            job = az('containerapp', 'job', 'show', '-g', resource_group, '-n', name)
            state = job['properties'].get('provisioningState')
            if state == 'Succeeded':
                verify_job(job, payload)
                break
            require(state not in ('Failed', 'Canceled') and time.monotonic() < deadline, 'qualification_job_not_provisioned')
            time.sleep(5)
        report['stage'] = 'preflight_before_inference'
        require_stable_api(api_before, az('rest', '--method', 'get', '--uri', api_uri), report, 'before_inference')
        report['called'] = None  # A lost start response does not prove inference was absent.
        report['stage'] = 'start_qualification'
        execution = az('containerapp', 'job', 'start', '-g', resource_group, '-n', name)['name']
        deadline = time.monotonic() + 300
        while True:
            executions = az('containerapp', 'job', 'execution', 'list', '-g', resource_group, '-n', name)
            state = next((item['properties']['status'] for item in executions if item['name'] == execution), '')
            if state in ('Succeeded', 'Failed', 'Stopped'): break
            require(time.monotonic() < deadline, 'qualification_execution_deadline')
            time.sleep(5)
        report['stage'] = 'collect_provider_evidence'
        logs = az('containerapp', 'job', 'logs', 'show', '-g', resource_group, '-n', name,
            '--execution', execution, '--container', name, '--tail', '300')
        report.update(decode_report(logs))
        report.update({'sourceSha': os.environ['GITHUB_SHA'], 'qualificationImage': image, 'executionStatus': state})
        require(report.get('provider') == provider and (not report['passed'] or state == 'Succeeded'), 'qualification_execution_result_mismatch')
    except Exception as error:
        report['passed'] = False
        report['diagnostic'] = closed_error(error)
    finally:
        if attempted:
            try:
                job = az('containerapp', 'job', 'show', '-g', resource_group, '-n', name)
                # A runtime-contract mismatch must block starting the job, but
                # must not block deleting the exact job this run just created.
                verify_job_ownership(job, payload)
                for execution in az('containerapp', 'job', 'execution', 'list', '-g', resource_group, '-n', name):
                    if execution['properties']['status'] not in ('Succeeded', 'Failed', 'Stopped', 'Canceled'):
                        az('containerapp', 'job', 'stop', '-g', resource_group, '-n', name, '--job-execution-name', execution['name'])
                az('containerapp', 'job', 'delete', '-g', resource_group, '-n', name, '--yes')
                jobs = az('containerapp', 'job', 'list', '-g', resource_group)
                require(not any(item['name'] == name for item in jobs), 'cleanup_not_verified')
                report['temporaryJobCleanup'] = 'verified'
            except Exception:
                report.update({'passed': False, 'temporaryJobCleanup': 'not_verified'})
        if api_before:
            try:
                after = az('rest', '--method', 'get', '--uri', api_uri)
                comparison = compare_api(api_before, after)
                report['apiDeploymentVerification'] = comparison
                report['apiDeploymentUnchanged'] = comparison['revisionUnchanged'] and comparison['templateUnchanged']
                if not report['apiDeploymentUnchanged']:
                    report.update({'passed': False, 'apiDeploymentVerificationDiagnostic':
                        'api_revision_changed' if not comparison['revisionUnchanged'] else 'api_template_changed'})
            except Exception as error:
                report.update({'passed': False, 'apiDeploymentUnchanged': 'not_verified',
                    'apiDeploymentVerificationDiagnostic': closed_error(error)})
        (out / 'qualification.json').write_text(json.dumps(report, indent=2) + '\n')
        if re.fullmatch(r'[a-z0-9_]{1,100}', str(report.get('diagnostic', ''))):
            # Only closed codes generated here, never raw Azure errors/values.
            print('MODULE025_QUALIFICATION_DIAGNOSTIC=' + report['diagnostic'])
        if report.get('apiDeploymentVerificationDiagnostic'):
            print('MODULE025_API_DEPLOYMENT_VERIFICATION_DIAGNOSTIC=' + report['apiDeploymentVerificationDiagnostic'])
        print('MODULE025_ONE_PHASE_QUALIFICATION=' + ('PASS' if report['passed'] else 'BLOCKED/FAIL'))
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
