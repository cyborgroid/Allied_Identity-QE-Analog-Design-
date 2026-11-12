using AlliedID.Shell.Widgets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using static AlliedID.Shell.Look;

namespace AlliedID.Shell.Views;

/// <summary>
/// Phone 모드: 상태 표시줄 / 홈(시계 + 앱 격자 + 독) / 전체화면 앱 / 내비게이션 바.
/// 같은 위젯 컨트롤을 전체 화면으로 띄운다.
/// </summary>
public sealed class PhoneView : UserControl
{
    readonly ShellContext _ctx;
    readonly ContentControl _page = new();
    readonly Stack<(string Kind, string? Arg)> _back = new();

    static readonly string[] Dock = ["dialer", "terminal", "blocks", "settings"];

    public PhoneView(ShellContext ctx)
    {
        _ctx = ctx;
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        grid.Children.Add(StatusBar());
        Grid.SetRow(_page, 1);
        grid.Children.Add(_page);
        var nav = NavBar();
        Grid.SetRow(nav, 2);
        grid.Children.Add(nav);
        Content = grid;
        ShowHome();
        this.WhileAttached(() => ctx.OpenRequested += Open, () => ctx.OpenRequested -= Open);
    }

    Control StatusBar()
    {
        var time = Label("", 13, Text, FontWeight.SemiBold);
        var right = Label("", 12, Muted);
        var bar = new DockPanel { Height = 28, Margin = new Thickness(16, 0) };
        time.VerticalAlignment = right.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        bar.Children.Add(right);
        bar.Children.Add(time);
        bar.OnTick(_ctx, () => time.Text = DateTime.Now.ToString("HH:mm"));
        bar.OnStatus(_ctx, () =>
        {
            var vpn = Directory.Exists("/sys/class/net/aid0") ? "VPN " : "";
            right.Text = $"{vpn}📶 {(_ctx.Status.Initialised ? "🔑" : "🔒")}";
        });
        return bar;
    }

    Control NavBar()
    {
        var back = Flat("◀", Back, 18);
        var home = Flat("●", () => { _back.Clear(); _current = null; ShowHome(); }, 18);
        var apps = Flat("▢", () => Open("tasks", null), 18);
        var g = new UniformGrid { Columns = 3, Height = 48, Background = Bar, Children = { back, home, apps } };
        foreach (var b in g.Children.OfType<Button>()) { b.HorizontalAlignment = HorizontalAlignment.Stretch; b.VerticalAlignment = VerticalAlignment.Stretch; }
        return g;
    }

    void ShowHome()
    {
        var clock = Label("", 64, Text, FontWeight.Thin);
        clock.HorizontalAlignment = HorizontalAlignment.Center;
        var date = Label("", 15, Muted);
        date.HorizontalAlignment = HorizontalAlignment.Center;
        var number = Label("", 14, Accent);
        number.HorizontalAlignment = HorizontalAlignment.Center;

        var appsGrid = new UniformGrid { Columns = 4, Margin = new Thickness(12) };
        var all = _ctx.Layout.SidebarApps.Concat(_ctx.Layout.Widgets.Select(w => w.Kind)).Append("circuit").Distinct().Where(a => !Dock.Contains(a));
        foreach (var a in all) appsGrid.Children.Add(AppIcon(a));
        var dock = new UniformGrid { Columns = 4, Margin = new Thickness(12, 0, 12, 12) };
        foreach (var a in Dock) dock.Children.Add(AppIcon(a));

        var home = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var head = new StackPanel { Margin = new Thickness(0, 40, 0, 0), Spacing = 2, Children = { clock, date, number } };
        home.Children.Add(head);
        var scroll = new ScrollViewer { Content = appsGrid, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(scroll, 1);
        home.Children.Add(scroll);
        var dockBorder = new Border { Background = Look.Brush("#600e1122"), CornerRadius = new CornerRadius(24), Margin = new Thickness(10, 0, 10, 8), Child = dock };
        Grid.SetRow(dockBorder, 2);
        home.Children.Add(dockBorder);

        home.OnTick(_ctx, () =>
        {
            clock.Text = DateTime.Now.ToString("HH:mm");
            date.Text = DateTime.Now.ToString("M월 d일 dddd", System.Globalization.CultureInfo.GetCultureInfo("ko-KR"));
        });
        home.OnStatus(_ctx, () => number.Text = _ctx.Status.Display ?? "");
        clock.Text = DateTime.Now.ToString("HH:mm");
        _page.Content = home;
    }

    Control AppIcon(string kind)
    {
        var (icon, label) = App(kind);
        var b = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(18), Background = SurfaceHi, Child = new TextBlock { Text = icon, FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
                    new TextBlock { Text = label, FontSize = 11, Foreground = Text, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        b.Classes.Add("flat");
        b.Click += (_, _) => Open(kind, null);
        return b;
    }

    (string Kind, string? Arg)? _current;

    void Open(string kind, string? arg)
    {
        if (_current is { } c) _back.Push(c);
        _current = (kind, arg);
        var spec = _ctx.Layout.Widgets.FirstOrDefault(w => w.Kind == kind);
        var title = Label($"{App(kind).Icon}  {spec?.Title ?? App(kind).Label}", 20, Text, FontWeight.SemiBold);
        title.Margin = new Thickness(16, 12);
        var body = new DockPanel();
        DockPanel.SetDock(title, Avalonia.Controls.Dock.Top);
        body.Children.Add(title);
        body.Children.Add(new Border { Padding = new Thickness(kind == "terminal" ? 0 : 16, 0, kind == "terminal" ? 0 : 16, 12), Child = WidgetFactory.Create(_ctx, kind, spec, arg) });
        _page.Content = body;
    }

    void Back()
    {
        if (_back.Count == 0) { _current = null; ShowHome(); return; }
        var (k, a) = _back.Pop();
        _current = null; // Open 이 현재 화면을 다시 스택에 넣지 않도록
        Open(k, a);
    }
}
