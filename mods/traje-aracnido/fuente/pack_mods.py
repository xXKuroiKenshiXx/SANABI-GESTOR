"""Repackage generated local file-replacement mods without rebuilding Unity assets."""
import json
import zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[3]

def pack(folder):
    manifest=json.loads((folder/'mod.json').read_text(encoding='utf-8'))
    dest=ROOT/'dist/mods'/f"{manifest['id']}-{manifest['version']}.zip"
    dest.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(dest,'w',zipfile.ZIP_DEFLATED) as z:
        for path in sorted((folder/'files').rglob('*')):
            if path.is_file():z.write(path,path.relative_to(folder/'files').as_posix())
        for name in ('mod.json','README.md','preview.png'):
            if (folder/name).is_file():z.write(folder/name,name)
    return dest

if __name__=='__main__':
    for folder in sorted((ROOT/'mods').iterdir()):
        if (folder/'mod.json').is_file() and (folder/'files').is_dir():print(pack(folder).name)
