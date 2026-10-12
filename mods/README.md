# Mods del catalogo

Cada carpeta de aca es un mod que aparece en la pestana **Catalogo** del gestor. Los mods entran por pull request
y se publican cuando alguien los revisa y los aprueba.

## Forma facil: desde el gestor

1. Abri el gestor y anda a **Subir mod**.
2. Inicia sesion con GitHub (es gratis).
3. Elegi tu `.zip`, carpeta, `.dll` o `.bundle`. El gestor ordena los archivos solo y te muestra a donde va cada uno.
4. Completa nombre, version y descripcion y toca **Enviar para revision**.

Con **Probar en mi juego** lo instalas en tu copia antes de enviarlo, y con **Exportar .zip** sacas un paquete
que tambien sirve para subir a Nexus Mods.

## Forma manual

```
mods/
  mi-mod/
    mod.json
    preview.png          (opcional, 16:9, se ve en el catalogo)
    files/               (los archivos con la misma ruta que tendrian dentro de la carpeta del juego)
      BepInEx/plugins/MiMod.dll
      SNB_Data/StreamingAssets/aa/StandaloneWindows64/...
```

`mod.json`:

```json
{
  "id": "mi-mod",
  "name": "Mi mod",
  "version": "1.0.0",
  "author": "tu nombre",
  "description": "Que hace el mod, en una o dos frases.",
  "category": "jugabilidad",
  "gameVersion": ">=1.3.53",
  "preview": "preview.png"
}
```

- `id`: igual al nombre de la carpeta (minusculas, numeros y guiones).
- `category`: `skins` (trajes, capas y animaciones sin habilidades), `habilidades` (armas, ataques y movimiento), `jugabilidad` (reglas y sistemas), `visual` (entorno, iluminación e interfaz), `audio`, `dificultad`, `traduccion`, `herramienta` u `otros`. El valor antiguo `personaje` se sigue leyendo como skins.
- `gameVersion` (opcional): `1.3.53`, `>=1.3.53`, `<1.3.48` o varias condiciones separadas por coma.
- Si el mod tiene archivos dentro de `BepInEx/`, el gestor instala BepInEx 6.0.0-pre.1 solo.

Solo se pueden instalar archivos dentro de `BepInEx/` y `SNB_Data/` (y los de arranque de BepInEx). No se aceptan
ejecutables ni scripts (`.exe`, `.bat`, `.ps1`, `.sh`...), y cada archivo tiene que pesar menos de 95 MB.

Para revisar tu mod antes de mandar el PR:

```
python .github/scripts/build_catalog.py --check
```

## Tipos de mod

- **Reemplazo de bundles**: SANABI carga sus recursos desde archivos `.bundle` (Unity Addressables) y no verifica
  su contenido, asi que un `.bundle` modificado con la misma ruta reemplaza al original. Asi funcionan la
  traduccion y el [traje aracnido](traje-aracnido). Se editan con UnityPy, UABEA o AssetStudio.
- **Plugins BepInEx** (`.dll`): codigo que cambia como se juega. El juego es Unity 2019.4.41 IL2CPP; los mods de
  Nexus usan BepInEx 6.0.0-pre.1 (BepInEx/unhollowed tiene los ensamblados generados despues del primer arranque).

## Reglas

Los mods tienen que respetar las
[reglas de contenido de fans de SANABI](https://store.steampowered.com/news/app/1562700/view/1834602721190057):

- Gratis: nada de pagos, accesos pagos ni promocion de marcas.
- Sin contenido que infrinja derechos de terceros (personajes, logos, musica o sonidos de otras franquicias).
- Sin contenido que pueda confundirse con contenido oficial de SANABI.
- Sin contenido discriminatorio, sexual explicito o violento de forma excesiva.
- Si NEOWIZ o WONDERPOTION piden sacar un mod, se saca.
