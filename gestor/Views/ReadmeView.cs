using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SanabiGestor.Views;

// Render repository prose without interpreting HTML, scripts or instructions.
// Original Markdown remains selectable, including URL destinations.
public sealed class ReadmeView : StackPanel
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ReadmeView, string?>(nameof(Text));
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) Render();
    }

    void Render()
    {
        Children.Clear(); Spacing = 10;
        var code = false;
        var codeLines = new List<string>();
        foreach (var line in (Text ?? "").Replace("\r", "").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```"))
            {
                if (code) { AddCode(string.Join('\n', codeLines)); codeLines.Clear(); }
                code = !code; continue;
            }
            if (code) { codeLines.Add(line); continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (heading.Success)
                Children.Add(new SelectableTextBlock { Text = heading.Groups[2].Value, FontSize = heading.Groups[1].Length == 1 ? 22 : 16,
                    FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            else
            {
                var body = new SelectableTextBlock { Text = Regex.Replace(line, @"^\s*[-*]\s", "• "),
                    TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
                body.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("TextBrush")); Children.Add(body);
            }
        }
        if (codeLines.Count > 0) AddCode(string.Join('\n', codeLines));
    }

    void AddCode(string text) => Children.Add(new Border
    {
        Background = Brush.Parse("#0D0B19"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14),
        Child = new SelectableTextBlock { Text = text, FontFamily = new FontFamily("monospace"), FontSize = 12,
            TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#66DFF4") }
    });
}
