using Avalonia;
using SanabiGestor.Core;

namespace SanabiGestor;

static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update")
        { Environment.ExitCode = AppUpdater.ApplyJob(args[1]); return; }
        // Si ya hay una ventana abierta y nos llaman con un enlace nxm://, se lo pasamos y salimos.
        var link = args.FirstOrDefault(a => a.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase));
        if (link != null && Platform.TrySendToRunningInstance(link)) return;

        StartupLink = link;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static string? StartupLink { get; private set; }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
