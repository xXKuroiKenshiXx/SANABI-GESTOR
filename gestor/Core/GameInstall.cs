using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace SanabiGestor.Core;

public sealed class GameInstall
{
    public const string ExeName = "SNB.exe";
    public const string DataFolder = "SNB_Data";
    public const string AddressablesRel = "SNB_Data/StreamingAssets/aa/StandaloneWindows64";

    public string Root { get; }
    public string? Version { get; }

    GameInstall(string root)
    {
        Root = root;
        Version = ReadVersion(root);
    }

    public string Abs(string rel) => System.IO.Path.Combine(Root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public string AddressablesDir => Abs(AddressablesRel);

    public static bool IsValid(string? dir) =>
        !string.IsNullOrWhiteSpace(dir)
        && File.Exists(System.IO.Path.Combine(dir, ExeName))
        && Directory.Exists(System.IO.Path.Combine(dir, DataFolder));

    public static GameInstall? Open(string? dir)
    {
        if (!IsValid(dir))
        {
            // Permitir elegir la carpeta SNB_Data o una carpeta que contenga "SANABI".
            if (dir is null) return null;
            var parent = System.IO.Path.GetDirectoryName(dir.TrimEnd('/', '\\'));
            if (IsValid(parent)) return new GameInstall(parent!);
            var child = System.IO.Path.Combine(dir, "SANABI");
            return IsValid(child) ? new GameInstall(child) : null;
        }
        return new GameInstall(System.IO.Path.GetFullPath(dir!));
    }

    public static bool IsRunning()
    {
        try
        {
            return Process.GetProcessesByName("SNB").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    // La version del juego (ej. 1.3.53) esta como texto dentro de globalgamemanagers.
    static string? ReadVersion(string root)
    {
        try
        {
            var path = System.IO.Path.Combine(root, DataFolder, "globalgamemanagers");
            using var fs = File.OpenRead(path);
            var buf = new byte[Math.Min(fs.Length, 512 * 1024)];
            fs.ReadExactly(buf);
            var text = Encoding.Latin1.GetString(buf);
            var m = Regex.Match(text, @"(?<![\d.])(\d{1,2}\.\d{1,3}\.\d{1,4})\0");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    public static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string sp) roots.Add(sp);
            }
            catch
            {
                // Sin registro de Steam: seguimos con las rutas tipicas.
            }
            roots.Add(@"C:\Program Files (x86)\Steam");
            roots.Add(@"C:\Program Files\Steam");
        }
        else
        {
            roots.Add(System.IO.Path.Combine(home, ".steam/steam"));
            roots.Add(System.IO.Path.Combine(home, ".local/share/Steam"));
            roots.Add(System.IO.Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"));
            roots.Add(System.IO.Path.Combine(home, "snap/steam/common/.local/share/Steam"));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            if (seen.Add(System.IO.Path.GetFullPath(root))) yield return root;
            var vdf = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            string content;
            try { content = File.ReadAllText(vdf); } catch { continue; }
            foreach (Match m in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
            {
                var lib = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(lib) && seen.Add(System.IO.Path.GetFullPath(lib))) yield return lib;
            }
        }
    }

    public static string? AutoDetect()
    {
        foreach (var lib in SteamLibraries())
        {
            var candidate = System.IO.Path.Combine(lib, "steamapps", "common", "SANABI");
            if (IsValid(candidate)) return candidate;
        }
        return null;
    }

    // Compara versiones tipo 1.3.53. Los requisitos son "1.3.53", ">=1.3.48", "<1.3.48" o varios separados por coma.
    public static bool Satisfies(string? version, string? requirement)
    {
        if (string.IsNullOrWhiteSpace(requirement) || string.IsNullOrWhiteSpace(version)) return true;
        foreach (var raw in requirement.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var m = Regex.Match(raw, @"^(>=|<=|>|<|=|==)?\s*([\d.]+)$");
            if (!m.Success) continue;
            var cmp = CompareVersions(version, m.Groups[2].Value);
            var ok = m.Groups[1].Value switch
            {
                ">=" => cmp >= 0,
                "<=" => cmp <= 0,
                ">" => cmp > 0,
                "<" => cmp < 0,
                _ => cmp == 0,
            };
            if (!ok) return false;
        }
        return true;
    }

    public static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var x = i < pa.Length && int.TryParse(pa[i], out var xi) ? xi : 0;
            var y = i < pb.Length && int.TryParse(pb[i], out var yi) ? yi : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
}
