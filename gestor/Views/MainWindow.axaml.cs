using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.VisualTree;
using SanabiGestor.ViewModels;

namespace SanabiGestor.Views;

public partial class MainWindow : Window, IUiHost
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
                vm.ConsoleLines.CollectionChanged += (_, _) => ConsoleScroll.ScrollToEnd();
        };
    }

    void OnModCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && source.GetVisualAncestors().Prepend(source).Any(v => v is Button or ToggleSwitch)) return;
        switch ((sender as Control)?.DataContext)
        {
            case ModCardViewModel card: card.OpenDetailsCommand.Execute(null); break;
            case InstalledModViewModel item: item.OpenDetailsCommand.Execute(null); break;
            case NexusModViewModel item: item.OpenDetailsCommand.Execute(null); break;
            default: return;
        }
        e.Handled = true;
    }

    static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p != null);
        if (path != null && DataContext is MainViewModel vm) await vm.InstallLocalPathAsync(path);
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> PickFileAsync(string title, string[] patterns)
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Mods") { Patterns = patterns }, FilePickerFileTypes.All],
        });
        return result.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.'),
        });
        return file?.TryGetLocalPath();
    }

    public async Task CopyAsync(string text)
    {
        if (Clipboard != null) await Clipboard.SetTextAsync(text);
    }

    public void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }
}
