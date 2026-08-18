from pathlib import Path

path = Path('src/frontend/project-time-web/src/EngineerTaskCloseoutCenter.jsx')
source = path.read_text()
old = 'const PAGE_SIZE = 50;'
new = 'const PAGE_SIZE = 20;'
count = source.count(old)
if count != 1:
    raise SystemExit(f'{path}: expected one PR 719 page-size marker, found {count}')
path.write_text(source.replace(old, new, 1))
Path('scripts/release-test/finalize-pr719-page-size.py').unlink(missing_ok=True)
