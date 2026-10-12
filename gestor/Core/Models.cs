using System.Text.Json.Serialization;

namespace SanabiGestor.Core;

// mod.json: describe un mod. Va en mods/<id>/mod.json del repo y opcionalmente dentro de un .zip.
public sealed class ModManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "otros";
    public List<string> Tags { get; set; } = new();
    public List<string> Requires { get; set; } = new();
    public string? GameVersion { get; set; }
    public string? Homepage { get; set; }
    public string? Preview { get; set; }
    public string? Base { get; set; }
    public List<ModFileMap>? Files { get; set; }
}

public sealed class ModFileMap
{
    public string Src { get; set; } = "";
    public string Dst { get; set; } = "";
}

public sealed class Catalog
{
    public int Schema { get; set; }
    public string? Generated { get; set; }
    public List<CatalogEntry> Mods { get; set; } = new();
}

public sealed class CatalogEntry
{
    public string AssetUrl(string path) => AppInfo.RawUrl(path);
    public string? Readme { get; set; }
    public List<CatalogVariant> Variants { get; set; } = new();
    public string? InstallInstructions { get; set; }
    public string? CompatibilityNotes { get; set; }
    public string? Verification { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "otros";
    public List<string> Tags { get; set; } = new();
    public List<string> Requires { get; set; } = new();
    public string? GameVersion { get; set; }
    public string? Homepage { get; set; }
    public string? Preview { get; set; }
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public List<CatalogFile> Files { get; set; } = new();
}

public sealed class CatalogVariant
{
    public string Label { get; set; } = "";
    public string Version { get; set; } = "";
    public string? GameVersion { get; set; }
    public string? CompatibilityNotes { get; set; }
    public List<CatalogFile> Files { get; set; } = new();
    public override string ToString() => Label;
}

public sealed class CatalogFile
{
    public string Src { get; set; } = "";
    public string Dst { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
}

// Estado guardado dentro de la carpeta del juego: <juego>/SanabiGestor/state.json
public sealed class GameState
{
    public int Schema { get; set; } = 1;
    public string? GameVersion { get; set; }
    public Dictionary<string, InstalledMod> Mods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FileRecord> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public BepInExRecord? BepInEx { get; set; }
}

public sealed class InstalledMod
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public string Source { get; set; } = "local";
    public string? SourceRef { get; set; }
    public bool Enabled { get; set; }
    public bool NeedsReapply { get; set; }
    public List<string> Files { get; set; } = new();
    public List<string> Requires { get; set; } = new();
    public string InstalledAt { get; set; } = "";
}

public sealed class FileRecord
{
    // Pila de mods que tocan este archivo; el ultimo es el que esta aplicado.
    public List<string> Owners { get; set; } = new();
    public bool OriginalExisted { get; set; }
    public long Size { get; set; }
    public long WriteTicks { get; set; }
}

public sealed class BepInExRecord
{
    public string Version { get; set; } = "";
    public List<string> Files { get; set; } = new();
}

// github-mods.json: repos de la comunidad revisados a mano (si son de SANABI y como se instalan) y repos bloqueados.
public sealed class CommunityList
{
    public int Schema { get; set; }
    public List<CommunityRepo> Repos { get; set; } = new();
    public List<string> Blocked { get; set; } = new();
}

public sealed class CommunityRepo
{
    public string Repo { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Kind { get; set; } = "mod";
    public string Description { get; set; } = "";
    public string Install { get; set; } = "release";
    public string? Version { get; set; }
    public string? Note { get; set; }
    public string? Preview { get; set; }
    public List<string> Requires { get; set; } = new();
    public List<ModFileMap>? Files { get; set; }
}

public sealed class GitHubDeviceCode
{
    [JsonPropertyName("device_code")] public string DeviceCode { get; set; } = "";
    [JsonPropertyName("user_code")] public string UserCode { get; set; } = "";
    [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; } = "";
    [JsonPropertyName("interval")] public int Interval { get; set; } = 5;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; } = 900;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(UpdateJob))]
[JsonSerializable(typeof(ModManifest))]
[JsonSerializable(typeof(Catalog))]
[JsonSerializable(typeof(GameState))]
[JsonSerializable(typeof(GitHubDeviceCode))]
[JsonSerializable(typeof(CommunityList))]
public partial class JsonCtx : JsonSerializerContext;
