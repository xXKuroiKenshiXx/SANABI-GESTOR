using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanabiGestor.Core;

namespace SanabiGestor.ViewModels;

public static class Fmt
{
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB",
    };

    public static string Category(string c) => c.ToLowerInvariant() switch
    {
        "personaje" or "skins" => "Skins",
        "habilidades" => "Habilidades",
        "personajes" => "Personajes",
        "traduccion" => "Traduccion",
        "jugabilidad" => "Jugabilidad",
        "audio" => "Audio",
        "visual" => "Entorno y efectos",
        "dificultad" => "Dificultad",
        "herramienta" => "Herramienta",
        _ => "Otros",
    };

    public static string Source(string s) => s switch
    {
        "catalogo" => "Catalogo",
        "nexus" => "Nexus Mods",
        "github" => "GitHub",
        "discord" => "Archivo local",
        _ => "Archivo local",
    };

    public static string CategoryDescription(string c) => c.ToLowerInvariant() switch
    {
        "personaje" or "skins" => "Skins: apariencia del personaje, trajes, capas y animaciones. No añaden habilidades.",
        "habilidades" => "Habilidades: ataques, armas y movimiento que modifican cómo se juega. Pueden combinarse con skins compatibles.",
        "personajes" => "Personajes: reemplazan al jugador con su controlador, animaciones y habilidades. Las skins solo cambian su apariencia.",
        "visual" => "Entorno y efectos: escenarios, iluminación, partículas e interfaz del juego.",
        "jugabilidad" => "Jugabilidad: cambios en reglas, controles y sistemas del juego.",
        "traduccion" => "Traducción: textos e idiomas del juego mediante archivos de una edición compatible.",
        "audio" => "Audio: música, voces y efectos de sonido.",
        _ => "Otras modificaciones del juego.",
    };
}

public partial class InstalledModViewModel(MainViewModel owner, InstalledMod mod) : ObservableObject
{
    public InstalledMod Mod { get; } = mod;
    public string Name => Mod.Name;
    public string Version => string.IsNullOrEmpty(Mod.Version) ? "" : "v" + Mod.Version;
    public string Author => string.IsNullOrEmpty(Mod.Author) ? "" : "por " + Mod.Author;
    public string Description => Mod.Description;
    public string SourceText => Fmt.Source(Mod.Source);
    public string FilesText => $"{Mod.Files.Count} archivo(s) · instalado {Mod.InstalledAt}";
    public bool NeedsReapply => Mod.NeedsReapply;
    public bool NeedsBepInEx => Mod.Requires.Contains("bepinex");

    [ObservableProperty] Bitmap? preview;
    [ObservableProperty] bool isEnabled = mod.Enabled;

    [RelayCommand]
    Task Toggle() => owner.ToggleModAsync(this);

    [RelayCommand]
    Task Uninstall() => owner.UninstallModAsync(this);

    [RelayCommand]
    void OpenFolder() => owner.OpenModFolder(this);

    [RelayCommand]
    void OpenDetails() => owner.ShowInstalledDetails(this);
}

// Tarjeta de la seccion Mods: sirve para los mods chequeados (catalogo del repo) y los de la comunidad (GitHub).
public partial class ModCardViewModel : ObservableObject
{
    readonly MainViewModel owner;
    readonly InstalledMod? installedDetails;
    readonly NexusModViewModel? nexusDetails;

    public ModCardViewModel(MainViewModel owner, CatalogEntry entry, bool isChecked = true)
    {
        this.owner = owner;
        Entry = entry;
        selectedVariant = entry.Variants.FirstOrDefault(v => v.Version == entry.Version) ?? entry.Variants.FirstOrDefault();
        IsChecked = isChecked && entry.Verification != "experimental";
        Id = entry.Id;
        Name = entry.Name;
        Author = entry.Author;
        Version = "v" + entry.Version;
        Description = entry.Description;
        Category = Fmt.Category(entry.Category);
        Meta = $"{Version} · por {Author} · {Fmt.Size(entry.Size)}";
        NeedsBepInEx = entry.Requires.Contains("bepinex");
        Url = entry.Homepage;
    }

