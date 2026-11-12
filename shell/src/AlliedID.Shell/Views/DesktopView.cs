using AlliedID.Shell.Core.Model;
using AlliedID.Shell.Widgets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static AlliedID.Shell.Look;

namespace AlliedID.Shell.Views;

/// <summary>
/// Desktop 모드: 상단바 / 사이드바 / 조립 가능한 바탕화면(캔버스) / 작업 표시줄.
/// 위젯은 제목 표시줄을 끌어서 옮기고, "배치 저장" 으로 desk.aid 에 기록한다.
/// </summary>
public sealed class DesktopView : UserControl
{
    readonly ShellContext _ctx;
    readonly Canvas _canvas = new() { ClipToBounds = true };
    readonly StackPanel _taskbar = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    readonly List<Frame> _frames = [];
    int _z;

    sealed record Frame(Border Root, string Kind, string Title, Widget? Spec);

    public DesktopView(ShellContext ctx)
    {
        _ctx = ctx;
        var layout = ctx.Layout;

        var root = new DockPanel();
        var top = TopBar();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var task = new Border { Background = Bar, Height = 44, Padding = new Thickness(8, 0), Child = _taskbar };
        DockPanel.SetDock(task, Dock.Bottom);
        root.Children.Add(task);

        var side = Sidebar(layout);
        DockPanel.SetDock(side, layout.SidebarSide switch { SidebarSide.Right => Dock.Right, SidebarSide.Bottom => Dock.Bottom, _ => Dock.Left });
        root.Children.Add(side);

        root.Children.Add(_canvas);
        Content = root;

        foreach (var w in layout.Widgets) AddFrame(w.Kind, w.Title ?? App(w.Kind).Label, w.X, w.Y, w.Width, w.Height, w, closable: false);

        this.WhileAttached(() => ctx.OpenRequested += Open, () => ctx.OpenRequested -= Open);
    }

    Control TopBar()
    {
        var clock = Label("", 13);
        var who = Label("", 13, Accent);
        var bar = new DockPanel { Height = 34, Background = Bar };
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Label("◆ AlliedID", 14, Accent, FontWeight.SemiBold), Label(_ctx.Layout.Name, 13, Muted) },
        };
        var save = Flat("💾 배치 저장", SaveArrangement, 12);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12, 0), VerticalAlignment = VerticalAlignment.Center, Children = { save, who, clock } };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(left);
        bar.Children.Add(right);
        bar.Children.Add(new Control());
        bar.OnTick(_ctx, () => clock.Text = DateTime.Now.ToString("M/d (ddd) HH:mm"));
        bar.OnStatus(_ctx, () => who.Text = _ctx.Status.Initialised ? $"🔑 {_ctx.Status.Display}" : "🔒 신원 없음");
        return bar;
    }

    Control Sidebar(DeskLayout layout)
    {
        var vertical = layout.SidebarSide != SidebarSide.Bottom;
        var panel = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = 4, Margin = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var app in layout.SidebarApps)
        {
            var (icon, label) = App(app);
            var b = new Button { Content = icon, FontSize = 22, Width = 52, Height = 52, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            b.Classes.Add("flat");
            ToolTip.SetTip(b, label);
            b.Click += (_, _) => Open(app, null);
            panel.Children.Add(b);
        }
        return new Border { Background = Look.Brush("#c00e1122"), Child = panel };
    }

    void Open(string kind, string? arg)
    {
        // 이미 열린 앱이면 앞으로 (인자가 있는 다이얼러는 새로)
        var existing = _frames.FirstOrDefault(f => f.Kind == kind && f.Spec is null);
        if (existing is not null && arg is null) { Raise(existing.Root); return; }
        if (existing is not null) Close(existing);
        var (w, h) = kind switch { "dialer" => (360, 560), "terminal" => (720, 440), "settings" => (460, 560), _ => (480, 380) };
        var n = _frames.Count(f => f.Spec is null);
        AddFrame(kind, App(kind).Label, 120 + n * 28, 60 + n * 28, w, h, null, closable: true, arg);
    }

    void AddFrame(string kind, string title, double x, double y, double w, double h, Widget? spec, bool closable, string? arg = null)
    {
        var content = WidgetFactory.Create(_ctx, kind, spec, arg);
        var titleText = Label($"{App(kind).Icon}  {title}", 13, Text, FontWeight.SemiBold);
        var head = new DockPanel { Background = SurfaceHi, Height = 32, Cursor = new Cursor(StandardCursorType.SizeAll) };
        Frame? frame = null;
        if (closable)
        {
            var close = Flat("✕", () => Close(frame!), 12);
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
        }
        titleText.VerticalAlignment = VerticalAlignment.Center;
        titleText.Margin = new Thickness(12, 0);
        head.Children.Add(titleText);

        var body = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);
        body.Children.Add(new Border { Padding = new Thickness(kind == "terminal" ? 0 : 12), Child = content });

        var border = new Border
        {
            Width = w,
            Height = h,
            Background = Surface,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            BoxShadow = BoxShadows.Parse("0 8 24 0 #66000000"),
            Child = body,
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        frame = new Frame(border, kind, title, spec);
        _frames.Add(frame);
        _canvas.Children.Add(border);
        Raise(border);
        MakeDraggable(head, border);
        if (closable) RefreshTaskbar();
        border.PointerPressed += (_, _) => Raise(border);
    }

    void MakeDraggable(Control handle, Control target)
    {
        Point? start = null;
        double ox = 0, oy = 0;
        handle.PointerPressed += (_, e) =>
        {
            start = e.GetPosition(_canvas);
            ox = Canvas.GetLeft(target);
            oy = Canvas.GetTop(target);
            e.Pointer.Capture(handle);
        };
        handle.PointerMoved += (_, e) =>
        {
            if (start is not { } s) return;
            var p = e.GetPosition(_canvas);
            // 8px 격자에 맞춘다 (조립하는 느낌)
            Canvas.SetLeft(target, Math.Max(0, Math.Round((ox + p.X - s.X) / 8) * 8));
            Canvas.SetTop(target, Math.Max(0, Math.Round((oy + p.Y - s.Y) / 8) * 8));
        };
        handle.PointerReleased += (_, e) => { start = null; e.Pointer.Capture(null); };
    }

    void Raise(Control c) => c.ZIndex = ++_z;

    void Close(Frame f)
    {
        _frames.Remove(f);
        _canvas.Children.Remove(f.Root);
        RefreshTaskbar();
    }

    void RefreshTaskbar()
    {
        _taskbar.Children.Clear();
        _taskbar.Children.Add(Label("작업", 12, Muted));
        foreach (var f in _frames.Where(f => f.Spec is null))
        {
            var b = new Button { Content = $"{App(f.Kind).Icon} {f.Title}", FontSize = 12 };
            b.Classes.Add("flat");
            b.Click += (_, _) => Raise(f.Root);
            _taskbar.Children.Add(b);
        }
    }

    /// <summary>현재 위젯 위치/크기를 desk.aid 로 저장 → 파일 감시가 재조립.</summary>
    void SaveArrangement()
    {
        if (!_ctx.Has("desktop.edit")) { _ctx.Notify("🔒 desktop.edit 권한이 없습니다"); return; }
        var widgets = _frames.Select(f => new Widget(
            f.Kind,
            f.Spec?.Title ?? (f.Spec is null ? f.Title : null),
            Canvas.GetLeft(f.Root), Canvas.GetTop(f.Root), f.Root.Width, f.Root.Height,
            f.Spec?.Props ?? new Dictionary<string, string>())).ToList();
        _ctx.Desk.Save(_ctx.Layout with { Widgets = widgets });
    }
}
