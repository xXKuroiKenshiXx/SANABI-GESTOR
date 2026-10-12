# SANABI Gestor

Gestor de mods y traducciones para SANABI, con interfaz en español, inglés, chino simplificado y ruso. Windows y Linux/Steam Proton.

## Descargar y empezar

Descargá el ejecutable de [la última release](https://github.com/xXKuroiKenshiXx/SANABI-GESTOR/releases/latest):

- Windows: `SanabiGestor-windows-x64.exe`.
- Linux: `SanabiGestor-x86_64.AppImage`; habilitá su ejecución con `chmod +x`.

Seleccioná la carpeta del juego. En **Mods** están el catálogo, el bloque **Experimentales** y la comunidad de GitHub. Abrí una tarjeta para ver la descripción, README, archivos, controles y compatibilidad; pulsá **Instalar**. En **Mis mods** aparecen las miniaturas y los controles para activar, desactivar o desinstalar. El gestor guarda los archivos originales y avisa cuando varios mods cambian el mismo archivo.

Podés instalar paquetes de cualquier comunidad arrastrando un ZIP, 7z, RAR, DLL o bundle a la ventana, o con **Instalar carpeta**. No hay integración ni bot de Discord: el botón **Abrir Discord**, en Actualizaciones, lleva a [la comunidad](https://discord.gg/P6yQeuEA8).

La configuración queda en `SanabiGestor-data` junto al ejecutable, o en la carpeta de datos del usuario si no puede escribir allí. No compartas esa carpeta: contiene tus ajustes y, si conectás una cuenta, sus credenciales locales. En Windows se cifran para tu usuario mediante DPAPI; en Linux el archivo de ajustes tiene permisos `0600`.

## Mods y traducciones

- **Skins**: apariencia, trajes y capas. Los intercambios visuales se llaman **Major Song · Skin campaña principal** y **Sargento · Skin DLC**; conservan las habilidades del protagonista de cada campaña.
- **Experimentales**, dentro de Mods: minigun y cambios de personaje con controlador y habilidades nativas. Las fichas detallan las pruebas pendientes y las incompatibilidades. No llevan el check del catálogo revisado.
- **Brazo minigun**: `2` alterna gancho/arma; soltá el clic después de alternar y luego mantené clic izquierdo. Usa proyectiles visibles, calor y 0,04 HP por impacto: 25 impactos para quitar 1 HP. Funciona con el protagonista original, no con Major Song ni clones.
- **Spider-Man**: skins originales y dos mods adicionales de cuerpo/animaciones adaptadas. Los nuevos sprites conservan el tamaño, pivotes y geometría del juego; la capa de 2099 aparece también en reposo. Spider Noir conserva sus assets.
- **Traducción**: elegí una de las cinco ediciones fechadas. El gestor descarga los `.bundle` de esa versión, verifica hashes y los instala con respaldo. No traduce en tiempo real. Los archivos históricos y recuperación están en `mods/traduccion-latam-fan/versiones`; las ediciones anteriores al DLC advierten su incompatibilidad.

Las skins de la campaña principal modifican el mismo bundle: elegí una por vez. Las DLL experimentales están compiladas para SANABI 1.3.53; su estado de validación figura en cada ficha.

En Linux, los plugins BepInEx necesitan esta opción de lanzamiento en Steam:

```text
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## Comunidad de GitHub

El gestor busca automáticamente repositorios públicos relacionados con mods de SANABI, además de los proyectos ya revisados en `github-mods.json`. No depende de una lista cerrada: los proyectos nuevos pueden aparecer como **Sin revisar**. Los autores pueden añadir el tema `sanabi-mod` y describir la compatibilidad con SANABI para facilitar el descubrimiento. GitHub impone límites de búsqueda y puede tardar en indexar proyectos nuevos.

Las tarjetas muestran el README y permiten instalar releases o archivos compatibles. Los proyectos que necesitan pasos manuales lo indican. También está disponible Nexus Mods y la asociación de enlaces `nxm://`.

## Compartir un mod con este repositorio

En **Subir mod**, elegí los archivos y completá nombre, versión, autor, categoría, descripción y portada. El gestor prepara `mods/<id>/mod.json`, `mods/<id>/files/` y la miniatura. Al enviar, crea una rama en el repositorio o en el fork de tu cuenta y abre un pull request a `main`. El mod entra al catálogo cuando se aprueba y fusiona; GitHub Actions valida rutas y hashes y regenera `catalog.json`.

### Configurar el inicio de sesión (administrador)

Para el botón de autorización en el navegador, registrá una OAuth App de GitHub y habilitá **Device flow**. Guardá únicamente su **Client ID público** en la variable de repositorio `GITHUB_CLIENT_ID`. El workflow lo incorpora a los builds. Ese identificador no concede acceso a una cuenta; cada usuario aprueba su propio acceso. No se necesita servidor ni `client_secret`. Mientras esa variable no esté configurada, queda la conexión avanzada mediante un token personal; no se presenta el login por navegador como disponible.

Para builds locales: `dotnet publish gestor -p:GitHubClientId=<ID_PUBLICO>`; para desarrollo también sirve `SANABI_GESTOR_GITHUB_CLIENT_ID`.

Guía de paquetes: [mods/README.md](mods/README.md). [Autorización de GitHub](https://docs.github.com/en/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps#device-flow).

## Actualizaciones del gestor

**Actualizaciones** consulta `https://api.github.com/repos/xXKuroiKenshiXx/SANABI-GESTOR/releases/latest` al abrir y cada seis horas. También podés pulsar **Comprobar actualizaciones**. Cuando hay una versión nueva, **Actualizar y reiniciar** descarga el EXE o AppImage correspondiente, verifica el SHA-256 de GitHub, reemplaza el ejecutable y vuelve a abrir el programa. Conserva configuración, mods y una copia del ejecutable anterior para recuperar una actualización fallida.

La instalación empieza al elegir actualizar. No basta con un push a `main`: debe existir una release estable con los nombres de archivo anteriores. Los archivos Linux `.tar.gz` y las carpetas de desarrollo se reemplazan manualmente.

## Desarrollo y publicación

Requiere .NET SDK 10:

```sh
dotnet run --project gestor
dotnet publish gestor -c Release -r win-x64 -o dist/win
bash gestor/packaging/linux/build-appimage.sh
python .github/scripts/build_catalog.py --check
dotnet run --project qa/GestorSmoke -- "<carpeta de este repositorio>"
```

El workflow de release compila Windows y Linux al subir un tag `v*` y publica los ejecutables. Los ZIP de mods se pueden regenerar con `mods/traje-aracnido/fuente/pack_mods.py`; los assets ya están en `mods/` y el usuario no necesita Python.

Las pruebas cubren navegación, fichas, traducciones fechadas, categorías, miniaturas, descubrimiento y envío simulado a GitHub, instalación/restauración aislada y actualización con hashes/rollback. No sustituyen probar cada mod dentro de SANABI.

Proyecto de fans, gratuito y no oficial. SANABI y sus personajes pertenecen a sus titulares.
