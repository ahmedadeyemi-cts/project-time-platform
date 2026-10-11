#!/usr/bin/env python3
"""Reject dispatch input expressions inside shell source, including typed inputs."""
import argparse
from pathlib import Path
import re
import yaml

EXPRESSIONS = re.compile(r"\$\{\{(.*?)\}\}", re.S)
INPUT_TOKEN = re.compile(r"\binputs\b", re.I)
# A whole event object, or dynamic lookup into it, can also expose inputs.
EVENT_OBJECT = re.compile(r"\bgithub\s*(?:\.\s*event\b|\[\s*['\"]event['\"]\s*\])\s*(?:$|[)\[])", re.I)

def violations(workflow):
    result = []
    for job_name, job in workflow.get('jobs', {}).items():
        for index, step in enumerate(job.get('steps', [])):
            for expression in EXPRESSIONS.findall(step.get('run', '')):
                if INPUT_TOKEN.search(expression) or EVENT_OBJECT.search(expression):
                    result.append((job_name, index, step.get('name', 'unnamed')))
    return result

def validate(root):
    files = sorted((Path(root) / '.github/workflows').glob('*.y*ml'))
    if not files:
        raise ValueError('No repository workflows found')
    errors = []
    for path in files:
        errors.extend((path.name, *v) for v in violations(yaml.safe_load(path.read_text())))
    if errors:
        raise ValueError('Dispatch input expressions must enter through env, never run source: ' + repr(errors))
    return len(files)

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--repo-root', default=str(Path(__file__).resolve().parents[2]))
    print('WORKFLOW_INPUT_SOURCE_BOUNDARIES=PASS workflows=' + str(validate(parser.parse_args().repo_root)))
