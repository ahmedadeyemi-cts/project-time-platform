#!/usr/bin/env python3
from pathlib import Path

path = Path('src/frontend/project-time-web/src/ProjectFlowHiveCenter.jsx')
value = path.read_text(encoding='utf-8')
old = "</small></strong><button type=\"button\" disabled={!enterprise?.access?.canManage} onClick={() => addTask(task.wbsNumber)}>Add task</button>"
new = "</small></span><button type=\"button\" disabled={!enterprise?.access?.canManage} onClick={() => addTask(task.wbsNumber)}>Add task</button>"
count = value.count(old)
if count != 1:
    raise SystemExit(f'Expected exactly one mismatched FlowHive phase action tag, found {count}.')
path.write_text(value.replace(old, new, 1), encoding='utf-8')
print('FLOWHIVE_PLANNER_PHASE_TAG_FIX=PASS')