    public ModCardViewModel(MainViewModel owner, string repo, CommunityRepo? curated, string? description)
    {
        this.owner = owner;
        Repo = repo;
        Curated = curated;
        IsChecked = false;
        Id = "gh-" + Paths.Slug(repo.Replace('/', '-'));
        Name = curated?.Name is { Length: > 0 } n ? n : repo.Split('/')[1];
        Author = curated?.Author is { Length: > 0 } a ? a : repo.Split('/')[0];
        Description = curated?.Description is { Length: > 0 } d ? d : string.IsNullOrWhiteSpace(description) ? "Sin descripcion." : description!;
        Category = curated?.Kind == "herramienta" ? "Herramienta" : "Mod";
        Version = curated?.Version is { Length: > 0 } v ? "v" + v : "";
        Meta = $"github.com/{repo}";
        NeedsBepInEx = curated?.Requires.Contains("bepinex") ?? false;
        Note = curated?.Note;
        Reviewed = curated != null;
        Url = "https://github.com/" + repo;
        if (curated?.Install == "none") ActionText = "Ver como usarlo";
    }

    public ModCardViewModel(MainViewModel owner, InstalledMod mod)
    {
        this.owner = owner;
        installedDetails = mod;
        Id = mod.Id; Name = mod.Name; Author = mod.Author; Version = "v" + mod.Version;
        Description = mod.Description; Category = Fmt.Source(mod.Source);
        Meta = $"{Version} · por {Author} · {mod.Files.Count} archivos";
        IsInstalled = true;
        NeedsBepInEx = mod.Requires.Contains("bepinex");
    }

    public ModCardViewModel(MainViewModel owner, NexusModViewModel item)
    {
        this.owner = owner;
        nexusDetails = item;
        Id = "nexus-" + item.Mod.ModId; Name = item.Name; Author = item.Mod.Author;
        Version = item.Version; Description = item.Summary; Category = "Nexus Mods";
        Meta = $"{Version} · {item.Author} · {item.Stats}";
        Url = item.Mod.Url; Preview = item.Picture;
        ActionText = "Ver archivos en Nexus";
    }

