using System.Text.Json;
using System.Text.Json.Serialization;

namespace SanabiGestor.Core;

public sealed class Settings
{
    public string? GamePath { get; set; }
    public string? NexusApiKey { get; set; }
    public string? GitHubToken { get; set; }
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "es";
    public string? ReadReleaseVersion { get; set; }

    [JsonIgnore] public static string DataDir { get; } = ResolveDataDir();
    [JsonIgnore] public static string CacheDir => Path.Combine(DataDir, "cache");
    [JsonIgnore] static string FilePath => Path.Combine(DataDir, "settings.json");

    // Portable: los datos viven al lado del ejecutable (o del .AppImage).
    // Si esa carpeta no se puede escribir, se usa la carpeta de datos del usuario.
    static string ResolveDataDir()
    {
        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        var baseDir = !string.IsNullOrEmpty(appImage)
            ? Path.GetDirectoryName(appImage)!
            : AppContext.BaseDirectory;
        var portable = Path.Combine(baseDir, "SanabiGestor-data");
        if (IsWritable(portable)) return portable;
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SanabiGestor");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(FilePath), JsonCtx.Default.Settings) ?? new();
                loaded.GitHubToken = LocalCredentials.Unprotect(loaded.GitHubToken);
                loaded.NexusApiKey = LocalCredentials.Unprotect(loaded.NexusApiKey);
                return loaded;
            }
        }
        catch (Exception e)
        {
            Log.Write($"No se pudo leer settings.json: {e.Message}");
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDir);
        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(this, JsonCtx.Default.Settings), JsonCtx.Default.Settings)!;
        copy.GitHubToken = LocalCredentials.Protect(GitHubToken);
        copy.NexusApiKey = LocalCredentials.Protect(NexusApiKey);
        var tmp = FilePath + ".tmp";
        var options = new FileStreamOptions { Mode=FileMode.Create, Access=FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(tmp, options))
            JsonSerializer.Serialize(stream, copy, JsonCtx.Default.Settings);
        File.Move(tmp, FilePath, true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

public static class Log
{
    static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
                File.AppendAllText(Path.Combine(Settings.DataDir, "gestor.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // El log nunca debe tumbar la app.
        }
    }
}
