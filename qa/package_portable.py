"""Build a Linux portable archive with native runtime and local catalog assets.

The archive works offline, including all translation editions. Original source
directories, old installers and game executables are not bundled.
"""
import json,tarfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
catalog=json.loads((ROOT/'catalog.json').read_text(encoding='utf-8'))
runtime=ROOT/'dist/linux'
(runtime/'LEEME.txt').write_text('''SANABI Gestor 0.7.0 — Linux x64

Extraé toda la carpeta. Desde una terminal en esa carpeta:
  chmod +x SanabiGestor
  ./SanabiGestor

Incluye el runtime .NET y el catálogo local: no necesita Python.
Elegí la instalación de SANABI de Steam/Proton (SNB.exe + SNB_Data).
Las traducciones históricas anteriores a 1.3.48 no incluyen el DLC moderno.
La traducción instala los .bundle de la edición fechada; no traduce en tiempo real.
Tema claro/oscuro e interfaz en español, inglés, chino simplificado y ruso.
Otros idiomas pueden importarse desde Ajustes con un paquete JSON.
Discord es un enlace a la comunidad; instalá sus paquetes mediante arrastrar o Instalar carpeta.
Brazo minigun 0.2.0: proyectiles y daño bajo (25 impactos para 1 HP); combate dentro del juego pendiente.
Major Son en campaña y sargento en DLC: intercambios de controlador nativo, separados de las skins y todavía experimentales.
Actualizaciones: comprueba releases al inicio y cada 6 horas.
Actualización integrada disponible en EXE portable y AppImage; esta carpeta se reemplaza manualmente.
Las ediciones y mods pueden usarse sin descargar archivos de GitHub.

El AppImage se comprobó en Linux; los mods del juego requieren SANABI/Proton.
''',encoding='utf-8')
files={'catalog.json','github-mods.json','mods/README.md'}
for entry in catalog['mods']:
    files.add(entry['path']+'/mod.json')
    for key in ('readme','preview'):
        if entry.get(key):files.add(entry[key])
    for group in [entry,*entry.get('variants',[])]:
        files.update(f['src'] for f in group['files'])
out=ROOT/'dist/SanabiGestor-linux-x64-0.7.0.tar.gz'
with tarfile.open(out,'w:gz',compresslevel=6) as archive:
    def mode(info):
        info.mode=0o755 if info.name.endswith('/SanabiGestor') or info.name.endswith('.so') else 0o644
        return info
    for path in sorted(runtime.iterdir()):
        if path.is_file():archive.add(path,arcname='SanabiGestor/'+path.name,filter=mode)
    for relative in sorted(files):archive.add(ROOT/relative,arcname='SanabiGestor/'+relative,filter=mode)
print(out,out.stat().st_size,'bytes;',len(files),'catalog files included')
