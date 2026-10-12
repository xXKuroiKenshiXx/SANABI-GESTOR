using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanabiGestor.Core;

namespace SanabiGestor.ViewModels;

public interface IUiHost
{
    Task<string?> PickFolderAsync(string title);
    Task<string?> PickFileAsync(string title, string[] patterns);
    Task<string?> SaveFileAsync(string title, string suggestedName);
    Task CopyAsync(string text);
    void BringToFront();
}

public partial class MainViewModel : ObservableObject
{
    readonly IUiHost ui;
    readonly HttpClient http;
    readonly Settings settings = Settings.Load();
    readonly CatalogClient catalogClient;
    readonly NexusClient nexus;
    readonly GitHubClient github;
    readonly CancellationTokenSource lifetime = new();
    List<NexusModViewModel> allNexus = new();
    ModStore? store;

    public MainViewModel(IUiHost ui, HttpClient? httpClient = null)
    {
        this.ui = ui;
        http = httpClient ?? Downloader.CreateClient();
        InitializePreferences();
        SelectedCategory = ModCategories[0];
        catalogClient = new CatalogClient(http);
        nexus = new NexusClient(http) { ApiKey = settings.NexusApiKey };
        github = new GitHubClient(http) { Token = settings.GitHubToken };
        Directory.CreateDirectory(Settings.CacheDir);
        Downloader.CleanTemp();
    }

    // ---------------- Estado general ----------------

    public string AppVersion => "v" + AppInfo.Version;
    public string DataDir => Settings.DataDir;
    public bool IsLinux => !OperatingSystem.IsWindows();
    public string LinuxLaunchOptions => "WINEDLLOVERRIDES=\"winhttp=n,b\" %command%";
    public bool DeviceFlowAvailable => GitHubClient.DeviceFlowAvailable;
    public bool NexusSsoAvailable => !string.IsNullOrEmpty(AppInfo.NexusAppSlug);

    [ObservableProperty] int section;
    [ObservableProperty] string? gamePath;
    [ObservableProperty] string gameStatus = "Juego no seleccionado";
    [ObservableProperty] bool hasGame;
    [ObservableProperty] string status = "Listo.";
    [ObservableProperty] double progress;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool showConsole;
    [ObservableProperty] DialogViewModel? dialog;
    [ObservableProperty] bool needsReapply;
    [ObservableProperty] string? updateAvailable;
    [ObservableProperty] bool bepInExInstalled;
    [ObservableProperty] string bepInExStatus = "";
    [ObservableProperty] bool nxmRegistered;
    [ObservableProperty] ModCardViewModel? selectedMod;

    public ObservableCollection<string> ConsoleLines { get; } = new();
    public ObservableCollection<InstalledModViewModel> Installed { get; } = new();
    public ObservableCollection<ModCardViewModel> CheckedMods { get; } = new();
    public ObservableCollection<ModCardViewModel> CommunityMods { get; } = new();
    public ObservableCollection<ModCardViewModel> ExperimentalMods { get; } = new();
    public ObservableCollection<NexusModViewModel> NexusMods { get; } = new();

    const int SectionInstalled = 0, SectionMods = 1, SectionUpload = 3, SectionSettings = 4;
    public bool IsInstalledSection => Section == SectionInstalled && SelectedMod == null;
    public bool IsModsSection => Section == SectionMods && SelectedMod == null;
    public bool IsNexusSection => Section == 2 && SelectedMod == null;
    public bool IsModDetailsSection => SelectedMod != null;
    public bool IsUploadSection => Section == SectionUpload && SelectedMod == null;
    public bool IsSettingsSection => Section == SectionSettings && SelectedMod == null;
    public bool HasNoInstalled => Installed.Count == 0;

    partial void OnSectionChanged(int value)
    {
        SelectedMod = null;
        OnPropertyChanged(nameof(IsInstalledSection));
        OnPropertyChanged(nameof(IsModsSection));
        OnPropertyChanged(nameof(IsNexusSection));
        OnPropertyChanged(nameof(IsUploadSection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(IsNotificationsSection));
        if (value == 5) MarkReleaseRead();
        if (value == SectionMods && allCards.Count == 0 && !ModsLoading) _ = LoadModsAsync();
        if (value == 2 && allNexus.Count == 0) _ = LoadNexusAsync();
    }

    partial void OnSelectedModChanged(ModCardViewModel? value)
    {
        OnPropertyChanged(nameof(IsInstalledSection));
        OnPropertyChanged(nameof(IsModsSection));
        OnPropertyChanged(nameof(IsNexusSection));
        OnPropertyChanged(nameof(IsModDetailsSection));
        OnPropertyChanged(nameof(IsSettingsSection));
        OnPropertyChanged(nameof(IsUploadSection));
        OnPropertyChanged(nameof(IsNotificationsSection));
    }

    public void ShowModDetails(ModCardViewModel card)
    {
        SelectedMod = card;
        _ = LoadReadmeAsync(card);
    }

    async Task LoadReadmeAsync(ModCardViewModel card)
    {
        if (card.ReadmeLoading || card.ReadmeText.Length > 0) return;
        card.ReadmeLoading = true;
        try
        {
            card.ReadmeText = card.Repo is { } repo ? await github.ReadmeAsync(repo, lifetime.Token) ?? "El repositorio no publicó un README."
                : card.Entry?.Readme is { } path ? await Downloader.GetStringAsync(http, card.Entry.AssetUrl(path), lifetime.Token) : "";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { card.ReadmeText = "No se pudo cargar el README. " + e.Message; }
        finally { card.ReadmeLoading = false; }
    }

    public void ShowInstalledDetails(InstalledModViewModel item)
    {
        var card = allCards.FirstOrDefault(c => c.Id == item.Mod.Id);
        if (card == null && CatalogClient.LocalOrCached()?.Mods.FirstOrDefault(e => e.Id == item.Mod.Id) is { } entry)
        {
            card = new ModCardViewModel(this, entry);
            card.Refresh(store?.State, store?.Game.Version);
            if (!string.IsNullOrEmpty(entry.Preview)) _ = LoadImageAsync(AppInfo.RawUrl(entry.Preview), b => card.Preview = b);
        }
        if (card?.HasVariants == true && card.Variants.FirstOrDefault(v => v.Version == item.Mod.Version) is { } installedVersion)
            card.SelectedVariant = installedVersion;
        ShowModDetails(card ?? new ModCardViewModel(this, item.Mod));
    }

    [RelayCommand]
    void CloseModDetails() => SelectedMod = null;

    void Info(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Status = message;
            ConsoleLines.Add($"{DateTime.Now:HH:mm:ss}  {message}");
            while (ConsoleLines.Count > 300) ConsoleLines.RemoveAt(0);
        });
        Log.Write(message);
    }

