using AlliedID.Shell.Core.Model;
using AlliedID.Shell.Core.Platform;
using AlliedID.Shell.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AlliedID.Shell;

/// <summary>
/// 최상위 창. 모드에 따라 <see cref="DesktopView"/> 와 <see cref="PhoneView"/> 를 갈아 끼운다.
/// 우선순위: --mode 인자 → desk.aid 의 mode → /run/alliedid/mode.json → 창 비율.
/// </summary>
public sealed class MainWindow : Window
{
    readonly ShellContext _ctx = new();
    readonly ShellOptions _opts;
    readonly ModeSource _modeSource = new();
    readonly Panel _root = new();
    readonly Border _toast;
    readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    ShellMode _mode = ShellMode.Auto;

    public MainWindow(ShellOptions opts)
    {
        _opts = opts;
        Title = "AlliedID 26.11.1 LTS";
        Width = opts.Width;
        Height = opts.Height;
        MinWidth = 320;
        MinHeight = 480;
        Background = Look.Brush(_ctx.Layout.Wallpaper);
        if (opts.FullScreen)
        {
            WindowState = WindowState.FullScreen;
            SystemDecorations = SystemDecorations.None;
        }

        _toast = new Border
        {
            Background = Look.Brush("#e0101428"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 72),
            IsVisible = false,
            Child = Look.Label("", 14),
        };
        _toastTimer.Tick += (_, _) => { _toast.IsVisible = false; _toastTimer.Stop(); };
        _ctx.Notified += ShowToast;

        Content = new Panel { Children = { _root, _toast } };

        _ctx.LayoutChanged += () =>
        {
            Background = Look.Brush(_ctx.Layout.Wallpaper);
            Rebuild(force: true);
        };
        _modeSource.Changed += _ => Dispatcher.UIThread.Post(() => Rebuild());
        SizeChanged += (_, _) => Rebuild();
        Opened += (_, _) =>
        {
            Rebuild(force: true);
            foreach (var e in _ctx.LayoutErrors) Console.Error.WriteLine($"desk.aid: {e}");
        };
        Closed += (_, _) => { _modeSource.Dispose(); _ctx.Dispose(); };
    }

    ShellMode Resolve()
    {
        if (_opts.Mode != ShellMode.Auto) return _opts.Mode;
        if (_ctx.Layout.Mode != ShellMode.Auto) return _ctx.Layout.Mode;
        return _modeSource.Current ?? ModeSource.FromAspect(Bounds.Width > 0 ? Bounds.Width : Width, Bounds.Height > 0 ? Bounds.Height : Height);
    }

    void Rebuild(bool force = false)
    {
        var m = Resolve();
        if (!force && m == _mode && _root.Children.Count > 0) return;
        if (m != _mode && _root.Children.Count > 0) ShowToast(m == ShellMode.Phone ? "📱 Phone 모드" : "🖥 Desktop 모드");
        _mode = m;
        foreach (var c in _root.Children.OfType<IDisposable>()) c.Dispose();
        _root.Children.Clear();
        _root.Children.Add(m == ShellMode.Phone ? new PhoneView(_ctx) : new DesktopView(_ctx));
    }

    void ShowToast(string message)
    {
        ((TextBlock)_toast.Child!).Text = message;
        _toast.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }
}