    public CatalogEntry? Entry { get; }
    public IReadOnlyList<CatalogVariant> Variants => Entry?.Variants ?? [];
    public bool HasVariants => Variants.Count > 0;
    public bool IsTranslation => Entry?.Category == "traduccion";
    public CatalogEntry? EffectiveEntry => Entry == null || SelectedVariant == null ? Entry : new CatalogEntry
    {
        Id = Entry.Id, Name = Entry.Name, Version = SelectedVariant.Version, Author = Entry.Author,
        Description = Entry.Description, Category = Entry.Category, Tags = Entry.Tags, Requires = Entry.Requires,
        GameVersion = SelectedVariant.GameVersion, CompatibilityNotes = SelectedVariant.CompatibilityNotes,
        InstallInstructions = Entry.InstallInstructions, Homepage = Entry.Homepage, Path = Entry.Path,
        Preview = Entry.Preview, Readme = Entry.Readme, Verification = Entry.Verification,
        Files = SelectedVariant.Files, Size = SelectedVariant.Files.Sum(f => f.Size)
    };
    [ObservableProperty] CatalogVariant? selectedVariant;
    [ObservableProperty] string readmeText = "";
    [ObservableProperty] bool readmeLoading;
    public bool HasReadme => !string.IsNullOrWhiteSpace(ReadmeText) || ReadmeLoading;
    partial void OnReadmeTextChanged(string value) => OnPropertyChanged(nameof(HasReadme));
    partial void OnReadmeLoadingChanged(bool value) => OnPropertyChanged(nameof(HasReadme));
    partial void OnSelectedVariantChanged(CatalogVariant? value)
    {
        OnPropertyChanged(nameof(FileRows)); OnPropertyChanged(nameof(GameVersionText));
        OnPropertyChanged(nameof(CompatibilityNotes)); OnPropertyChanged(nameof(EffectiveEntry));
        OnPropertyChanged(nameof(Version)); OnPropertyChanged(nameof(Meta));
        Refresh(lastState, lastGameVersion);
    }
    GameState? lastState;
    string? lastGameVersion;
    public string? Repo { get; }
    public CommunityRepo? Curated { get; }
    public string Id { get; }
    public bool IsChecked { get; }
    public bool IsCommunity => !IsChecked;
    public bool Reviewed { get; }
    public string ReviewText => Entry?.Verification == "experimental" ? "Experimental" : Reviewed ? "Es de SANABI" : "Sin revisar";
    public string Name { get; }
    public string Author { get; }
    string baseVersion = "";
    public string Version { get => SelectedVariant == null ? baseVersion : "v" + SelectedVariant.Version; private set => baseVersion = value; }
    public string Description { get; }
    public string Category { get; }
    public string CategoryDescription => Fmt.CategoryDescription(Entry?.Category ?? "otros");
    string baseMeta = "";
    public string Meta { get => EffectiveEntry is { } e ? $"{Version} · por {Author} · {Fmt.Size(e.Size)}" : baseMeta; private set => baseMeta = value; }
    public string? Note { get; }
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool NeedsBepInEx { get; }
    public string? Url { get; }
    public bool HasUrl => !string.IsNullOrEmpty(Url);
    public string UrlText => IsChecked ? "Mas info" : "Abrir repo";
    public bool CanInstall => Entry != null || Repo != null || nexusDetails != null;
    public string InstallationTitle => Curated?.Install == "none" ? "Uso manual / herramienta externa" : "Cómo instalar";
    public string InstallationInstructions => Entry?.InstallInstructions
        ?? (installedDetails != null ? "Este mod ya está instalado. Activalo, desactivalo o desinstalalo desde Mis mods. El gestor conserva un respaldo de los archivos originales."
        : nexusDetails != null ? "Abrí Ver archivos en Nexus para elegir la descarga. Con cuenta gratis usá Mod Manager Download en Nexus: el enlace abre el gestor. Después podés activar o desactivar el mod desde Mis mods."
        : Curated?.Install == "none" ? Curated.Note ?? "Este proyecto necesita pasos manuales. Abrí el repositorio para seguir las instrucciones del autor."
        : "Cerrá SANABI y elegí su carpeta en Ajustes. Pulsá Instalar: el mod se instala y activa. El gestor guarda el respaldo original y avisa si hay conflictos. Podés desactivarlo desde Mis mods. Para instalar un ZIP local, arrastralo a la ventana.");
    public string CompatibilityNotes => EffectiveEntry?.CompatibilityNotes
        ?? (NeedsBepInEx ? "Necesita BepInEx. El gestor ofrece instalarlo antes de aplicar el mod." : "Revisá la versión del juego y las instrucciones del autor antes de activarlo.");
    public string GameVersionText => EffectiveEntry?.GameVersion is { Length: > 0 } v ? "SANABI " + v : "Versión del juego no especificada por el autor";
    public string VerificationText => Entry == null ? "Mod externo: consultá las comprobaciones y notas del autor."
        : Entry.Verification == "experimental" ? "Experimental: compilado y comprobado fuera del juego. Prueba de jugabilidad pendiente."
        : Entry.Verification == "gameplay" ? "Probado dentro del juego."
        : "Paquete validado: archivos, rutas y hashes comprobados. Prueba dentro del juego pendiente.";
    public IEnumerable<string> FileRows => EffectiveEntry != null
        ? EffectiveEntry.Files.Select(f => $"{f.Dst}\n{Fmt.Size(f.Size)} · SHA-256: {f.Sha256}")
        : installedDetails != null ? installedDetails.Files
        : nexusDetails is { Files.Count: > 0 } n ? n.Files.Select(f => $"{f.Name}\n{f.Meta}")
        : Curated?.Files is { Count: > 0 } files ? files.Select(f => f.Dst)
        : new[] { "El autor no publicó una lista de archivos de destino. Se muestra al elegir o descargar el paquete." };

