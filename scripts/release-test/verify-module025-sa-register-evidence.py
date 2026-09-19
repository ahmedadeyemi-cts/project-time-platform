#!/usr/bin/env python3
"""Require this run's completed actual-SA browser evidence; never generate."""
import json
import os
import re
from pathlib import Path


def verify(report, run_id):
    evidence = report.get('registerBrowser') or {}
    if (not re.fullmatch(r'[0-9]+', run_id) or report.get('status') != 'passed'
            or report.get('normalAuthorizedSolutionArchitect') is not True
            or evidence != {'status': 'passed', 'sourceRunId': run_id,
                            'generationPosts': 0, 'businessWrites': 0}):
        raise RuntimeError('module025_current_run_normal_sa_register_evidence_required')


if __name__ == '__main__':
    report = json.loads((Path(os.environ['EVIDENCE_DIR']) / 'module025-installed-sa-uat.json').read_text())
    verify(report, os.environ.get('GITHUB_RUN_ID', ''))
    print('MODULE025_NORMAL_SA_REGISTER_EVIDENCE=PASS generationPosts=0 writes=0')
