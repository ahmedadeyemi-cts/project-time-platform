#!/usr/bin/env python3
import importlib.util
from pathlib import Path
spec = importlib.util.spec_from_file_location('check', Path(__file__).with_name('verify-core-only-release-admission.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
a = 'a' * 40
assert module.evaluate(a,a,'refs/heads/main','test','NOT_EXECUTED') == []
for vals in [(a,'b'*40,'refs/heads/main','test','NOT_EXECUTED'),(a,a,'refs/heads/dev','test','NOT_EXECUTED'),(a,a,'refs/heads/main','production','NOT_EXECUTED'),(a,a,'refs/heads/main','test','PASS'),('invalid',a,'refs/heads/main','test','NOT_EXECUTED')]:
    assert module.evaluate(*vals)
print('PULSE_CORE_ADMISSION_POLICY_TEST=PASS')
