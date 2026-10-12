import hashlib,html,json,re,zipfile
from pathlib import Path
root=Path(__file__).resolve().parents[1]
catalog=json.loads((root/'catalog.json').read_text(encoding='utf-8'))
total=0
for entry in catalog['mods']:
    for group in [entry]+entry.get('variants',[]):
        with zipfile.ZipFile(root/'dist/mods'/f"{entry['id']}-{group['version']}.zip") as archive:
            assert json.loads(archive.read('mod.json'))['version']==group['version']
            for file in group['files']:
                expected=file['sha256']
                assert hashlib.sha256((root/file['src']).read_bytes()).hexdigest()==expected,(entry['id'],file['src'])
                assert hashlib.sha256(archive.read(file['dst'])).hexdigest()==expected,(entry['id'],file['dst'])
                total+=1
packs={p.stem:json.loads(p.read_text(encoding='utf-8')) for p in (root/'gestor/Assets/Languages').glob('*.json')}
keys=set(packs['es']);assert all(set(p)==keys for p in packs.values())
source=(root/'gestor/Views/MainWindow.axaml').read_text(encoding='utf-8')
assert {html.unescape(k) for k in re.findall(r"\{loc:Tr '([^']+)'\}",source)}<=keys
assert 'GitHub Actions puede leer el foro con un bot' not in source
assert hashlib.sha256((root/'gestor/Assets/icon.ico').read_bytes()).hexdigest()=='66b64701b650ab78823fe4d6cc3856126c21f5d999dc5472c74eef00e828071a'
print('PASS',len(catalog['mods']),'mods;',total,'catalog/ZIP payload hashes;',len(keys),'UI keys in es/en/zh/ru; exact user icon; simple community UI.')
