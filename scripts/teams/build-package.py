#!/usr/bin/env python3
"""Build an installable personal Teams app; no upload, consent or policy mutations."""
import argparse
import io
import json
from pathlib import Path
import zipfile
from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[2]

def build(output: Path) -> None:
    manifest = json.loads((ROOT / 'deployment/teams/pulse-uat.manifest.json').read_text())
    assert manifest['version'] == '1.0.2'
    assert manifest['id'] == '1e9ddbc8-4618-43f4-82a6-dd3efefd2481'
    assert manifest['webApplicationInfo']['id'] == 'f30f7ad8-663d-447c-a8e6-ac908b738167'
    assert manifest['staticTabs'][0]['scopes'] == ['personal']
    assert manifest['staticTabs'][0]['entityId'] == 'pulse-notifications'
    logo = Image.open(ROOT / 'src/frontend/project-time-web/brand/ussignal.png').convert('RGBA')
    color = Image.new('RGBA', (192, 192), 'white')
    logo.thumbnail((168, 168), Image.Resampling.LANCZOS)
    color.alpha_composite(logo, ((192-logo.width)//2, (192-logo.height)//2))
    # White outline mark on a transparent canvas, derived from the same brand asset.
    ink = ImageOps.invert(color.convert('L')).resize((32, 32), Image.Resampling.LANCZOS)
    outline = Image.new('RGBA', (32, 32), (255,255,255,0)); outline.putalpha(ink)
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        entries = {'manifest.json': (json.dumps(manifest, indent=2)+'\n').encode()}
        for name, image in [('color.png',color), ('outline.png',outline)]:
            memory=io.BytesIO(); image.save(memory, format='PNG'); entries[name]=memory.getvalue()
        for name, content in entries.items():
            info=zipfile.ZipInfo(name, date_time=(2026,9,23,0,0,0)); info.compress_type=zipfile.ZIP_DEFLATED
            archive.writestr(info,content)
    print(f'Built {output.name}: manifest.json, 192px color icon, 32px transparent outline icon. Not uploaded or installed.')

if __name__ == '__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--output',type=Path,required=True)
    build(parser.parse_args().output)