    public Task<string?> AskAsync(string title, string message, params string[] buttons) => AskHighlightedAsync(title, message, "", buttons);

    async Task<string?> AskHighlightedAsync(string title, string message, string highlight, params string[] buttons)
    {
        var d = new DialogViewModel { Title = Locale.T(title), Message = Locale.T(message), HighlightName = highlight };
        for (var i = 0; i < buttons.Length; i++) d.Buttons.Add(new DialogButton(d, buttons[i], i == 0));
        Dialog = d;
        var result = await d.Result;
        Dialog = null;
        return result;
    }

    Task Alert(string title, string message) => AskAsync(title, message, "Entendido");

    async Task RunBusy(string what, Func<Task> action)
    {
        if (IsBusy || UpdatingManager)
        {
            await Alert("Un momento", "Hay otra operacion en curso. Espera a que termine.");
            return;
        }
        IsBusy = true;
        Progress = 0;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            Info("Cancelado.");
        }
        catch (UnauthorizedAccessException e)
        {
            Info($"Error de permisos: {e.Message}");
            await Alert("Sin permisos", "No se pudo escribir en la carpeta del juego. Cerra el juego y, si sigue fallando, " +
                                        "ejecuta el gestor como administrador.\n\n" + e.Message);
        }
        catch (Exception e)
        {
            Info($"Error al {what}: {e.Message}");
            Log.Write(e.ToString());
            await Alert("Algo salio mal", e.Message);
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    public async Task StartAsync(string? startupLink)
    {
        Platform.ListenForMessages(msg => Dispatcher.UIThread.Post(() => _ = HandleLinkAsync(msg)), lifetime.Token);
        NxmRegistered = Platform.NxmIsOurs();
        var path = settings.GamePath;
        if (!GameInstall.IsValid(path)) path = GameInstall.AutoDetect();
        if (path != null) await SetGameAsync(path, quiet: true);
        else Info("No encontre SANABI automaticamente. Elegi la carpeta del juego en Ajustes.");
        if (startupLink != null) await HandleLinkAsync(startupLink);
        _ = RefreshGitHubAccountAsync();
        StartUpdateNotifications();
    }

    public void Shutdown()
    {
        lifetime.Cancel();
        updateTimer?.Stop();
        Downloader.CleanTemp();
    }

    async Task HandleLinkAsync(string link)
    {
        ui.BringToFront();
        var nxm = NxmLink.Parse(link);
        if (nxm == null)
        {
            Info($"Enlace no reconocido: {link}");
            return;
        }
        if (!nxm.Game.Equals(AppInfo.NexusGameDomain, StringComparison.OrdinalIgnoreCase))
        {
            await Alert("Otro juego", $"Ese enlace es de un mod de \"{nxm.Game}\", no de SANABI.");
            return;
        }
        var file = new NexusFile(nxm.FileId, $"Archivo {nxm.FileId}", "", "MAIN", 0, DateTimeOffset.Now, "", $"nexus-{nxm.ModId}-{nxm.FileId}.zip");
        var mod = allNexus.FirstOrDefault(m => m.Mod.ModId == nxm.ModId);
        try
        {
            var files = await nexus.ListFilesAsync(nxm.ModId);
            file = files.FirstOrDefault(f => f.FileId == nxm.FileId) ?? file;
            if (mod == null)
            {
                var mods = await nexus.ListModsAsync();
                var found = mods.FirstOrDefault(m => m.ModId == nxm.ModId);
                if (found != null) mod = new NexusModViewModel(this, found);
            }
        }
        catch (Exception e)
        {
            Log.Write($"Info de Nexus: {e.Message}");
        }
        mod ??= new NexusModViewModel(this, new NexusMod(nxm.ModId, $"Mod {nxm.ModId}", "", "", "", 0, 0, null, default, false));
        await InstallNexusFileAsync(new NexusFileViewModel(this, mod, file), nxm);
    }

    // ---------------- Juego ----------------

    async Task SetGameAsync(string path, bool quiet = false)
    {
        var game = GameInstall.Open(path);
        if (game == null)
        {
            await Alert("Carpeta incorrecta", "Ahi no esta SANABI. Elegi la carpeta que tiene SNB.exe " +
                                              "(normalmente steamapps/common/SANABI).");
            return;
        }
        store = ModStore.Load(game);
        GamePath = game.Root;
        HasGame = true;
        settings.GamePath = game.Root;
        settings.Save();
        GameStatus = $"SANABI {game.Version ?? "?"}";
        var changed = store.DetectExternalChanges();
        if (changed > 0)
            Info($"Steam reemplazo {changed} archivo(s) modificados (actualizacion o verificacion del juego).");
        RefreshInstalled();
        RefreshBepInEx();
        foreach (var c in allCards) c.Refresh(store.State, game.Version);
        if (!quiet) Info($"Juego seleccionado: {game.Root}");
        else Info($"SANABI {game.Version} encontrado en {game.Root}");
    }

    void RefreshInstalled()
    {
        Installed.Clear();
        if (store == null) return;
        foreach (var m in store.State.Mods.Values.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            var card = new InstalledModViewModel(this, m);
            Installed.Add(card);
            var cached = Path.Combine(store.StagedPath(m.Id), ".gestor-preview.png");
            if (File.Exists(cached)) { try { card.Preview = new Bitmap(cached); } catch { } }
            else if (CatalogClient.LocalOrCached()?.Mods.FirstOrDefault(e => e.Id == m.Id)?.Preview is { } preview)
                _ = LoadImageAsync(AppInfo.RawUrl(preview), b => card.Preview = b);
            else if (m.Source == "github" && m.SourceRef is { } repo)
                _ = LoadInstalledGithubPreviewAsync(card, repo);
        }
        NeedsReapply = store.State.Mods.Values.Any(m => m.Enabled && m.NeedsReapply);
        OnPropertyChanged(nameof(HasNoInstalled));
        foreach (var c in allCards) c.Refresh(store.State, store.Game.Version);
    }

    async Task LoadInstalledGithubPreviewAsync(InstalledModViewModel card, string repo)
    {
        try { if (await github.PreviewUrlAsync(repo, lifetime.Token) is { } url) await LoadImageAsync(url, b => card.Preview = b); }
        catch (OperationCanceledException) { }
    }

    void RefreshBepInEx()
    {
        BepInExInstalled = store?.BepInExInstalled ?? false;
        BepInExStatus = store == null ? "Elegi primero la carpeta del juego."
            : !BepInExInstalled ? "No instalado. Solo hace falta para mods que son plugins (.dll), como los de Nexus."
            : store.BepInExPrepared ? $"Instalado y listo ({store.State.BepInEx?.Version ?? "version desconocida"})."
            : "Instalado. Abri el juego una vez para que termine de prepararse (tarda un par de minutos).";
    }

    [RelayCommand]
    async Task BrowseGame()
    {
        var folder = await ui.PickFolderAsync("Elegi la carpeta de SANABI (donde esta SNB.exe)");
        if (folder != null) await SetGameAsync(folder);
    }

    [RelayCommand]
    async Task DetectGame()
    {
        var found = GameInstall.AutoDetect();
        if (found == null) await Alert("No encontrado", "No encontre SANABI en las bibliotecas de Steam. Elegi la carpeta a mano.");
        else await SetGameAsync(found);
    }

    [RelayCommand]
    void Play()
    {
        Platform.LaunchGame();
        Info("Abriendo SANABI con Steam...");
    }

    [RelayCommand]
    void OpenGameFolder()
    {
        if (GamePath != null) Platform.Open(GamePath);
    }

    [RelayCommand]
    void OpenDataFolder() => Platform.Open(Settings.DataDir);

    [RelayCommand]
    void OpenRepo() => Platform.Open(AppInfo.RepoUrl);

    [RelayCommand]
    void OpenGuidelines() => Platform.Open("https://store.steampowered.com/news/app/1562700/view/1834602721190057");

    [RelayCommand]
    void ToggleConsole() => ShowConsole = !ShowConsole;

    [RelayCommand]
    Task CopyLaunchOptions() => ui.CopyAsync(LinuxLaunchOptions);

    // ---------------- Instalacion comun ----------------

    async Task<bool> EnsureReadyToModify()
    {
        if (store == null)
        {
            await Alert("Falta el juego", "Primero elegi la carpeta de SANABI en Ajustes.");
            Section = SectionSettings;
            return false;
        }
        while (GameInstall.IsRunning())
        {
            if (await AskAsync("SANABI esta abierto", "Cerra el juego antes de instalar o cambiar mods.", "Ya lo cerre", "Cancelar") != "Ya lo cerre")
                return false;
        }
        return true;
    }

    async Task InstallStagedAsync(StagedMod staged)
    {
        var files = staged.RelativeFiles();
        if (files.Count == 0)
        {
            await Alert("No se pudo instalar", "No encontre archivos que se puedan instalar en este mod.\n\n" +
                                                string.Join("\n", staged.Warnings.Take(12)));
            return;
        }
        if (staged.Warnings.Count > 0)
        {
            var answer = await AskAsync("Revisa antes de seguir",
                "Algunos archivos no se van a instalar:\n\n" + string.Join("\n", staged.Warnings.Take(12)) +
                $"\n\nSe instalaran {files.Count} archivo(s).", "Instalar igual", "Cancelar");
            if (answer != "Instalar igual") return;
        }
        if (!GameInstall.Satisfies(store!.Game.Version, staged.Manifest.GameVersion))
        {
            var answer = await AskAsync("Version del juego distinta",
                $"Este mod pide la version {staged.Manifest.GameVersion} y tenes la {store.Game.Version}. " +
                "Puede romper el juego. Si pasa, desactivalo desde Mis mods.", "Instalar igual", "Cancelar");
            if (answer != "Instalar igual") return;
        }
        if (staged.Manifest.Requires.Contains("bepinex") && !store.BepInExInstalled)
        {
            var answer = await AskAsync("Necesita BepInEx",
                "Este mod es un plugin y necesita BepInEx (el cargador de plugins). ¿Lo instalo ahora?",
                "Instalar BepInEx", "Cancelar");
            if (answer != "Instalar BepInEx") return;
            await store.InstallBepInExAsync(http, Info, new Progress<double>(p => Progress = p), lifetime.Token);
            RefreshBepInEx();
            if (IsLinux) await ShowLinuxBepInExHint();
        }
        var conflicts = store.Conflicts(staged.Manifest.Id, files);
        if (conflicts.Count > 0)
        {
            var owners = string.Join(", ", conflicts.Select(c => store.State.Mods.GetValueOrDefault(c.Owner)?.Name ?? c.Owner).Distinct());
            var answer = await AskAsync("Choque entre mods",
                $"{conflicts.Count} archivo(s) ya los cambia: {owners}. El nuevo mod va a tener prioridad; " +
                "si lo desactivas, vuelve el anterior.", "Continuar", "Cancelar");
            if (answer != "Continuar") return;
        }
        Info($"Instalando {staged.Manifest.Name}...");
        await Task.Run(() => store.Install(staged, Info));
        RefreshInstalled();
        Info($"Listo: {staged.Manifest.Name} {staged.Manifest.Version} instalado.");
        if (staged.Manifest.Requires.Contains("bepinex") && !store.BepInExPrepared)
            Info("La primera vez que abras el juego con BepInEx tarda unos minutos en prepararse; es normal.");
    }

    Task ShowLinuxBepInExHint() => AskAsync("Linux: un paso mas",
        "En Linux el juego corre con Proton. Para que cargue BepInEx, en Steam: clic derecho en SANABI > Propiedades > " +
        "Opciones de lanzamiento, y pega:\n\n" + LinuxLaunchOptions, "Entendido");

    [RelayCommand]
    async Task InstallFromFile()
    {
        var path = await ui.PickFileAsync("Elegi el mod (.zip, .7z, .rar, .dll o .bundle)",
            ["*.zip", "*.7z", "*.rar", "*.dll", "*.bundle", "*.tar.gz"]);
        if (path != null) await InstallLocalPathAsync(path);
    }

    [RelayCommand]
    async Task InstallFromFolder()
    {
        var path = await ui.PickFolderAsync("Elegi la carpeta del mod");
        if (path != null) await InstallLocalPathAsync(path);
    }

    public async Task InstallLocalPathAsync(string path)
    {
        if (Section == SectionUpload)
        {
            await LoadUploadSourceAsync(path);
            return;
        }
        if (!await EnsureReadyToModify()) return;
        await RunBusy("instalar", async () =>
        {
            var name = Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\'));
            Info($"Leyendo {Path.GetFileName(path)}...");
            var staged = await Task.Run(() => ArchiveImporter.Import(path, store!.Game, name, "local", Path.GetFileName(path)));
            await InstallStagedAsync(staged);
            Section = SectionInstalled;
        });
    }

    // ---------------- Mis mods ----------------

    public async Task ToggleModAsync(InstalledModViewModel item)
    {
        var want = item.IsEnabled;
        if (!await EnsureReadyToModify())
        {
            item.IsEnabled = !want;
            return;
        }
        await RunBusy(want ? "activar" : "desactivar", async () =>
        {
            if (want && item.NeedsBepInEx && !store!.BepInExInstalled)
            {
                await store.InstallBepInExAsync(http, Info, new Progress<double>(p => Progress = p), lifetime.Token);
                RefreshBepInEx();
            }
            await Task.Run(() =>
            {
                if (want) store!.Enable(item.Mod.Id, Info);
                else store!.Disable(item.Mod.Id, Info);
            });
        });
        RefreshInstalled();
    }

    public async Task UninstallModAsync(InstalledModViewModel item)
    {
        if (await AskHighlightedAsync("Desinstalar", "Se restauran los archivos originales del juego.", item.Name,
                "Desinstalar", "Cancelar") != "Desinstalar") return;
        if (!await EnsureReadyToModify()) return;
        await RunBusy("desinstalar", () => Task.Run(() => store!.Uninstall(item.Mod.Id, Info)));
        RefreshInstalled();
    }

    public void OpenModFolder(InstalledModViewModel item)
    {
        if (store != null) Platform.Open(store.StagedPath(item.Mod.Id));
    }

    [RelayCommand]
    async Task Reapply()
    {
        if (!await EnsureReadyToModify()) return;
        await RunBusy("reaplicar", () => Task.Run(() => store!.Reapply(Info)));
        RefreshInstalled();
    }

    [RelayCommand]
    async Task RestoreOriginal()
    {
        if (store == null) return;
        if (await AskAsync("Dejar el juego original",
                "Se desactivan todos los mods y se quita BepInEx. Tus mods quedan guardados para activarlos despues.",
                "Restaurar", "Cancelar") != "Restaurar") return;
        if (!await EnsureReadyToModify()) return;
        await RunBusy("restaurar", () => Task.Run(() =>
        {
            store.DisableAll(Info);
            if (store.BepInExInstalled) store.UninstallBepInEx(Info);
        }));
        RefreshInstalled();
        RefreshBepInEx();
        Info("El juego quedo como el original.");
    }

    // ---------------- Mods: chequeados (catalogo del repo) + comunidad (GitHub) ----------------

    readonly List<ModCardViewModel> allCards = new();
    CommunityList community = new();
    [ObservableProperty] string? modsError;
    [ObservableProperty] bool modsLoading;
    [ObservableProperty] string modsQuery = "";

    partial void OnModsQueryChanged(string value) => FilterCards();

    void FilterCards()
    {
        CheckedMods.Clear();
        CommunityMods.Clear();
        ExperimentalMods.Clear();
        foreach (var c in allCards.Where(c => c.Matches(ModsQuery) && MatchesCategory(c)))
            (c.Entry?.Verification == "experimental" ? ExperimentalMods : c.IsChecked ? CheckedMods : CommunityMods).Add(c);
        OnPropertyChanged(nameof(HasExperimentalMods));
        OnPropertyChanged(nameof(HasNoCommunity));
    }

    public bool HasNoCommunity => CommunityMods.Count == 0;

    void AddCard(ModCardViewModel card, string? previewUrl)
    {
        card.Refresh(store?.State, store?.Game.Version);
        allCards.Add(card);
        if (previewUrl != null) _ = LoadImageAsync(previewUrl, b => card.Preview = b);
    }

    [RelayCommand]
    async Task LoadModsAsync()
    {
        ModsLoading = true;
        ModsError = null;
        allCards.Clear();
        try
        {
            Catalog catalog;
            try
            {
                catalog = await catalogClient.FetchAsync(lifetime.Token);
            }
            catch (Exception e) when (CatalogClient.Cached() != null)
            {
                catalog = CatalogClient.Cached()!;
                ModsError = $"Sin conexion ({e.Message}). Mostrando la ultima copia guardada.";
            }
            foreach (var entry in catalog.Mods)
                AddCard(new ModCardViewModel(this, entry), string.IsNullOrEmpty(entry.Preview) ? null : AppInfo.RawUrl(entry.Preview));
            FilterCards(); // El catálogo debe aparecer aunque GitHub esté lento o sin conexión.

            community = await catalogClient.FetchCommunityAsync(lifetime.Token);
            foreach (var repo in community.Repos)
                AddCard(new ModCardViewModel(this, repo.Repo, repo, null),
                    repo.Preview is { Length: > 0 } pv ? (pv.StartsWith("http") ? pv : AppInfo.RawUrl(pv)) : await github.PreviewUrlAsync(repo.Repo, lifetime.Token));
            FilterCards();
        }
        catch (Exception e)
        {
            ModsError = $"No se pudo cargar la lista de mods: {e.Message}";
        }
        finally
        {
            ModsLoading = false;
        }
        await SearchGitHubAsync();
    }

    // Busca en GitHub repos que no estan en la lista revisada; aparecen como "Sin revisar".
    [RelayCommand]
    async Task SearchGitHubAsync()
    {
        ModsLoading = true;
        try
        {
            var exclude = new HashSet<string>(community.Blocked.Concat(community.Repos.Select(r => r.Repo)), StringComparer.OrdinalIgnoreCase);
            var found = await github.SearchAsync(ModsQuery.Trim(), exclude, lifetime.Token);
            allCards.RemoveAll(c => c.Repo != null && !c.Reviewed);
            foreach (var r in found)
                AddCard(new ModCardViewModel(this, r.FullName, null, r.Description), await github.PreviewUrlAsync(r.FullName, lifetime.Token));
            FilterCards();
        }
        catch (Exception e)
        {
            Info($"Busqueda en GitHub: {e.Message}");
        }
        finally
        {
            ModsLoading = false;
        }
    }

    public async Task InstallCardAsync(ModCardViewModel card)
    {
        if (card.Repo == null && card.Entry == null)
        {
            card.OpenNexusFiles();
            return;
        }
        if (card.Entry != null)
        {
            await InstallCatalogAsync(card);
            return;
        }
        var curated = card.Curated;
        if (curated?.Install == "none")
        {
            if (await AskAsync(card.Name, (curated.Note ?? "Este proyecto no se instala en el juego.") + "\n\n" + card.Description,
                    "Abrir repo", "Cerrar") == "Abrir repo")
                Platform.Open(card.Url!);
            return;
        }
        if (!card.Reviewed &&
            await AskAsync("Mod sin revisar", $"Nadie reviso todavia {card.Repo}. Puede no ser un mod de SANABI o no funcionar. " +
                                              "Si algo sale mal, desactivalo desde Mis mods.", "Instalar igual", "Cancelar") != "Instalar igual")
            return;
        if (!await EnsureReadyToModify()) return;
        await RunBusy("instalar desde GitHub", async () =>
        {
            var repo = card.Repo!;
            var hint = new ModManifest
            {
                Id = card.Id,
                Name = card.Name,
                Author = card.Author,
                Version = curated?.Version ?? "repo",
                Description = card.Description,
                Homepage = card.Url,
                Requires = curated?.Requires.ToList() ?? new(),
            };

            if (curated?.Install == "files" && curated.Files is { Count: > 0 })
            {
                var staging = Downloader.NewTempDir("gf");
                foreach (var f in curated.Files)
                {
                    Info($"Descargando {Path.GetFileName(f.Src)}...");
                    await Downloader.DownloadAsync(http, GitHubClient.RawFileUrl(repo, f.Src),
                        Path.Combine(staging, f.Dst.Replace('/', Path.DirectorySeparatorChar)), new Progress<double>(p => Progress = p), lifetime.Token);
                }
                await InstallStagedAsync(new StagedMod { Manifest = hint, Dir = staging, Source = "github", SourceRef = repo });
                return;
            }

            string? file;
            var assets = await github.ReleaseAssetsAsync(repo, lifetime.Token);
            if (assets.Count > 0)
            {
                var asset = assets.Count == 1 ? assets[0] : await PickAsync("Elegi que descargar", assets, a => a.Name);
                if (asset == null) return;
                hint.Version = asset.Tag.TrimStart('v', 'V');
                Info($"Descargando {asset.Name}...");
                file = await Downloader.DownloadAsync(http, asset.Url, Path.Combine(Downloader.NewTempDir("gh"), asset.Name),
                    new Progress<double>(p => Progress = p), lifetime.Token);
            }
            else
            {
                // Sin release: buscamos paquetes o plugins ya compilados dentro del repo.
                var candidates = await github.InstallableFilesAsync(repo, lifetime.Token);
                var archives = candidates.Where(ArchiveImporter.IsArchive).ToList();
                if (archives.Count > 0)
                {
                    var pick = archives.Count == 1 ? archives[0] : await PickAsync("Elegi que instalar", archives, a => a);
                    if (pick == null) return;
                    file = await Downloader.DownloadAsync(http, GitHubClient.RawFileUrl(repo, pick),
                        Path.Combine(Downloader.NewTempDir("gh"), Path.GetFileName(pick)), new Progress<double>(p => Progress = p), lifetime.Token);
                }
                else if (candidates.Count > 0)
                {
                    var staging = Downloader.NewTempDir("gp");
                    foreach (var path in candidates)
                    {
                        var idx = path.IndexOf("BepInEx/", StringComparison.OrdinalIgnoreCase);
                        await Downloader.DownloadAsync(http, GitHubClient.RawFileUrl(repo, path),
                            Path.Combine(staging, path[idx..].Replace('/', Path.DirectorySeparatorChar)), ct: lifetime.Token);
                    }
                    file = staging;
                }
                else
                {
                    if (await AskAsync("Solo codigo fuente",
                            $"{repo} no tiene una version compilada para instalar (ni release ni archivos listos). " +
                            "Hay que compilarlo siguiendo las instrucciones del repo.", "Abrir repo", "Cerrar") == "Abrir repo")
                        Platform.Open(card.Url!);
                    return;
                }
            }

            var staged = await Task.Run(() => ArchiveImporter.Import(file, store!.Game, hint.Name, "github", repo, hint));
            staged.Manifest.Id = hint.Id;
            await InstallStagedAsync(staged);
        });
    }

    async Task<T?> PickAsync<T>(string title, List<T> options, Func<T, string> label) where T : class
    {
        var labels = options.Select(label).Take(5).ToList();
        var answer = await AskAsync(title, "Hay varias opciones:", [.. labels, "Cancelar"]);
        var i = answer == null ? -1 : labels.IndexOf(answer);
        return i >= 0 ? options[i] : null;
    }

    async Task InstallCatalogAsync(ModCardViewModel item)
    {
        if (!item.Compatible &&
            await AskAsync("No compatible", item.CompatText + "\n\n¿Instalarlo igual?", "Instalar igual", "Cancelar") != "Instalar igual")
            return;
        if (!await EnsureReadyToModify()) return;
        await RunBusy("instalar", async () =>
        {
            var staged = await catalogClient.DownloadAsync(item.EffectiveEntry!, Info, new Progress<double>(p => Progress = p), lifetime.Token);
            staged.Manifest.GameVersion = null; // ya se pregunto arriba
            await InstallStagedAsync(staged);
        });
    }

    // ---------------- Nexus Mods ----------------

    [ObservableProperty] string nexusQuery = "";
    [ObservableProperty] string? nexusError;
    [ObservableProperty] bool nexusLoading;
    [ObservableProperty] string nexusAccount = "";
    [ObservableProperty] string nexusKeyInput = "";

    partial void OnNexusQueryChanged(string value) => FilterNexus();

    void FilterNexus()
    {
        NexusMods.Clear();
        foreach (var m in allNexus.Where(m => string.IsNullOrWhiteSpace(NexusQuery)
                                              || (m.Name + " " + m.Summary + " " + m.Mod.Author).Contains(NexusQuery, StringComparison.OrdinalIgnoreCase)))
            NexusMods.Add(m);
    }

    [RelayCommand]
    async Task LoadNexusAsync()
    {
        NexusLoading = true;
        NexusError = null;
        try
        {
            var mods = await nexus.ListModsAsync(lifetime.Token);
            allNexus = mods.Where(m => !m.Adult).Select(m => new NexusModViewModel(this, m)).ToList();
            FilterNexus();
            foreach (var vm in allNexus.Where(v => v.Mod.PictureUrl != null))
                _ = LoadImageAsync(vm.Mod.PictureUrl!, b => vm.Picture = b);
            if (allNexus.Count == 0) NexusError = "No hay mods de SANABI en Nexus todavia.";
        }
        catch (Exception e)
        {
            NexusError = $"No se pudo conectar con Nexus Mods: {e.Message}";
        }
        finally
        {
            NexusLoading = false;
        }
        await RefreshNexusAccountAsync();
    }

    async Task RefreshNexusAccountAsync()
    {
        if (string.IsNullOrEmpty(nexus.ApiKey))
        {
            NexusAccount = "Sin cuenta conectada: podes ver los mods; para instalarlos con un clic conecta tu cuenta en Ajustes.";
            return;
        }
        try
        {
            var user = await nexus.ValidateAsync(lifetime.Token);
            NexusAccount = $"Conectado como {user.Name}" + (user.IsPremium ? " (Premium: descarga directa)" : " (cuenta gratis)");
        }
        catch (Exception e)
        {
            NexusAccount = e.Message;
        }
    }

    public async Task ToggleNexusFilesAsync(NexusModViewModel item)
    {
        item.ShowFiles = !item.ShowFiles;
        if (!item.ShowFiles || item.Files.Count > 0) return;
        item.LoadingFiles = true;
        try
        {
            foreach (var f in await nexus.ListFilesAsync(item.Mod.ModId, lifetime.Token))
                item.Files.Add(new NexusFileViewModel(this, item, f));
        }
        catch (Exception e)
        {
            Info($"Nexus: {e.Message}");
        }
        finally
        {
            item.LoadingFiles = false;
        }
    }

    public async Task InstallNexusFileAsync(NexusFileViewModel file, NxmLink? nxm)
    {
        var modId = file.Parent.Mod.ModId;
        if (string.IsNullOrEmpty(nexus.ApiKey))
        {
            var answer = await AskAsync("Conecta tu cuenta de Nexus",
                "Nexus Mods solo deja descargar desde programas con tu cuenta conectada. Podes:\n\n" +
                "• Conectar tu cuenta en Ajustes (una sola vez).\n" +
                "• O descargar el archivo desde la web y arrastrarlo a esta ventana.",
                "Ir a Ajustes", "Abrir la pagina del archivo", "Cancelar");
            if (answer == "Ir a Ajustes") Section = SectionSettings;
            else if (answer == "Abrir la pagina del archivo") Platform.Open(NexusClient.FilesPageUrl(modId, file.File.FileId));
            return;
        }
        var premium = false;
        if (nxm == null)
        {
            try { premium = (await nexus.ValidateAsync(lifetime.Token)).IsPremium; } catch { }
            if (!premium)
            {
                // Cuenta gratis: Nexus exige pasar por su pagina y tocar "Mod Manager Download".
                if (!Platform.NxmIsOurs())
                {
                    var answer = await AskAsync("Un paso en la web",
                        "Con cuenta gratis, Nexus pide confirmar cada descarga en su pagina. Voy a asociar los enlaces de Nexus " +
                        "a este gestor y abrir la pagina: toca \"Slow download\" y el mod se instala solo.",
                        "Continuar", "Cancelar");
                    if (answer != "Continuar") return;
                    RegisterNxmCore();
                }
                Platform.Open(NexusClient.FilesPageUrl(modId, file.File.FileId));
                Info("Esperando la confirmacion en la pagina de Nexus...");
                return;
            }
        }
        if (!await EnsureReadyToModify()) return;
        await RunBusy("instalar desde Nexus", async () =>
        {
            Info($"Descargando {file.Name} de Nexus...");
            var archive = await nexus.DownloadFileAsync(modId, file.File.FileId, file.File.FileName, nxm,
                new Progress<double>(p => Progress = p), lifetime.Token);
            var hint = new ModManifest
            {
                Id = $"nexus-{modId}",
                Name = file.Parent.Name,
                Version = string.IsNullOrEmpty(file.File.Version) ? file.Parent.Mod.Version : file.File.Version,
                Author = file.Parent.Mod.Author,
                Description = file.Parent.Summary,
                Homepage = file.Parent.Mod.Url,
            };
            var staged = await Task.Run(() => ArchiveImporter.Import(archive, store!.Game, hint.Name, "nexus",
                $"{modId}/{file.File.FileId}", hint));
            // Los mods de Nexus no traen mod.json: usamos los datos de Nexus.
            staged.Manifest.Id = hint.Id;
            await InstallStagedAsync(staged);
            try { File.Delete(archive); } catch { }
        });
    }

    // ---------------- Cuentas ----------------

    [ObservableProperty] string gitHubAccount = "";
    [ObservableProperty] string gitHubTokenInput = "";
    [ObservableProperty] string? deviceCode;

    [RelayCommand]
    async Task SaveNexusKey()
    {
        var key = NexusKeyInput.Trim();
        if (key.Length == 0) return;
        nexus.ApiKey = key;
        try
        {
            var user = await nexus.ValidateAsync(lifetime.Token);
            settings.NexusApiKey = key;
            settings.Save();
            NexusKeyInput = "";
            Info($"Cuenta de Nexus conectada: {user.Name}");
        }
        catch (Exception e)
        {
            nexus.ApiKey = settings.NexusApiKey;
            await Alert("Nexus", e.Message);
        }
        await RefreshNexusAccountAsync();
    }

    [RelayCommand]
    async Task NexusSso()
    {
        try
        {
            Info("Aproba el acceso en la pagina de Nexus que se abrio...");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            cts.CancelAfter(TimeSpan.FromMinutes(5));
            var key = await NexusClient.SsoLoginAsync(Platform.Open, cts.Token);
            NexusKeyInput = key;
            await SaveNexusKey();
        }
        catch (Exception e)
        {
            await Alert("Nexus", e.Message);
        }
    }

    [RelayCommand]
    async Task DisconnectNexus()
    {
        settings.NexusApiKey = null;
        settings.Save();
        nexus.ApiKey = null;
        await RefreshNexusAccountAsync();
    }

    [RelayCommand]
    void OpenNexusKeyPage() => Platform.Open(NexusClient.ApiKeyPage);

    void RegisterNxmCore()
    {
        try
        {
            Platform.RegisterNxm();
            NxmRegistered = Platform.NxmIsOurs();
            Info("Los enlaces de Nexus (nxm://) ahora abren este gestor.");
        }
        catch (Exception e)
        {
            Info($"No se pudieron asociar los enlaces nxm: {e.Message}");
        }
    }

    [RelayCommand]
    void RegisterNxm() => RegisterNxmCore();

    async Task RefreshGitHubAccountAsync()
    {
        if (string.IsNullOrEmpty(github.Token))
        {
            GitHubAccount = "";
            return;
        }
        try
        {
            GitHubAccount = await github.GetLoginAsync(lifetime.Token);
        }
        catch
        {
            GitHubAccount = "";
        }
    }

    [RelayCommand]
    async Task GitHubLogin()
    {
        try
        {
            var code = await github.StartDeviceFlowAsync(lifetime.Token);
            DeviceCode = code.UserCode;
            await ui.CopyAsync(code.UserCode);
            Platform.Open(code.VerificationUri);
            Info($"Pega el codigo {code.UserCode} en la pagina de GitHub (ya esta copiado).");
            var token = await github.PollDeviceFlowAsync(code, lifetime.Token);
            github.Token = token;
            settings.GitHubToken = token;
            settings.Save();
            await RefreshGitHubAccountAsync();
            Info($"Sesion de GitHub iniciada: {GitHubAccount}");
        }
        catch (Exception e)
        {
            await Alert("GitHub", e.Message);
        }
        finally
        {
            DeviceCode = null;
        }
    }

    [RelayCommand]
    async Task SaveGitHubToken()
    {
        var token = GitHubTokenInput.Trim();
        if (token.Length == 0) return;
        github.Token = token;
        await RefreshGitHubAccountAsync();
        if (GitHubAccount.Length == 0)
        {
            github.Token = settings.GitHubToken;
            await Alert("GitHub", "Ese token no funciona. Tiene que tener el permiso public_repo.");
            return;
        }
        settings.GitHubToken = token;
        settings.Save();
        GitHubTokenInput = "";
    }

    [RelayCommand]
    void OpenGitHubTokenPage() =>
        Platform.Open("https://github.com/settings/tokens/new?scopes=public_repo&description=SANABI%20Gestor");

    [RelayCommand]
    async Task GitHubLogout()
    {
        settings.GitHubToken = null;
        settings.Save();
        github.Token = null;
        await RefreshGitHubAccountAsync();
    }

    // ---------------- Subir mod ----------------

    public string[] Categories { get; } = ["skins", "personajes", "habilidades", "jugabilidad", "visual", "audio", "dificultad", "traduccion", "herramienta", "otros"];

    [ObservableProperty] string uploadName = "";
    [ObservableProperty] string uploadVersion = "1.0.0";
    [ObservableProperty] string uploadAuthor = "";
    [ObservableProperty] string uploadDescription = "";
    [ObservableProperty] string uploadCategory = "otros";
    [ObservableProperty] string uploadGameVersion = "";
    [ObservableProperty] string uploadNotes = "";
    [ObservableProperty] string? uploadSource;
    [ObservableProperty] string? uploadPreview;
    [ObservableProperty] string? uploadResultUrl;
    [ObservableProperty] bool uploadAccepted;
    StagedMod? uploadStaged;
    public ObservableCollection<string> UploadFiles { get; } = new();
    public ObservableCollection<string> UploadWarnings { get; } = new();

    [RelayCommand]
    async Task PickUploadArchive()
    {
        var path = await ui.PickFileAsync("Elegi el mod empaquetado", ["*.zip", "*.7z", "*.rar", "*.dll", "*.bundle"]);
        if (path != null) await LoadUploadSourceAsync(path);
    }

    [RelayCommand]
    async Task PickUploadFolder()
    {
        var path = await ui.PickFolderAsync("Elegi la carpeta del mod");
        if (path != null) await LoadUploadSourceAsync(path);
    }

    [RelayCommand]
    async Task PickUploadPreview()
    {
        UploadPreview = await ui.PickFileAsync("Imagen de portada (PNG)", ["*.png"]) ?? UploadPreview;
    }

    async Task LoadUploadSourceAsync(string path)
    {
        if (store == null)
        {
            await Alert("Falta el juego", "Para ordenar los archivos del mod necesito la carpeta de SANABI (Ajustes).");
            return;
        }
        await RunBusy("leer el mod", async () =>
        {
            var name = string.IsNullOrWhiteSpace(UploadName) ? Path.GetFileNameWithoutExtension(path.TrimEnd('/', '\\')) : UploadName;
            var staged = await Task.Run(() => ArchiveImporter.Import(path, store.Game, name));
            uploadStaged = staged;
            UploadSource = path;
            var m = staged.Manifest;
            if (string.IsNullOrWhiteSpace(UploadName)) UploadName = m.Name;
            if (!string.IsNullOrWhiteSpace(m.Description) && string.IsNullOrWhiteSpace(UploadDescription)) UploadDescription = m.Description;
            if (!string.IsNullOrWhiteSpace(m.Author) && string.IsNullOrWhiteSpace(UploadAuthor)) UploadAuthor = m.Author;
            if (m.Version != "1.0.0") UploadVersion = m.Version;
            if (Categories.Contains(m.Category)) UploadCategory = m.Category;
            else if (m.Category == "personaje") UploadCategory = "skins";
            if (string.IsNullOrWhiteSpace(UploadGameVersion)) UploadGameVersion = m.GameVersion ?? (store.Game.Version != null ? ">=" + store.Game.Version : "");
            UploadFiles.Clear();
            foreach (var f in staged.RelativeFiles()) UploadFiles.Add(f);
            UploadWarnings.Clear();
            foreach (var w in staged.Warnings) UploadWarnings.Add(w);
            if (string.IsNullOrWhiteSpace(UploadAuthor)) UploadAuthor = GitHubAccount;
            Info($"Mod leido: {UploadFiles.Count} archivo(s) listos para subir.");
        });
    }

    string? ValidateUpload()
    {
        if (uploadStaged == null || UploadFiles.Count == 0) return "Elegi los archivos del mod.";
        if (UploadName.Trim().Length < 3) return "Ponele un nombre al mod.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(UploadVersion.Trim(), @"^\d+(\.\d+){0,3}$")) return "La version tiene que ser tipo 1.0.0";
        if (UploadDescription.Trim().Length < 10) return "Escribi una descripcion (que hace el mod).";
        if (UploadAuthor.Trim().Length == 0) return "Falta el autor.";
        if (!UploadAccepted) return "Tenes que aceptar las reglas de contenido de fans de SANABI.";
        return null;
    }

    ModManifest BuildUploadManifest()
    {
        var m = uploadStaged!.Manifest;
        return new ModManifest
        {
            Id = Paths.Slug(UploadName),
            Name = UploadName.Trim(),
            Version = UploadVersion.Trim(),
            Author = UploadAuthor.Trim(),
            Description = UploadDescription.Trim(),
            Category = UploadCategory,
            Requires = m.Requires.ToList(),
            GameVersion = string.IsNullOrWhiteSpace(UploadGameVersion) ? null : UploadGameVersion.Trim(),
            Tags = m.Tags,
            Homepage = m.Homepage,
        };
    }

    [RelayCommand]
    async Task SubmitUpload()
    {
        var error = ValidateUpload();
        if (error != null)
        {
            await Alert("Falta algo", error);
            return;
        }
        if (string.IsNullOrEmpty(github.Token))
        {
            await Alert("GitHub", "Para enviar el mod necesitas iniciar sesion con GitHub (arriba en esta misma pantalla).");
            return;
        }
        var manifest = BuildUploadManifest();
        if (await AskAsync("Enviar para revision",
                $"Se va a enviar \"{manifest.Name}\" ({UploadFiles.Count} archivos) al repositorio para que lo revisen. " +
                "No aparece en el catalogo hasta que lo aprueben.", "Enviar", "Cancelar") != "Enviar") return;
        await RunBusy("subir el mod", async () =>
        {
            var url = await github.SubmitModAsync(manifest, uploadStaged!.Dir, UploadPreview, UploadNotes, Info,
                new Progress<double>(p => Progress = p), lifetime.Token);
            UploadResultUrl = url;
            Info($"Mod enviado. Revision: {url}");
            if (await AskAsync("¡Enviado!", "Tu mod quedo esperando revision. Te llega un aviso de GitHub cuando lo aprueben.",
                    "Ver el pull request", "Cerrar") == "Ver el pull request")
                Platform.Open(url);
        });
    }

    [RelayCommand]
    async Task ExportUpload()
    {
        var error = ValidateUpload();
        if (error != null)
        {
            await Alert("Falta algo", error);
            return;
        }
        var manifest = BuildUploadManifest();
        var path = await ui.SaveFileAsync("Guardar paquete del mod", $"{manifest.Id}-{manifest.Version}.zip");
        if (path == null) return;
        await RunBusy("exportar", () => Task.Run(() =>
        {
            if (File.Exists(path)) File.Delete(path);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            var json = JsonSerializer.Serialize(manifest, JsonCtx.Default.ModManifest);
            using (var w = new StreamWriter(zip.CreateEntry(Paths.ManifestName).Open())) w.Write(json);
            foreach (var rel in UploadFiles)
                zip.CreateEntryFromFile(Path.Combine(uploadStaged!.Dir, rel), "files/" + rel);
            if (UploadPreview != null) zip.CreateEntryFromFile(UploadPreview, "preview.png");
            Info($"Paquete guardado: {path} (sirve para Nexus o para instalar desde archivo).");
        }));
    }

    [RelayCommand]
    async Task TestUpload()
    {
        if (uploadStaged == null || UploadFiles.Count == 0 || UploadName.Trim().Length == 0)
        {
            await Alert("Falta algo", "Elegi los archivos del mod y ponele un nombre.");
            return;
        }
        if (!await EnsureReadyToModify()) return;
        await RunBusy("probar", async () =>
        {
            var copy = Downloader.NewTempDir("t");
            foreach (var rel in UploadFiles) Paths.CopyFile(Path.Combine(uploadStaged!.Dir, rel), Path.Combine(copy, rel));
            var manifest = BuildUploadManifest();
            await InstallStagedAsync(new StagedMod { Manifest = manifest, Dir = copy, Source = "local", SourceRef = "prueba" });
        });
    }

    // ---------------- BepInEx ----------------

    [RelayCommand]
    async Task InstallBepInEx()
    {
        if (!await EnsureReadyToModify()) return;
        await RunBusy("instalar BepInEx", () => store!.InstallBepInExAsync(http, Info, new Progress<double>(p => Progress = p), lifetime.Token));
        RefreshBepInEx();
        if (IsLinux) await ShowLinuxBepInExHint();
    }

    [RelayCommand]
    async Task UninstallBepInEx()
    {
        if (!await EnsureReadyToModify()) return;
        await RunBusy("quitar BepInEx", () => Task.Run(() => store!.UninstallBepInEx(Info)));
        RefreshBepInEx();
        RefreshInstalled();
    }

    // ---------------- Varios ----------------

    [RelayCommand]
    void OpenReleases() => Platform.Open(AppInfo.RepoUrl + "/releases/latest");

    static readonly SemaphoreSlim ImageGate = new(4);

    async Task LoadImageAsync(string url, Action<Bitmap> set)
    {
        try
        {
            var cacheDir = Path.Combine(Settings.CacheDir, "img");
            Directory.CreateDirectory(cacheDir);
            var file = Path.Combine(cacheDir, Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(url))));
            await ImageGate.WaitAsync(lifetime.Token);
            try
            {
                if (!File.Exists(file)) await Downloader.DownloadAsync(http, url, file, ct: lifetime.Token);
            }
            finally
            {
                ImageGate.Release();
            }
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) File.Copy(new Uri(url).LocalPath, file, true);
            var bitmap = await Task.Run(() =>
            {
                using var fs = File.OpenRead(file);
                return Bitmap.DecodeToWidth(fs, 480, BitmapInterpolationMode.MediumQuality);
            });
            Dispatcher.UIThread.Post(() => set(bitmap));
        }
        catch (Exception e)
        {
            Log.Write($"Imagen {url}: {e.Message}");
        }
    }
}