    [ObservableProperty] Bitmap? preview;
    [ObservableProperty] string actionText = "Instalar";
    [ObservableProperty] bool isInstalled;
    [ObservableProperty] bool compatible = true;
    [ObservableProperty] string compatText = "";

    public bool Matches(string query) =>
        string.IsNullOrWhiteSpace(query)
        || $"{Name} {Author} {Description} {Repo}".Contains(query, StringComparison.OrdinalIgnoreCase);

    public void Refresh(GameState? state, string? gameVersion)
    {
        lastState = state; lastGameVersion = gameVersion;
        var installed = state?.Mods.GetValueOrDefault(Id);
        IsInstalled = installed != null;
        if (Curated?.Install == "none") return;
        if (Entry == null)
        {
            ActionText = installed == null ? "Instalar" : "Reinstalar";
            return;
        }
        ActionText = installed == null ? "Instalar"
            : GameInstall.CompareVersions(installed.Version, EffectiveEntry!.Version) < 0 ? "Actualizar"
            : "Reinstalar";
        Compatible = GameInstall.Satisfies(gameVersion, EffectiveEntry!.GameVersion);
        CompatText = Compatible ? "" : $"No es para tu versión del juego ({gameVersion}); pide {EffectiveEntry.GameVersion}";
    }

    [RelayCommand]
    Task Install() => owner.InstallCardAsync(this);

    [RelayCommand]
    void OpenDetails() => owner.ShowModDetails(this);

    public void OpenNexusFiles() => nexusDetails?.OpenPageCommand.Execute(null);

    [RelayCommand]
    void OpenUrl()
    {
        if (Url != null) Platform.Open(Url);
    }
}

public partial class NexusModViewModel(MainViewModel owner, NexusMod mod) : ObservableObject
{
    [RelayCommand]
    void OpenDetails() => owner.ShowModDetails(new ModCardViewModel(owner, this));
    public NexusMod Mod { get; } = mod;
    public string Name => Mod.Name;
    public string Author => "por " + Mod.Author;
    public string Summary => Mod.Summary;
    public string Version => string.IsNullOrEmpty(Mod.Version) ? "" : "v" + Mod.Version;
    public string Stats => $"{Mod.Downloads:N0} descargas · {Mod.Endorsements:N0} endorsements";
    public ObservableCollection<NexusFileViewModel> Files { get; } = new();

    [ObservableProperty] Bitmap? picture;
    [ObservableProperty] bool showFiles;
    [ObservableProperty] bool loadingFiles;

    [RelayCommand]
    Task ToggleFiles() => owner.ToggleNexusFilesAsync(this);

    [RelayCommand]
    void OpenPage() => Platform.Open(Mod.Url);
}

public partial class NexusFileViewModel(MainViewModel owner, NexusModViewModel parent, NexusFile file) : ObservableObject
{
    public NexusFile File { get; } = file;
    public NexusModViewModel Parent { get; } = parent;
    public string Name => File.Name;
    public string Meta => $"{(File.Category == "MAIN" ? "Principal" : "Opcional")} · v{File.Version} · {Fmt.Size(File.SizeBytes)} · {File.Date:yyyy-MM-dd}";
    public string Description => File.Description;

    [RelayCommand]
    Task Install() => owner.InstallNexusFileAsync(this, null);
}

public partial class DialogViewModel : ObservableObject
{
    readonly TaskCompletionSource<string?> done = new();
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string HighlightName { get; init; } = "";
    public List<DialogButton> Buttons { get; init; } = new();
    public Task<string?> Result => done.Task;

    public void Close(string? value) => done.TrySetResult(value);
}

public sealed partial class DialogButton(DialogViewModel dialog, string text, bool primary)
{
    public string Text { get; } = text;
    public bool Primary { get; } = primary;

    [RelayCommand]
    void Choose() => dialog.Close(Text);
}
