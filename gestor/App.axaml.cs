using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SanabiGestor.ViewModels;
using SanabiGestor.Views;

namespace SanabiGestor;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var vm = new MainViewModel(window);
            window.DataContext = vm;
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.Shutdown();
            window.Opened += async (_, _) => await vm.StartAsync(Program.StartupLink);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
