#!/usr/bin/env python3
"""Read-only Oracle host evidence. Emits counters and closed fields, never raw logs.

Run on the Oracle host with permission to read the system journal. No credential,
configuration, service, model, database, or deployment is changed.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.request

MODELS = {'gemma3:4b', 'qwen3:4b-instruct', 'llama3.2:3b', 'embeddinggemma', 'embeddinggemma:latest'}
SIGNALS = {
    'oom': r'out of memory|oom.kill|killed process|memory cgroup out of memory',
    'runner_exit': r'runner.*(stopped|exited|terminated)|signal: killed',
    'timeout': r'timed out|timeout|context deadline exceeded',
    'connection_reset': r'connection reset|connection refused|broken pipe|unexpected eof',
    'queue_full': r'queue.*full|too many requests',
    'model_load_failure': r'failed to load|error loading model',
}
SERVICE_FIELDS = {'ActiveState', 'SubState', 'NRestarts', 'ExecMainStatus', 'MemoryCurrent', 'MemoryPeak', 'ControlGroup'}

def command(args):
    try:
        p = subprocess.run(args, capture_output=True, text=True, timeout=15, check=False)
        if p.returncode:
            return None, 'read_failed'
        if re.search(r'permission denied|not seeing messages|insufficient permissions', p.stderr, re.I):
            return None, 'read_permission_limited'
        if len(p.stdout) > 4_000_000:
            return None, 'read_truncated'
        return p.stdout, 'complete'
    except (OSError, subprocess.TimeoutExpired):
        return None, 'read_unavailable'


def journal_summary(raw):
    counts = Counter()
    inference_statuses = Counter()
    lines = raw.splitlines()
    invalid = 0
    for line in lines:
        try:
            entry = json.loads(line)
            message = entry.get('MESSAGE', '')
            if not isinstance(message, str):
                invalid += 1
                continue
            if '/api/chat' in message or '/v1/chat/completions' in message:
                status = re.search(r'\|\s*(200|400|401|403|422|429|500|502|503|504)\s*\|', message)
                if status:
                    inference_statuses[status.group(1)] += 1
            for code, pattern in SIGNALS.items():
                if re.search(pattern, message, re.I):
                    counts[code] += 1
        except (ValueError, AttributeError):
            invalid += 1
    return {'status': 'incomplete' if invalid or len(lines) >= 5000 else 'complete',
            'entriesExamined': len(lines), 'invalidEntries': invalid,
            'signals': {key: counts[key] for key in SIGNALS},
            'inferenceHttpStatuses': dict(inference_statuses)}


def service_summary(raw):
    result = {}
    for line in raw.splitlines():
        key, _, value = line.partition('=')
        if key not in SERVICE_FIELDS:
            continue
        if key in {'ActiveState', 'SubState'} and re.fullmatch(r'[a-z-]{1,40}', value):
            result[key] = value
        elif key not in {'ActiveState', 'SubState', 'ControlGroup'} and value.isdigit():
            result[key] = int(value)
        elif key == 'ControlGroup' and re.fullmatch(r'/system.slice/(ollama|celar-ai-gateway)\.service', value):
            path = Path('/sys/fs/cgroup') / value.lstrip('/') / 'memory.events'
            try:
                result['memoryEvents'] = {k: int(v) for k, v in (row.split() for row in path.read_text().splitlines())
                                          if k in {'low', 'high', 'max', 'oom', 'oom_kill', 'oom_group_kill'} and v.isdigit()}
            except (OSError, ValueError):
                result['memoryEventsStatus'] = 'unavailable'
    result['status'] = 'complete' if 'ActiveState' in result else 'incomplete'
    return result


def model_summary(payload):
    if not isinstance(payload, dict) or not isinstance(payload.get('models'), list):
        return {'status': 'incomplete'}
    rows = []
    omitted = 0
    for item in payload['models']:
        if not isinstance(item, dict) or item.get('name') not in MODELS:
            omitted += 1
            continue
        rows.append({'model': item['name'], **{k: item[k] for k in ('size', 'size_vram', 'context_length')
                                             if type(item.get(k)) is int and item[k] >= 0}})
    return {'status': 'incomplete' if omitted else 'complete', 'loadedModels': rows, 'omittedModels': omitted}


def collect(since):
    report = {'schema': 1, 'capturedAt': datetime.now(timezone.utc).isoformat(),
              'readOnly': True, 'rawLogsIncluded': False, 'services': {}, 'journals': {},
              'logicalCpuCount': os.cpu_count()}
    try:
        report['loadAverage'] = list(os.getloadavg())
    except OSError:
        report['loadAverageStatus'] = 'unavailable'
    for service in ('ollama', 'celar-ai-gateway'):
        raw, status = command(['systemctl', 'show', service, '--property=' + ','.join(sorted(SERVICE_FIELDS))])
        report['services'][service] = service_summary(raw) if raw is not None else {'status': status}
        raw, status = command(['journalctl', '-u', service, '--since', since, '--no-pager', '-n', '5000', '-o', 'json'])
        report['journals'][service] = journal_summary(raw) if raw is not None else {'status': status}
    raw, status = command(['journalctl', '-k', '--since', since, '--no-pager', '-n', '5000', '-o', 'json'])
    report['journals']['kernel'] = journal_summary(raw) if raw is not None else {'status': status}
    try:
        keep = {'MemTotal', 'MemAvailable', 'SwapTotal', 'SwapFree'}
        report['memoryKiB'] = {k.rstrip(':'): int(v) for k, v, *_ in (line.split() for line in Path('/proc/meminfo').read_text().splitlines()) if k.rstrip(':') in keep}
    except (OSError, ValueError):
        report['memoryStatus'] = 'unavailable'
    try:
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        with opener.open('http://127.0.0.1:11434/api/ps', timeout=5) as response:
            raw = response.read(65537)
            if len(raw) > 65536:
                raise ValueError('bounded response exceeded')
            report['ollama'] = model_summary(json.loads(raw))
    except Exception:
        report['ollama'] = {'status': 'read_unavailable'}
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--since', default='2 hours ago', help='journal start time; no raw journal text is emitted')
    args = parser.parse_args()
    print(json.dumps(collect(args.since), indent=2, sort_keys=True))
