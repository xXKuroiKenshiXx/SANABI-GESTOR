from pathlib import Path
import json,re
root=Path(__file__).resolve().parents[1]
# Each row is source | English | Simplified Chinese | Russian.
rows='''1. Cuenta de GitHub|1. GitHub account|1. GitHub 账户|1. Аккаунт GitHub
2. Archivos del mod|2. Mod files|2. 模组文件|2. Файлы мода
3. Datos|3. Details|3. 详细信息|3. Сведения
4. Enviar|4. Submit|4. 提交|4. Отправка
API key de Nexus|Nexus API key|Nexus API 密钥|API-ключ Nexus
Abrir|Open|打开|Открыть
Abrir Discord|Open Discord|打开 Discord|Открыть Discord
Abrir carpeta|Open folder|打开文件夹|Открыть папку
Abrir en Nexus|Open on Nexus|在 Nexus 中打开|Открыть на Nexus
Abrir fuente|Open source page|打开来源页面|Открыть источник
Aceptar|OK|确定|ОК
Acerca de|About|关于|О программе
Activa, desactiva o quita mods. Al desactivar uno, el juego vuelve a sus archivos originales.|Enable, disable or remove mods. Disabling a mod restores the original game files.|启用、禁用或移除模组。禁用模组后会恢复游戏的原始文件。|Включайте, отключайте и удаляйте моды. При отключении восстанавливаются оригинальные файлы игры.
Actualizar|Refresh|刷新|Обновить
Ajustes|Settings|设置|Настройки
Apariencia e idioma|Appearance and language|外观和语言|Внешний вид и язык
Archivo local|Local file|本地文件|Локальный файл
Archivos|Files|文件|Файлы
Archivos que instala|Installed files|安装的文件|Устанавливаемые файлы
Audio|Audio|音频|Звук
Autor|Author|作者|Автор
BepInEx (cargador de plugins)|BepInEx (plugin loader)|BepInEx（插件加载器）|BepInEx (загрузчик плагинов)
Busca en Mods o en Nexus Mods. Tambien podes arrastrar un .zip, .7z, .rar o .dll a esta ventana.|Browse Mods or Nexus Mods. You can also drop a .zip, .7z, .rar or .dll into this window.|浏览模组或 Nexus Mods，也可以将 .zip、.7z、.rar 或 .dll 文件拖入此窗口。|Найдите моды в каталоге или на Nexus Mods. Можно перетащить сюда файл .zip, .7z, .rar или .dll.
Buscar en GitHub|Search GitHub|搜索 GitHub|Искать на GitHub
Buscar en Steam|Find on Steam|在 Steam 中查找|Найти в Steam
Buscar en los mods de SANABI de Nexus...|Search SANABI mods on Nexus…|搜索 Nexus 上的 SANABI 模组…|Поиск модов SANABI на Nexus…
Buscar mods (filtra la lista; Buscar en GitHub busca repos nuevos)...|Filter mods; Search GitHub discovers new repositories…|筛选模组；搜索 GitHub 可发现新仓库…|Фильтр модов; поиск на GitHub находит новые репозитории…
Canal: 1558909352146895030|Channel: 1558909352146895030|频道：1558909352146895030|Канал: 1558909352146895030
Cancelar|Cancel|取消|Отмена
Cargando documentación…|Loading documentation…|正在加载文档…|Загрузка документации…
Carpeta de datos|Data folder|数据文件夹|Папка данных
Carpeta del juego|Game folder|游戏文件夹|Папка игры
Catalogo|Catalog|目录|Каталог
Categoria|Category|类别|Категория
Catálogo automático|Automatic catalog|自动目录|Автоматический каталог
Catálogo de Discord actualizado.|Discord catalog updated.|Discord 目录已更新。|Каталог Discord обновлён.
Cerrar sesion|Sign out|退出登录|Выйти
Compatibilidad y comprobaciones|Compatibility and checks|兼容性和检查|Совместимость и проверки
Comunidad (sin chequear)|Community (unverified)|社区（未验证）|Сообщество (не проверено)
Comunidad Discord|Discord community|Discord 社区|Сообщество Discord
Conectar con Nexus|Connect to Nexus|连接 Nexus|Подключить Nexus
Consola|Console|控制台|Консоль
Copia tu API key personal desde Nexus (abajo de todo en la pagina) y pegala aca. Se guarda solo en esta carpeta.|Copy your personal API key from the bottom of the Nexus page and paste it here. It is stored only in this folder.|从 Nexus 页面底部复制个人 API 密钥并粘贴到此处。密钥只保存在此文件夹中。|Скопируйте личный API-ключ внизу страницы Nexus и вставьте сюда. Он хранится только в этой папке.
Copiar|Copy|复制|Копировать
Crea un token con permiso public_repo (un clic) y pegalo aca. Si no tenes cuenta de GitHub, crearla es gratis.|Create a token with public_repo permission and paste it here. GitHub accounts are free.|创建具有 public_repo 权限的令牌并粘贴到此处。GitHub 账户可以免费注册。|Создайте токен с разрешением public_repo и вставьте сюда. Регистрация на GitHub бесплатна.
Crear token|Create token|创建令牌|Создать токен
Cuenta de Nexus Mods|Nexus Mods account|Nexus Mods 账户|Аккаунт Nexus Mods
Datos del programa: |App data: |程序数据：|Данные программы:
Dejar el juego original|Restore original game|恢复原始游戏|Восстановить оригинал
Desactiva todos los mods y quita BepInEx. Los mods quedan guardados para volver a activarlos.|Disables all mods and removes BepInEx. Mods remain saved for later use.|禁用所有模组并移除 BepInEx。模组仍然保留，可稍后重新启用。|Отключает все моды и удаляет BepInEx. Моды сохраняются для повторного включения.
Descargar|Download|下载|Скачать
Desconectar|Disconnect|断开连接|Отключить
Descripcion|Description|描述|Описание
Desinstalar|Uninstall|卸载|Удалить
Dificultad|Difficulty|难度|Сложность
Elegir archivo...|Choose file…|选择文件…|Выбрать файл…
Elegir carpeta...|Choose folder…|选择文件夹…|Выбрать папку…
Elegí la fecha de la traducción y pulsá Instalar. El gestor descarga los .bundle correspondientes del repositorio y los instala con copia de respaldo. No traduce durante el juego.|Choose the translation date and click Install. The manager downloads that edition's .bundle files from the repository and installs them with a backup. It does not translate during gameplay.|选择翻译日期并点击安装。管理器会从仓库下载该版本的 .bundle 文件，并在备份后安装。它不会在游戏过程中实时翻译。|Выберите дату перевода и нажмите «Установить». Менеджер скачает файлы .bundle этой версии из репозитория и установит их с резервной копией. Во время игры перевод не выполняется.
Elegí un catálogo HTTPS para conectar el foro.|Choose an HTTPS catalog to connect the forum.|选择 HTTPS 目录以连接论坛。|Укажите HTTPS-каталог для подключения форума.
Enviar para revision|Submit for review|提交审核|Отправить на проверку
Es de SANABI|SANABI mod|SANABI 模组|Мод SANABI
Exportar .zip|Export .zip|导出 .zip|Экспорт .zip
GESTOR DE MODS|MOD MANAGER|模组管理器|МЕНЕДЖЕР МОДОВ
GitHub Actions puede leer el foro con un bot autorizado y publicar el catálogo. El gestor consulta ese catálogo sin guardar el token del bot. No necesitás mantener un servidor encendido.|GitHub Actions can read the forum using an authorized bot and publish the catalog. The manager reads that catalog without storing the bot token. You do not need to keep a server running.|GitHub Actions 可通过已授权的机器人读取论坛并发布目录。管理器读取目录，不保存机器人令牌。无需保持自己的服务器运行。|GitHub Actions может читать форум через авторизованного бота и публиковать каталог. Менеджер читает каталог без хранения токена бота. Собственный работающий сервер не нужен.
Guardar|Save|保存|Сохранить
Guardar y actualizar|Save and refresh|保存并刷新|Сохранить и обновить
Hay una version nueva del gestor: v|A new manager version is available: v|管理器有新版本：v|Доступна новая версия менеджера: v
Herramienta|Tool|工具|Инструмент
Idioma de la aplicación|App language|应用语言|Язык приложения
Imagen de portada (opcional, PNG)|Cover image (optional, PNG)|封面图片（可选，PNG）|Обложка (необязательно, PNG)
Iniciar sesion con GitHub|Sign in with GitHub|使用 GitHub 登录|Войти через GitHub
Instalado|Installed|已安装|Установлено
Instalar|Install|安装|Установить
Instalar BepInEx|Install BepInEx|安装 BepInEx|Установить BepInEx
Instalar carpeta...|Install from folder…|从文件夹安装…|Установить из папки…
Instalar desde archivo...|Install from file…|从文件安装…|Установить из файла…
Juego no seleccionado|No game selected|未选择游戏|Игра не выбрана
Jugabilidad|Gameplay|玩法|Геймплей
Leer las reglas de contenido de fans (NEOWIZ / WONDERPOTION)|Read the fan content guidelines (NEOWIZ / WONDERPOTION)|阅读同人内容准则（NEOWIZ / WONDERPOTION）|Правила фанатского контента (NEOWIZ / WONDERPOTION)
Linux (Proton): pega esto en Steam > SANABI > Propiedades > Opciones de lanzamiento:|Linux (Proton): paste this in Steam > SANABI > Properties > Launch options:|Linux（Proton）：将此内容粘贴到 Steam > SANABI > 属性 > 启动选项：|Linux (Proton): вставьте в Steam > SANABI > Свойства > Параметры запуска:
Listo.|Ready.|就绪。|Готово.
Los botones "Mod Manager Download" de Nexus abren este gestor.|Nexus "Mod Manager Download" buttons open this manager.|Nexus 的“Mod Manager Download”按钮会打开此管理器。|Кнопки «Mod Manager Download» на Nexus открывают этот менеджер.
Los cambios se aplican al instante. El idioma del juego se elige en el mod de traducción.|Changes apply immediately. Select the game language in the translation mod.|更改立即生效。游戏语言在翻译模组中选择。|Изменения применяются сразу. Язык игры выбирается в моде перевода.
Mi mod es gratis, no usa contenido protegido de terceros (personajes, logos o musica de otras marcas) y respeta las reglas de contenido de fans de SANABI.|My mod is free, uses no protected third-party content (characters, logos or music from other brands), and follows SANABI's fan content guidelines.|我的模组免费，不使用受保护的第三方内容（其他品牌的角色、标志或音乐），并遵守 SANABI 同人内容准则。|Мой мод бесплатен, не использует защищённый контент третьих лиц (персонажей, логотипы или музыку других брендов) и соблюдает правила фанатского контента SANABI.
Mis mods|My mods|我的模组|Мои моды
Mods|Mods|模组|Моды
Mods del catálogo y de la comunidad. Abrí una tarjeta para ver archivos, instalación y comprobaciones.|Catalog and community mods. Open a card for files, installation and checks.|目录和社区模组。打开卡片可查看文件、安装方式和检查结果。|Моды из каталога и сообщества. Откройте карточку, чтобы увидеть файлы, установку и проверки.
Mods publicados por la comunidad en el foro de Discord.|Mods shared by the community in the Discord forum.|社区在 Discord 论坛中发布的模组。|Моды, опубликованные сообществом на форуме Discord.
Necesita BepInEx (se instala solo)|Requires BepInEx (installed automatically)|需要 BepInEx（自动安装）|Требуется BepInEx (устанавливается автоматически)
No|No|否|Нет
No hay mods de la comunidad con esa busqueda.|No community mods match this search.|没有符合此搜索的社区模组。|По этому запросу моды сообщества не найдены.
No los probamos nosotros. "Es de SANABI" significa que revisamos que el repo sea un mod de este juego; "Sin revisar" son resultados nuevos de la busqueda.|We have not tested these mods. "SANABI mod" means the repository was checked for relevance; "Unreviewed" means a new search result.|我们尚未测试这些模组。“SANABI 模组”表示已检查仓库与本游戏相关；“未审核”表示新的搜索结果。|Эти моды нами не тестировались. «Мод SANABI» означает проверку назначения репозитория; «Не проверено» — новый результат поиска.
Nombre|Name|名称|Название
Notas para quien revisa (opcional)|Notes for the reviewer (optional)|审核者备注（可选）|Примечания для проверяющего (необязательно)
Obtener key|Get API key|获取 API 密钥|Получить ключ
Otros|Other|其他|Прочее
Pega este codigo en la pagina que se abrio: |Paste this code on the page that opened: |将此代码粘贴到打开的页面：|Вставьте код на открывшейся странице:
Personaje|Character|角色|Персонаж
Pisado por Steam|Overwritten by Steam|已被 Steam 覆盖|Перезаписано Steam
Probar en mi juego|Try in my game|在我的游戏中试用|Попробовать в игре
Quitar BepInEx|Remove BepInEx|移除 BepInEx|Удалить BepInEx
README del mod|Mod README|模组 README|README мода
Reaplicar mods|Reapply mods|重新应用模组|Применить моды снова
Reglas de contenido de fans|Fan content guidelines|同人内容准则|Правила фанатского контента
Reinstalar|Reinstall|重新安装|Переустановить
Repositorio|Repository|仓库|Репозиторий
Restaurar|Restore|恢复|Восстановить
SANABI Gestor es una herramienta hecha por fans, gratis y de codigo abierto. No es oficial ni esta respaldada por WONDERPOTION o NEOWIZ. SANABI y sus personajes pertenecen a sus duenos.|SANABI Gestor is a free, open-source fan tool. It is unofficial and not endorsed by WONDERPOTION or NEOWIZ. SANABI and its characters belong to their owners.|SANABI Gestor 是免费开源的同人工具，非官方，也未获 WONDERPOTION 或 NEOWIZ 认可。SANABI 及其角色属于各自权利人。|SANABI Gestor — бесплатный инструмент фанатов с открытым кодом. Он неофициальный и не поддерживается WONDERPOTION или NEOWIZ. SANABI и персонажи принадлежат правообладателям.
Sesion iniciada como |Signed in as |已登录为 |Вход выполнен:
Sin revisar|Unreviewed|未审核|Не проверено
Steam actualizo o verifico el juego y piso archivos de tus mods. Reaplicalos para volver a tenerlos.|Steam updated or verified the game and overwrote mod files. Reapply the mods to restore them.|Steam 更新或验证游戏时覆盖了模组文件。请重新应用模组以恢复。|Steam обновил или проверил игру и перезаписал файлы модов. Примените моды снова.
Subir mod|Upload mod|上传模组|Загрузить мод
Subir un mod|Upload a mod|上传模组|Загрузка мода
Sí|Yes|是|Да
Tema|Theme|主题|Тема
Todavia no instalaste mods|No mods installed yet|尚未安装模组|Моды ещё не установлены
Traduccion|Translation|翻译|Перевод
Traducción por edición|Translation editions|翻译版本|Версии перевода
Tu mod se envia como pull request al repositorio. Aparece en el Catalogo cuando lo revisan y aprueban.|Your mod is submitted as a repository pull request. It appears in the catalog after review and approval.|模组将以拉取请求提交到仓库，通过审核后会出现在目录中。|Мод отправляется как запрос на включение в репозиторий. Он появится в каталоге после проверки и одобрения.
Un .zip/.7z/.rar, una carpeta, un plugin .dll o un .bundle. El gestor detecta a donde va cada archivo (tambien podes arrastrarlo aca).|A .zip/.7z/.rar, folder, .dll plugin or .bundle. The manager detects where each file belongs (you can also drop it here).|支持 .zip/.7z/.rar、文件夹、.dll 插件或 .bundle。管理器会识别每个文件的安装位置，也支持拖放。|Архив .zip/.7z/.rar, папка, плагин .dll или .bundle. Менеджер определяет место установки файлов; их можно перетащить сюда.
Usar este gestor para los enlaces de Nexus|Use this manager for Nexus links|使用此管理器打开 Nexus 链接|Использовать для ссылок Nexus
Ver archivos|View files|查看文件|Посмотреть файлы
Ver archivos en Nexus|View files on Nexus|在 Nexus 中查看文件|Файлы на Nexus
Ver como usarlo|Usage instructions|使用说明|Инструкция
Verificados · Catálogo|Verified · Catalog|已验证 · 目录|Проверено · Каталог
Version|Version|版本|Версия
Version del juego (ej. >=1.3.53)|Game version (e.g. >=1.3.53)|游戏版本（例如 >=1.3.53）|Версия игры (например >=1.3.53)
Versión de la traducción|Translation version|翻译版本|Версия перевода
Visual|Visuals|视觉|Визуальные эффекты
← Volver a la lista|Back to list|返回列表|Назад к списку
▶  Jugar SANABI|Play SANABI|启动 SANABI|Играть в SANABI
Sin elegir|Not selected|未选择|Не выбрано'''
base=json.loads((root/'gestor/Assets/Languages/es.json').read_text('utf-8'))
translations=[{}, {}, {}]
for row in rows.splitlines():
    parts=row.split('|')
    assert len(parts)==4,row
    for i in range(3): translations[i][parts[0]]=parts[i+1]
for i,value in enumerate(('Mod version','模组版本','Версия мода')): translations[i]['Version del mod']=value
neutral={' · ','...','BepInEx','Nexus Mods','https://…/discord-catalog.json','ghp_...'}
missing=set(base)-set(translations[0])-neutral
assert not missing,missing
for i,lang in enumerate(('en','zh','ru')):
    data={k:translations[i].get(k,k) for k in base}
    (root/f'gestor/Assets/Languages/{lang}.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),'utf-8')
p=root/'gestor/Views/MainWindow.axaml'
s=p.read_text('utf-8'); s=re.sub(r'\{loc:Tr ([^}]+)\}',r"{loc:Tr '\1'}",s);p.write_text(s,'utf-8')
p=root/'gestor/SanabiGestor.csproj';s=p.read_text('utf-8').replace('<Version>0.3.0</Version>','<Version>0.4.0</Version>');p.write_text(s,'utf-8')
print(f'{len(base)} UI strings, es/en/zh/ru')
