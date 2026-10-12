using System.Diagnostics;
using System.IO.Pipes;

namespace SanabiGestor.Core;

public static class Platform
{
    const string PipeName = "SanabiGestor-ipc";

    public static string ExecutablePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage
            ? appImage
            : Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "SanabiGestor");

    public static void Open(string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { target }, UseShellExecute = false });
        }
        catch (Exception e)
        {
            Log.Write($"No se pudo abrir {target}: {e.Message}");
        }
    }

    public static void LaunchGame() => Open($"steam://rungameid/{AppInfo.SteamAppId}");

    // ---- Una sola instancia: si llega un enlace nxm:// y la app ya esta abierta, se lo pasamos ----

    public static bool TrySendToRunningInstance(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(400);
            using var writer = new StreamWriter(client);
            writer.WriteLine(message);
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ListenForMessages(Action<string> onMessage, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(ct);
                    if (!string.IsNullOrWhiteSpace(line)) onMessage(line.Trim());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Log.Write($"IPC: {e.Message}");
                    await Task.Delay(1000, ct);
                }
            }
        }, ct);
    }

    // ---- Asociar los enlaces nxm:// (boton "Mod Manager Download" de Nexus) a este programa ----

    public static string? NxmHandler()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Classes\nxm\shell\open\command");
                return key?.GetValue(null) as string;
            }
            var psi = new ProcessStartInfo("xdg-mime") { ArgumentList = { "query", "default", "x-scheme-handler/nxm" }, RedirectStandardOutput = true };
            using var p = Process.Start(psi);
            var output = p?.StandardOutput.ReadToEnd().Trim();
            p?.WaitForExit(2000);
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    public static bool NxmIsOurs()
    {
        var handler = NxmHandler();
        if (handler is null) return false;
        return OperatingSystem.IsWindows()
            ? handler.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase)
            : handler.Equals("sanabi-gestor-nxm.desktop", StringComparison.OrdinalIgnoreCase);
    }

    public static void RegisterNxm()
    {
        if (OperatingSystem.IsWindows())
        {
            using var root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\nxm");
            root.SetValue(null, "URL:NXM Protocol");
            root.SetValue("URL Protocol", "");
            using var cmd = root.CreateSubKey(@"shell\open\command");
            cmd.SetValue(null, $"\"{ExecutablePath}\" \"%1\"");
            return;
        }

        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "sanabi-gestor-nxm.desktop"), $"""
            [Desktop Entry]
            Type=Application
            Name=SANABI Gestor (enlaces nxm)
            Exec="{ExecutablePath}" %u
            MimeType=x-scheme-handler/nxm;
            NoDisplay=true
            Terminal=false
            """);
        Run("xdg-mime", "default", "sanabi-gestor-nxm.desktop", "x-scheme-handler/nxm");
        Run("update-desktop-database", apps);
    }

    static void Run(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch (Exception e)
        {
            Log.Write($"{file}: {e.Message}");
        }
    }
}
