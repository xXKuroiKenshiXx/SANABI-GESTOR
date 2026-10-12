"""Build the three local Spider-Man variants, catalog, packages and visual QA.

Usage: python build_collection.py "<SANABI folder>"
"""
import json
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SOURCE = Path(__file__).parent
VARIANTS = [
    ('traje-aracnido', 'classic', 'Spider-Man', '1.2.0',
     'Traje clásico rojo y azul, máscara de ojos blancos y emblema de araña. Sin sombrero ni capa. Gancho de telaraña y sonidos sintetizados.'),
    ('spider-noir', 'noir', 'Spider Noir', '1.1.0',
     'Máscara negra con lentes blancos, sombrero y gabardina oscura. Telaraña blanca y sonidos sintetizados del gancho.'),
    ('spider-2099', '2099', 'Spider-Man 2099 · Capa roja', '1.1.0',
     'Traje azul oscuro, máscara y emblema con detalles rojos, y capa roja animada. Telaraña blanca y sonidos sintetizados del gancho.'),
]

for slug, variant, name, version, description in VARIANTS:
    folder = ROOT/'mods'/slug
    folder.mkdir(parents=True, exist_ok=True)
    subprocess.run([sys.executable, str(SOURCE/'build.py'), sys.argv[1], str(folder/'files'), variant, '--keep-audio'], check=True)
    manifest = dict(id=slug, name=name, version=version, author='Kuro Kenshi',
                    description=description, category='personaje', tags=['skin', 'telaraña', 'audio', variant],
                    gameVersion='1.3.53', preview='preview.png', verification='package',
                    installInstructions='Cerrá SANABI y pulsá Instalar. El mod se instala y activa automáticamente. Para volver al diseño original, desactivalo desde Mis mods. El gestor conserva el respaldo y muestra los conflictos.',
                    compatibilityNotes='Campaña principal. Máscara y brazo redibujados; malla de render ampliada para evitar recortes. Elegí una sola skin o capa del héroe a la vez. Conserva los sonidos de telaraña de la versión anterior. Prueba dentro del juego pendiente.')
    (folder/'mod.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    (folder/'README.md').write_text(
        f'# {name}\n\n{description}\n\n'
        'Mod local de fans. La marca del catálogo indica revisión del paquete dentro del gestor; no certificación oficial.\n\n'
        'Hecho a partir de SANABI 1.3.53. Instalar solo una de las tres variantes a la vez: reemplazan los mismos bundles. '
        'El gestor detecta el conflicto y permite desactivar la anterior.\n\n'
        'Regenerar las tres variantes con `python mods/traje-aracnido/fuente/build_collection.py "<carpeta de SANABI>"` '
        '(UnityPy, Pillow y NumPy). Las dimensiones y los pivotes originales se conservan. '
        'Los sonidos del gancho se sintetizan desde cero.\n\n'
        'Los sprites se comprueban en hojas de contacto y los bundles se vuelven a abrir tras generarse. '
        'La comprobación de juego en movimiento sigue pendiente.\n', encoding='utf-8')
    packages = ROOT/'dist'/'mods'
    packages.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(packages/f'{slug}-{version}.zip', 'w', zipfile.ZIP_DEFLATED) as z:
        for path in sorted((folder/'files').rglob('*')):
            if path.is_file():
                z.write(path, path.relative_to(folder/'files').as_posix())
        for name in ('mod.json', 'README.md', 'preview.png'):
            z.write(folder/name, name)

subprocess.run([sys.executable, str(ROOT/'.github/scripts/build_catalog.py')], check=True)
