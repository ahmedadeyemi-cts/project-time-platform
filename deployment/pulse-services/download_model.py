"""Build-only, exact-checkpoint data download. Runtime never downloads models."""
import hashlib,json,os,sys,urllib.request
from pathlib import Path
MANIFEST=Path(__file__).with_name('laya-model.json')
def validate(root):
    m=json.loads(MANIFEST.read_text())
    if m['revision']!='1c5edc17a7acd8701df6fc341c0d179f1c62c982':raise ValueError('model_revision')
    for e in m['files']:
        p=root/e['path']
        if p.is_symlink() or not p.is_file() or p.stat().st_size!=e['size']:raise ValueError('model_file_identity')
        h=hashlib.sha256()
        with p.open('rb') as f:
            while b:=f.read(1024*1024):h.update(b)
        if h.hexdigest()!=e['sha256']:raise ValueError('model_content_identity')
    return root
if __name__=='__main__':
    root=Path(sys.argv[1]);m=json.loads(MANIFEST.read_text());root.mkdir(parents=True,exist_ok=True)
    for e in m['files']:
        p=root/e['path'];p.parent.mkdir(parents=True,exist_ok=True)
        u=f"https://huggingface.co/{m['repository']}/resolve/{m['revision']}/{e['path']}"
        count=0
        with urllib.request.urlopen(u,timeout=120) as src,p.open('xb') as out:
            while b:=src.read(1024*1024):
                count+=len(b)
                if count>e['size']:raise ValueError('model_download_size')
                out.write(b)
        p.chmod(0o444)
    validate(root);print('PINNED_LAYA_MODEL=VERIFIED')
