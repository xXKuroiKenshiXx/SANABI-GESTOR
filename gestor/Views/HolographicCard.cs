using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace SanabiGestor.Views;

// A lightweight card surface: pointer position changes the tilt, cover depth
// and rainbow reflection. Interpolation also eases the return to rest.
public sealed class HolographicCard : Border
{
    protected override Type StyleKeyOverride => typeof(Border);
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly SkewTransform tilt = new();
    readonly ScaleTransform scale = new(1, 1);
    readonly TranslateTransform depth = new();
    readonly LinearGradientBrush foil = new()
    {
        GradientStops = new GradientStops
        {
            new(Colors.Transparent, 0), new(Color.Parse("#65F55D91"), .28),
            new(Color.Parse("#708C6BFF"), .42), new(Color.Parse("#804DEBFF"), .55),
            new(Color.Parse("#60FFFFFF"), .64), new(Colors.Transparent, 1)
        }
    };
    Border? reflection;
    InputElement? pointerRoot;
    double x, y, targetX, targetY, hover, targetHover;

    public HolographicCard()
    {
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = new TransformGroup { Children = { tilt, scale, depth } };
        PointerMoved += Move;
        PointerExited += (_, _) => Reset();
        timer.Tick += (_, _) => Animate();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        pointerRoot = e.Root as InputElement;
        pointerRoot?.AddHandler(PointerMovedEvent, RootMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        if (reflection != null || Child == null) return;
        var content = Child;
        Child = null;
        reflection = new Border { Background = foil, Opacity = 0, IsHitTestVisible = false, CornerRadius = CornerRadius };
        var layers = new Grid();
        layers.Children.Add(content); layers.Children.Add(reflection);
        Child = layers;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        pointerRoot?.RemoveHandler(PointerMovedEvent, RootMoved);
        pointerRoot = null;
        timer.Stop(); base.OnDetachedFromVisualTree(e);
    }

    void Reset() { targetX = targetY = targetHover = 0; timer.Start(); }

    void RootMoved(object? sender, PointerEventArgs e)
    {
        // Child controls can capture a pointer; observe the root route as well
        // so leaving the surface always clears its reflection and tilt.
        var point = e.GetPosition(this);
        if (targetHover != 0 && !new Rect(Bounds.Size).Contains(point)) Reset();
    }

    void Move(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        targetX = Math.Clamp(p.X / Math.Max(1, Bounds.Width) * 2 - 1, -1, 1);
        targetY = Math.Clamp(p.Y / Math.Max(1, Bounds.Height) * 2 - 1, -1, 1);
        targetHover = 1; timer.Start();
    }

    void Animate()
    {
        x += (targetX - x) * .18; y += (targetY - y) * .18; hover += (targetHover - hover) * .18;
        tilt.AngleX = -y * 2.8; tilt.AngleY = x * 2.2;
        scale.ScaleX = scale.ScaleY = 1 + .018 * hover;
        depth.X = x * 3; depth.Y = y * 2 - hover * 3;
        foil.StartPoint = new RelativePoint(-.45 + x * .35, -.2 + y * .25, RelativeUnit.Relative);
        foil.EndPoint = new RelativePoint(1.2 + x * .35, 1.1 + y * .25, RelativeUnit.Relative);
        if (reflection != null) reflection.Opacity = hover * .38;
        if (Math.Abs(x - targetX) + Math.Abs(y - targetY) + Math.Abs(hover - targetHover) < .002) timer.Stop();
    }
}
