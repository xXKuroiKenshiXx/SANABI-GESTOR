# Brazo minigun · Experimental 0.2.0

Plugin de jugabilidad para el héroe de la campaña principal, compilado contra las interfaces IL2CPP de **SANABI 1.3.53 / Unity 2019.4.41f1**. No reemplaza los bundles de las skins.

## Controles

- **2** alterna el gancho y la minigun. Equipar el arma suelta el gancho si estaba conectado. Requiere haber desbloqueado el gancho en la historia.
- Soltá el botón del mouse después de cambiar de modo. **Mouse** apunta y **clic izquierdo sostenido** dispara.
- La minigun acumula calor, se bloquea al llegar al máximo y vuelve a estar disponible al enfriarse. El indicador se muestra abajo a la izquierda.
- Durante pausas, escenas, muerte o control del clon no dispara. Al cambiar de escena o destruirse el jugador se restaura el gancho.

Balas visibles con recorrido: viajan a 36 unidades/s y comprueban colisiones a lo largo de cada tramo para evitar atravesar paredes entre cuadros. Cada proyectil desaparece al impactar o agotar el alcance. Respetan colliders propios, paredes e invulnerabilidad.

El daño predeterminado es **0,04 HP por impacto**: hacen falta **25 impactos al mismo enemigo para quitar 1 HP**, frente a 1 HP por bala en 0.1.0. El juego acepta daño entero, por lo que los impactos se acumulan por enemigo y solo se envía `AttackType.PlayerBullet` cuando suman un punto completo; no se redondea cada bala. A 12 disparos/s serían 0,48 HP/s antes de descontar el calor, los fallos y el tiempo de vuelo. El daño parcial caduca tras 6 segundos sin impactos y al cambiar de escena. No se cambia el controlador ni la vida del jugador.

Incluye pixel art original del arma, cuatro cuadros de giro de cañones, fogonazo, trazadoras y sonido de disparo sintetizado en el plugin.

## Instalación

Instalar desde el gestor, o copiar `SanabiMinigun.dll` a `BepInEx/plugins/`. Usar **BepInEx Unity IL2CPP x64 6.0.0-pre.1**. El primer arranque genera las interfaces y puede demorar.

En Linux, el juego Windows corre con Proton. Agregar a Steam → SANABI → Propiedades → Opciones de lanzamiento:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

Configuración generada en `BepInEx/config/kuro.sanabi.minigun.cfg`: `ShotsPerSecond`, `DamagePerBullet` (0,005–0,1), `Range` y `BulletSpeed`. El valor antiguo `Damage` ya no se usa para evitar conservar el daño excesivo al actualizar. Desinstalar o desactivar la DLL desde el gestor restaura el comportamiento normal del gancho.

## Estado real de las pruebas

DLL compilada sin errores, interfaces extraídas del juego instalado, y pruebas del cambio de modo, pausa, cadencia, calor y acumulación de 25 impactos para 1 HP realizadas con el mismo código C# del plugin. **Pendiente: ejecutar combate en el juego para comprobar proyectiles, daño, capas y restauración en cambios de escena.** Se publica como experimental y no lleva check de mod verificado.

Si una actualización invalida una interfaz, el plugin registra el error en `BepInEx/LogOutput.txt` y trata de devolver el gancho. No se ha instalado automáticamente en tu partida.

## Compilar

`fuente/SanabiMinigun.csproj` usa `InteropDir` (interfaces generadas para la versión del juego) y `BepCore` (DLL de BepInEx pre.1). No distribuir las DLL de Unity ni las interfaces del juego: solo el plugin compilado.

```
dotnet build fuente/SanabiMinigun.csproj -c Release -p:InteropDir=RUTA_INTERFACES -p:BepCore=RUTA_BEPINEX_CORE
```
