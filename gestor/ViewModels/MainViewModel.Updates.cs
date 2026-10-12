using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SanabiGestor.Core;

namespace SanabiGestor.ViewModels;

public partial class MainViewModel
{
    ManagerRelease? availableRelease;
    DispatcherTimer? updateTimer;
    [ObservableProperty] bool checkingUpdate;
    [ObservableProperty] bool updatingManager;
    [ObservableProperty] double managerUpdateProgress;
    [ObservableProperty] string updateStatus = "";
    [ObservableProperty] string releaseNotes = "";
    [ObservableProperty] bool hasUnreadNotification;
    [ObservableProperty] string updateResult = "";
    public bool IsNotificationsSection => Section == 5 && SelectedMod == null;
    public bool HasManagerUpdate => availableRelease != null;
    public bool CanUpdateManager => availableRelease?.CanInstall == true && !UpdatingManager && !IsBusy;
    public string UpdateTitle => UpdateAvailable is { } v ? "Nueva versión del gestor: v" + v : "El gestor está actualizado";
    public string NotificationLabel => HasUnreadNotification ? "Actualizaciones · 1" : "Actualizaciones";
    public string UpdateFormatHint => !AppUpdater.IsPortableExecutable
        ? "Las actualizaciones integradas requieren el EXE portable o AppImage. Esta carpeta de desarrollo se actualiza manualmente."
        : availableRelease != null && !availableRelease.CanInstall ? "Esta release todavía no tiene un ejecutable con SHA-256 para tu plataforma. Podés abrir la release." : "";

    partial void OnUpdatingManagerChanged(bool value) => OnPropertyChanged(nameof(CanUpdateManager));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanUpdateManager));
    partial void OnHasUnreadNotificationChanged(bool value) => OnPropertyChanged(nameof(NotificationLabel));
    partial void OnUpdateAvailableChanged(string? value) => OnPropertyChanged(nameof(UpdateTitle));

    void StartUpdateNotifications()
    {
        ReadUpdateResult();
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        updateTimer.Tick += (_, _) => _ = CheckManagerUpdateAsync();
        updateTimer.Start();
        _ = CheckManagerUpdateAsync();
    }

    void ReadUpdateResult()
    {
        try
        {
            var parent = Path.GetDirectoryName(Platform.ExecutablePath)!;
            var last = Directory.EnumerateDirectories(parent, ".SanabiGestor-update-*")
                .Select(d => Path.Combine(d, "result.txt")).Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (last != null) UpdateResult = File.ReadAllText(last);
        }
        catch { }
    }

    [RelayCommand]
    async Task CheckManagerUpdateAsync()
    {
        if (CheckingUpdate || UpdatingManager) return;
        CheckingUpdate = true;
        try
        {
            var release = await AppUpdater.CheckAsync(http, lifetime.Token);
            SetAvailableRelease(release);
            UpdateStatus = "Última comprobación: " + DateTime.Now.ToString("HH:mm");
        }
        catch (OperationCanceledException) { UpdateStatus = "No se pudo comprobar la actualización. Intentá nuevamente."; }
        catch (Exception e) { UpdateStatus = "No se pudo comprobar la actualización. " + e.Message; }
        finally { CheckingUpdate = false; }
    }

    // Shared with the UI tests; receiving a release never installs it automatically.
    public void SetAvailableRelease(ManagerRelease? release)
    {
        if (release != null && GameInstall.CompareVersions(release.Version, AppInfo.Version) <= 0) release = null;
        availableRelease = release; UpdateAvailable = release?.Version; ReleaseNotes = release?.Notes ?? "";
        HasUnreadNotification = release != null && settings.ReadReleaseVersion != release.Version;
        if (IsNotificationsSection) MarkReleaseRead();
        OnPropertyChanged(nameof(HasManagerUpdate)); OnPropertyChanged(nameof(CanUpdateManager)); OnPropertyChanged(nameof(UpdateFormatHint));
    }

    void MarkReleaseRead()
    {
        if (availableRelease != null) { settings.ReadReleaseVersion = availableRelease.Version; settings.Save(); }
        HasUnreadNotification = false;
    }

    [RelayCommand]
    void ShowNotifications() { Section = 5; SelectedMod = null; MarkReleaseRead(); }

    [RelayCommand]
    async Task UpdateManagerAsync()
    {
        if (!CanUpdateManager || availableRelease == null) return;
        var release = availableRelease;
        var answer = await AskAsync("Actualizar el gestor", "Se descargará la nueva versión y el gestor se reiniciará. Tus ajustes y mods instalados se conservan.", "Actualizar y reiniciar", "Cancelar");
        if (answer != "Actualizar y reiniciar") return;
        if (!CanUpdateManager) return;
        UpdatingManager = true; ManagerUpdateProgress = 0;
        try
        {
            UpdateStatus = "Descargando y verificando el gestor…";
            var stage = await AppUpdater.PrepareAsync(http, release, new Progress<double>(p => ManagerUpdateProgress = p * 100), lifetime.Token);
            AppUpdater.LaunchHelper(stage);
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
        catch (Exception e) { UpdateStatus = e.Message; await Alert("No se pudo actualizar", e.Message); }
        finally { UpdatingManager = false; }
    }
}
