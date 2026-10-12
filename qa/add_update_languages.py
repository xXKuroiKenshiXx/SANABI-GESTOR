import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'gestor/Assets/Languages'
# App UI translations; authors' release notes and mod descriptions keep their source language.
rows=[
('Actualizaciones','Updates','更新','Обновления'),
('Actualizaciones · 1','Updates · 1','更新 · 1','Обновления · 1'),
('Personajes','Characters','角色','Персонажи'),
('Personajes: reemplazan al jugador con su controlador, animaciones y habilidades. Las skins solo cambian su apariencia.','Characters: replace the player with their controller, animations and abilities. Skins only change appearance.','角色：替换玩家及其控制器、动画和能力。外观只改变样貌。','Персонажи: заменяют игрока вместе с контроллером, анимациями и способностями. Облики меняют только внешность.'),
('Experimentales · Personajes y habilidades','Experimental · Characters and abilities','实验性 · 角色和能力','Экспериментальные · Персонажи и способности'),
('Mods de la comunidad','Community mods','社区模组','Моды сообщества'),
('Encontrá los mods compartidos en Discord. El catálogo se actualiza automáticamente; no necesitás configurar nada.','Find mods shared on Discord. The catalog updates automatically; no setup is needed.','查看在Discord分享的模组。目录自动更新，无需设置。','Находите моды, опубликованные в Discord. Каталог обновляется автоматически, настройка не нужна.'),
('Actualizar catálogo','Refresh catalog','刷新目录','Обновить каталог'),
('Configuración avanzada de comunidad','Advanced community settings','社区高级设置','Расширенные настройки сообщества'),
('Dirección del catálogo de comunidad','Community catalog address','社区目录地址','Адрес каталога сообщества'),
('Skins','Skins','外观','Облики'),
('Habilidades','Abilities','能力','Способности'),
('Entorno y efectos','Environment and effects','环境和特效','Окружение и эффекты'),
('Todas las categorías','All categories','所有分类','Все категории'),
('Notificaciones','Notifications','通知','Уведомления'),
('Notificaciones · 1','Notifications · 1','通知 · 1','Уведомления · 1'),
('Experimentales · Habilidades','Experimental · Abilities','实验性 · 能力','Экспериментальные · Способности'),
('Mods de jugabilidad en desarrollo. La ficha indica controles, compatibilidad y pruebas pendientes.','Gameplay mods in development. Details list controls, compatibility and pending tests.','开发中的玩法模组。详情列出操作、兼容性和待完成的测试。','Моды игрового процесса в разработке. В карточке указаны управление, совместимость и необходимые проверки.'),
('Skins cambian la apariencia; habilidades cambian ataques y movimiento; entorno y efectos cambia escenarios e interfaz.','Skins change appearance; abilities change attacks and movement; environment and effects change scenes and UI.','外观改变角色样貌；能力改变攻击和移动；环境和特效改变场景与界面。','Облики меняют внешность; способности — атаки и движение; окружение и эффекты — сцены и интерфейс.'),
('Skins: apariencia del personaje, trajes, capas y animaciones. No añaden habilidades.','Skins: character appearance, costumes, capes and animations. They do not add abilities.','外观：角色样貌、服装、披风和动画。不添加能力。','Облики: внешность персонажа, костюмы, плащи и анимации. Не добавляют способностей.'),
('Habilidades: ataques, armas y movimiento que modifican cómo se juega. Pueden combinarse con skins compatibles.','Abilities: attacks, weapons and movement that change gameplay. Can be combined with compatible skins.','能力：改变玩法的攻击、武器和移动。可与兼容外观搭配。','Способности: атаки, оружие и движение, меняющие игровой процесс. Можно сочетать с совместимыми обликами.'),
('Entorno y efectos: escenarios, iluminación, partículas e interfaz del juego.','Environment and effects: scenes, lighting, particles and game UI.','环境和特效：场景、光照、粒子及游戏界面。','Окружение и эффекты: сцены, освещение, частицы и интерфейс игры.'),
('Jugabilidad: cambios en reglas, controles y sistemas del juego.','Gameplay: changes to game rules, controls and systems.','玩法：修改游戏规则、操作和系统。','Игровой процесс: изменения правил, управления и игровых систем.'),
('Traducción: textos e idiomas del juego mediante archivos de una edición compatible.','Translation: game text and languages using files from a compatible edition.','翻译：使用兼容版本的文件更改游戏文本和语言。','Перевод: тексты и языки игры из файлов совместимой редакции.'),
('Audio: música, voces y efectos de sonido.','Audio: music, voices and sound effects.','音频：音乐、语音和音效。','Аудио: музыка, голоса и звуковые эффекты.'),
('Otras modificaciones del juego.','Other game modifications.','其他游戏修改。','Другие изменения игры.'),
('Comprobar actualizaciones','Check for updates','检查更新','Проверить обновления'),
('Ver actualización','View update','查看更新','Посмотреть обновление'),
('El gestor comprueba las releases al abrirse y cada 6 horas. La instalación empieza cuando elegís actualizar.','The manager checks releases at startup and every 6 hours. Installation starts when you choose to update.','管理器在启动时及每6小时检查发布版本。选择更新后才开始安装。','Менеджер проверяет релизы при запуске и каждые 6 часов. Установка начинается, когда вы выбираете обновление.'),
('Nueva versión del gestor: v{0}','New manager version: v{0}','管理器新版本：v{0}','Новая версия менеджера: v{0}'),
('El gestor está actualizado','The manager is up to date','管理器已是最新版本','Менеджер обновлён'),
('Última comprobación: {0}','Last checked: {0}','上次检查：{0}','Последняя проверка: {0}'),
('Actualizar y reiniciar','Update and restart','更新并重启','Обновить и перезапустить'),
('Abrir release','Open release','打开发布页面','Открыть релиз'),
('Actualizar el gestor','Update the manager','更新管理器','Обновить менеджер'),
('Se descargará la nueva versión y el gestor se reiniciará. Tus ajustes y mods instalados se conservan.','The new version will download and the manager will restart. Your settings and installed mods are preserved.','将下载新版本并重启管理器。设置和已安装的模组会保留。','Новая версия будет загружена, а менеджер перезапущен. Настройки и установленные моды сохранятся.'),
('Descargando y verificando el gestor…','Downloading and verifying the manager…','正在下载并验证管理器…','Загрузка и проверка менеджера…'),
('No se pudo actualizar','Update failed','更新失败','Не удалось обновить'),
('No se pudo comprobar la actualización. Intentá nuevamente.','Could not check for updates. Try again.','无法检查更新。请重试。','Не удалось проверить обновление. Попробуйте снова.'),
('No se pudo comprobar la actualización. {0}','Could not check for updates. {0}','无法检查更新。{0}','Не удалось проверить обновление. {0}'),
('Las actualizaciones integradas requieren el EXE portable o AppImage. Esta carpeta de desarrollo se actualiza manualmente.','In-app updates require the portable EXE or AppImage. Update this development folder manually.','应用内更新需要便携EXE或AppImage。此开发文件夹需手动更新。','Для встроенных обновлений нужен портативный EXE или AppImage. Эта папка разработки обновляется вручную.'),
('Esta release todavía no tiene un ejecutable con SHA-256 para tu plataforma. Podés abrir la release.','This release does not yet have an executable with SHA-256 for your platform. You can open the release.','此版本尚无适用于您平台且附带SHA-256的可执行文件。可打开发布页面。','В этом релизе пока нет исполняемого файла с SHA-256 для вашей платформы. Можно открыть страницу релиза.'),
]
for code,column in [('es',0),('en',1),('zh',2),('ru',3)]:
    path=ROOT/(code+'.json');data=json.loads(path.read_text(encoding='utf-8'))
    for row in rows:data[row[0]]=row[column]
    for key,name in [('personajes','Personajes'),('skins','Skins'),('habilidades','Habilidades'),('visual','Entorno y efectos'),('personaje','Skins'),('jugabilidad','Jugabilidad'),('audio','Audio'),('dificultad','Dificultad'),('traduccion','Traduccion'),('herramienta','Herramienta'),('otros','Otros')]:data[key]=data[name]
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Update/category UI translated in es/en/zh/ru.')
