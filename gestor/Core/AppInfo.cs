using System.Reflection;

namespace SanabiGestor.Core;

public static class AppInfo
{
    public const string Name = "SANABI Gestor";
    public const string RepoOwner = "xXKuroiKenshiXx";
    public const string RepoName = "SANABI-GESTOR";
    public const string RepoBranch = "main";
    public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;

    public const string NexusGameDomain = "sanabi";
    public const int NexusGameId = 6117;
    public const int SteamAppId = 1562700;

    // Client ID de la OAuth App de GitHub (no es secreto). Vacio = solo token personal.
    public static string GitHubClientId => Environment.GetEnvironmentVariable("SANABI_GESTOR_GITHUB_CLIENT_ID")
        ?? Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "GitHubClientId")?.Value ?? "";
    // Slug de aplicacion registrado en Nexus Mods para el login SSO. Vacio = solo API key.
    public const string NexusAppSlug = "";

    // Todos los mods de Nexus para SANABI estan hechos para esta version de BepInEx.
    public const string BepInExVersion = "6.0.0-pre.1";
    public const string BepInExUrl =
        "https://github.com/BepInEx/BepInEx/releases/download/v6.0.0-pre.1/BepInEx_UnityIL2CPP_x64_6.0.0-pre.1.zip";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.0.0";

    public static string UserAgent => $"SanabiGestor/{Version}";

    // Para desarrollar: SANABI_GESTOR_REPO=<carpeta del repo> hace que el catalogo se lea de disco.
    public static string? LocalRepo { get; } = Environment.GetEnvironmentVariable("SANABI_GESTOR_REPO") is { Length: > 0 } dir
        && Directory.Exists(dir) ? dir : FindPortableRepo();

    // Los builds dentro del proyecto también deben mostrar los mods locales recién generados.
    static string? FindPortableRepo()
    {
        // AppImages execute in a temporary mount. Search beside the actual
        // AppImage first so local catalogs work without an environment override.
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        return appImage is { Length: > 0 } && FindPortableRepoAt(Path.GetDirectoryName(appImage)!) is { } external
            ? external : FindPortableRepoAt(AppContext.BaseDirectory);
    }

    public static string? FindPortableRepoAt(string directory)
    {
        var dir = new DirectoryInfo(directory);
        for (var i = 0; i < 4 && dir != null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "catalog.json"))
                && Directory.Exists(Path.Combine(dir.FullName, "mods"))) return dir.FullName;
        return null;
    }

    public static string RawUrl(string repoPath) =>
        LocalRepo != null
            ? new Uri(Path.Combine(LocalRepo, repoPath.Replace('/', Path.DirectorySeparatorChar))).AbsoluteUri
            : $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{RepoBranch}/" +
              string.Join('/', repoPath.Split('/').Select(Uri.EscapeDataString));
}
