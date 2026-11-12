using System.Globalization;
using AlliedID.Shell.Core.Model;
using AlliedID.Shell.Core.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using static AlliedID.Shell.Look;

namespace AlliedID.Shell.Widgets;

/// <summary>위젯/앱 kind → 컨트롤. 데스크톱(창)과 폰(전체화면) 이 같은 컨트롤을 쓴다.</summary>
public static class WidgetFactory
{
    public const string BlocksUrl = "http://127.0.0.1:8626/";

    public static Control Create(ShellContext ctx, string kind, Widget? spec = null, string? arg = null) => kind switch
    {
        "clock" => Clock(ctx),
        "terminal" => Terminal(ctx, spec?.Prop("command") ?? ""),
        "files" => Files(ctx),
        "packages" => Packages(ctx),
        "tasks" => Tasks(ctx),
        "dialer" => Dialer(ctx, arg),
        "note" => Note(spec?.Prop("text") ?? ""),
        "network" => Network(ctx),
        "circuit" => Circuit(ctx),
        "settings" => Settings(ctx),
        "blocks" => Blocks(ctx),
        "android" => Android(ctx),
        _ => Label($"알 수 없는 위젯: {kind}", 14, Muted),
    };

    static Control Scroll(Control c) => new ScrollViewer { Content = c, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

    // ── 시계 ────────────────────────────────────────────────────────────
    static Control Clock(ShellContext ctx)
    {
        var time = Label("", 44, Text, FontWeight.Light);
        var date = Label("", 15, Muted);
        var num = Label("", 14, Accent);
        void Update()
        {
            var now = DateTime.Now;
            time.Text = now.ToString("HH:mm:ss");
            date.Text = now.ToString("yyyy년 M월 d일 dddd", CultureInfo.GetCultureInfo("ko-KR"));
        }
        Update();
        var panel = new StackPanel { Spacing = 2, Children = { time, date, num } };
        panel.OnTick(ctx, Update);
        panel.OnStatus(ctx, () => num.Text = ctx.Status.Display ?? "기기 신원 없음");
        return panel;
    }

    // ── 터미널 (스크립트) ────────────────────────────────────────────────
    static Control Terminal(ShellContext ctx, string initial)
    {
        var output = new SelectableTextBlock { FontFamily = Mono, FontSize = 13, Foreground = Text, TextWrapping = TextWrapping.Wrap };
        var scroll = new ScrollViewer { Content = output };
        var input = new TextBox { FontFamily = Mono, Watermark = "명령 입력 후 Enter  (예: aid status)", Background = Bar };
        var history = new List<string>();
        var hIndex = 0;
        var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        async Task Run(string cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return;
            history.Add(cmd);
            hIndex = history.Count;
            output.Text += $"aid@alliedid:{cwd.Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~")}$ {cmd}\n";
            if (cmd.Trim() == "clear") { output.Text = ""; return; }
            if (cmd.Trim().StartsWith("cd"))
            {
                var target = cmd.Trim()[2..].Trim();
                var next = target.Length == 0 ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : Path.GetFullPath(Path.Combine(cwd, target.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))));
                if (Directory.Exists(next)) cwd = next; else output.Text += $"cd: {target}: 그런 디렉터리가 없습니다\n";
                return;
            }
            var r = await SystemInfo.ShellAsync(cmd, cwd);
            output.Text += r.Stdout + r.Stderr;
            scroll.ScrollToEnd();
        }

        input.AddHandler(InputElement.KeyDownEvent, async (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Enter:
                    var cmd = input.Text ?? "";
                    input.Text = "";
                    e.Handled = true;
                    await Run(cmd);
                    break;
                case Key.Up when history.Count > 0:
                    hIndex = Math.Max(0, hIndex - 1);
                    input.Text = history[hIndex];
                    input.CaretIndex = input.Text.Length;
                    e.Handled = true;
                    break;
                case Key.Down when history.Count > 0:
                    hIndex = Math.Min(history.Count, hIndex + 1);
                    input.Text = hIndex < history.Count ? history[hIndex] : "";
                    e.Handled = true;
                    break;
            }
        }, RoutingStrategies.Tunnel);

        var external = Flat("↗ 전체 터미널", () => ctx.Launch("x-terminal-emulator"), 12);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Background = Bar };
        grid.Children.Add(new Border { Padding = new Thickness(8), Child = scroll });
        var bottom = new DockPanel { Margin = new Thickness(4) };
        DockPanel.SetDock(external, Dock.Right);
        bottom.Children.Add(external);
        bottom.Children.Add(input);
        Grid.SetRow(bottom, 1);
        grid.Children.Add(bottom);
        if (!string.IsNullOrWhiteSpace(initial)) grid.AttachedToVisualTree += async (_, _) => { if (output.Text is null or "") await Run(initial); };
        return grid;
    }

    // ── 열린 파일 ───────────────────────────────────────────────────────
    static Control Files(ShellContext ctx)
    {
        var list = new StackPanel { Spacing = 2 };
        void Refresh()
        {
            list.Children.Clear();
            var files = SystemInfo.RecentFiles(14);
            if (files.Count == 0) list.Children.Add(Label("최근 파일이 없습니다", 13, Muted));
            foreach (var f in files)
            {
                var row = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = new DockPanel
                    {
                        Children =
                        {
                            DockRight(Label(f.Modified.ToString("MM-dd HH:mm"), 11, Muted)),
                            Label($"📄 {f.Name}", 13),
                        },
                    },
                };
                row.Classes.Add("flat");
                ToolTip.SetTip(row, f.Path);
                row.Click += (_, _) => ctx.Launch("xdg-open", f.Path);
                list.Children.Add(row);
            }
        }
        Refresh();
        var root = new DockPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(Flat("⟳ 새로고침", Refresh, 12));
        top.Children.Add(Flat("🗂 파일 관리자", () => ctx.Launch("xdg-open", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), 12));
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(Scroll(list));
        return root;
    }

    static Control DockRight(Control c) { DockPanel.SetDock(c, Dock.Right); c.Margin = new Thickness(8, 0, 0, 0); return c; }

    // ── 패키지 ─────────────────────────────────────────────────────────
    static Control Packages(ShellContext ctx)
    {
        var search = new TextBox { Watermark = "패키지 검색 / 설치할 이름" };
        var list = new StackPanel { Spacing = 1 };
        var count = Label("", 12, Muted);
        async Task Refresh()
        {
            var pkgs = await SystemInfo.PackagesAsync(string.IsNullOrWhiteSpace(search.Text) ? null : search.Text.Trim());
            list.Children.Clear();
            count.Text = $"{pkgs.Count}개 설치됨";
            foreach (var p in pkgs.Take(80))
                list.Children.Add(new DockPanel { Children = { DockRight(Label(p.Version, 11, Muted)), Label(p.Name, 13) } });
        }
        search.TextChanged += async (_, _) => await Refresh();
        var install = new Button { Content = "설치" };
        install.Classes.Add("go");
        install.Click += (_, _) =>
        {
            var name = search.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (!ctx.Has("apps.install")) { ctx.Notify("🔒 apps.install 권한이 없습니다"); return; }
            // aid-install 은 sudoers 로 허용된 유일한 관리 명령이며, 스스로 권한(apps.install)을 다시 확인한다.
            ctx.Launch("x-terminal-emulator", "-e", "sudo", "/usr/libexec/alliedid/aid-install", name);
        };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 6), Children = { DockRight(install), search } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(count, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(count);
        root.Children.Add(Scroll(list));
        root.AttachedToVisualTree += async (_, _) => await Refresh();
        return root;
    }

    // ── 작업 ───────────────────────────────────────────────────────────
    static Control Tasks(ShellContext ctx)
    {
        var list = new StackPanel { Spacing = 1 };
        var mem = Label("", 12, Muted);
        var n = 0;
        void Refresh()
        {
            if (n++ % 3 != 0) return;
            list.Children.Clear();
            foreach (var p in SystemInfo.TopProcesses(14))
                list.Children.Add(new DockPanel
                {
                    Children =
                    {
                        DockRight(Label($"{p.RssKb / 1024.0:0.0} MB", 12, Muted)),
                        Label($"{p.Pid,6}  {p.Name}", 13),
                    },
                });
            try
            {
                var lines = File.ReadAllLines("/proc/meminfo");
                long Get(string k) => long.Parse(lines.First(l => l.StartsWith(k)).Split(':')[1].Trim().Split(' ')[0]);
                var total = Get("MemTotal"); var avail = Get("MemAvailable");
                mem.Text = $"메모리 {(total - avail) / 1048576.0:0.0} / {total / 1048576.0:0.0} GB · 가동 {TimeSpan.FromSeconds(double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], CultureInfo.InvariantCulture)):d\\일\\ hh\\:mm}";
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or FormatException) { mem.Text = ""; }
        }
        Refresh();
        var root = new DockPanel();
        DockPanel.SetDock(mem, Dock.Top);
        mem.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(mem);
        root.Children.Add(Scroll(list));
        return root.OnTick(ctx, Refresh);
    }

    // ── 전화 ───────────────────────────────────────────────────────────
    static Control Dialer(ShellContext ctx, string? preset)
    {
        var digits = AidNumber.Digits(preset ?? "");
        var display = Label("", 30, Text, FontWeight.Light);
        display.TextAlignment = TextAlignment.Center;
        display.FontFamily = Mono;
        var hint = Label("", 13, Muted);
        hint.TextAlignment = TextAlignment.Center;
        var mine = Label("", 12, Muted);
        mine.TextAlignment = TextAlignment.Center;

        void Update()
        {
            display.Text = digits.Length == 0 ? " " : AidNumber.FormatPartial(digits);
            (hint.Text, hint.Foreground) = digits.Length switch
            {
                0 => ("국가번호 없이 12자리 AlliedID 번호", Muted),
                < 12 => ($"{12 - digits.Length}자리 더", Muted),
                _ when AidNumber.IsValid(digits) => ("✓ 유효한 번호", Accent),
                _ => ("✗ 체크 숫자가 맞지 않습니다", Danger),
            };
        }

        var pad = new UniformGrid { Columns = 3, Rows = 4, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "⌫", "0", "✕" })
        {
            var b = new Button { Content = k, Width = 76, Height = 60 };
            b.Classes.Add("key");
            b.Click += (_, _) =>
            {
                digits = k switch
                {
                    "⌫" => digits.Length > 0 ? digits[..^1] : digits,
                    "✕" => "",
                    _ when digits.Length < 12 => digits + k,
                    _ => digits,
                };
                Update();
            };
            pad.Children.Add(b);
        }

        var call = new Button { Content = "📞  통화", HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(40, 12), FontSize = 18, Margin = new Thickness(0, 8) };
        call.Classes.Add("go");
        call.Click += async (_, _) =>
        {
            if (!AidNumber.IsValid(digits)) { ctx.Notify("번호를 확인하세요"); return; }
            if (!ctx.Has("call")) { ctx.Notify("🔒 call 권한이 없습니다"); return; }
            ctx.Notify($"📞 {AidNumber.Display(digits)} 연결 중…");
            var r = await ctx.Aid.CallAsync(digits);
            ctx.Notify(r.Ok ? r.Stdout.Split('\n')[0] : $"통화 실패: {r.Stderr.Trim()}");
        };

        var root = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { display, hint, pad, call, mine } };
        root.KeyDown += (_, e) =>
        {
            if (e.Key is >= Key.D0 and <= Key.D9 && digits.Length < 12) { digits += (char)('0' + (e.Key - Key.D0)); Update(); }
            else if (e.Key is >= Key.NumPad0 and <= Key.NumPad9 && digits.Length < 12) { digits += (char)('0' + (e.Key - Key.NumPad0)); Update(); }
            else if (e.Key == Key.Back && digits.Length > 0) { digits = digits[..^1]; Update(); }
        };
        root.Focusable = true;
        root.OnStatus(ctx, () => mine.Text = $"내 번호  {ctx.Status.Display ?? "-"}");
        Update();
        return root;
    }

    // ── 메모 ───────────────────────────────────────────────────────────
    static Control Note(string text) =>
        new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0) };

    // ── 네트워크: 국경 없는 VPN / Wi-Fi 탐색 / 위치 ───────────────────
    static Control Network(ShellContext ctx)
    {
        var info = new StackPanel { Spacing = 2 };
        var aps = new StackPanel { Spacing = 1 };
        void Status()
        {
            info.Children.Clear();
            var s = ctx.Status;
            var vpnUp = Directory.Exists("/sys/class/net/aid0");
            info.Children.Add(Label($"{(vpnUp ? "🟢" : "⚪")} 오버레이 VPN {(vpnUp ? "연결됨" : "대기")}  ·  국경 없음", 13, vpnUp ? Accent : Muted));
            info.Children.Add(Label($"주소 {s.Ipv6 ?? "-"}", 12, Muted));
        }
        async Task Scan()
        {
            if (!ctx.Has("net.wifi")) { aps.Children.Clear(); aps.Children.Add(Label("🔒 net.wifi 권한 필요", 12, Locked)); return; }
            var list = await ctx.Aid.WifiScanAsync();
            aps.Children.Clear();
            if (list.Count == 0) aps.Children.Add(Label("주변 Wi-Fi 없음 (또는 nmcli 없음)", 12, Muted));
            foreach (var ap in list.Take(10))
                aps.Children.Add(new DockPanel
                {
                    Children =
                    {
                        DockRight(Label($"{ap.Signal}%", 12, Muted)),
                        Label($"{(ap.InUse ? "●" : "○")} {(ap.Ssid.Length > 0 ? ap.Ssid : "(숨김)")}  {ap.FreqMhz / 1000.0:0.#}GHz", 13, ap.InUse ? Accent : Text),
                    },
                });
        }
        var locate = Flat("📍 위치", async () =>
        {
            var r = await ctx.Aid.RunAsync(["locate"]);
            ctx.Notify(r.Ok ? r.Stdout.Trim() : r.Stderr.Trim());
        }, 12);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { Flat("⟳ Wi-Fi 탐색", () => _ = Scan(), 12), locate } };
        var root = new StackPanel { Spacing = 8, Children = { info, bar, aps } };
        var scanned = false;
        root.OnStatus(ctx, () =>
        {
            Status();
            // 상태(권한)가 도착한 뒤 첫 스캔
            if (!scanned && ctx.Status.Initialised) { scanned = true; _ = Scan(); }
        });
        return Scroll(root);
    }

    // ── 회로도 (블록으로 조립한 GPIO) ─────────────────────────────────
    static Control Circuit(ShellContext ctx)
    {
        var list = new StackPanel { Spacing = 6 };
        var leds = new Dictionary<int, Border>();
        void Build()
        {
            list.Children.Clear();
            leds.Clear();
            list.Children.Add(Label(ctx.Circuit.Simulated ? $"시뮬레이션 ({ctx.Circuit.Chip} 없음)" : $"GPIO: {ctx.Circuit.Chip}", 12, Muted));
            if (ctx.Layout.Circuit.Count == 0) list.Children.Add(Label("조립대(🧩)에서 회로 블록을 추가하세요", 13, Muted));
            foreach (var p in ctx.Layout.Circuit)
            {
                Control right = p.Kind switch
                {
                    "button" => Flat("누르기", () => ctx.Circuit.Press(p), 12),
                    _ => Flat("전환", () => ctx.Circuit.Toggle(p.Pin), 12),
                };
                var dot = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = ctx.Circuit.Get(p.Pin) ? Accent : SurfaceHi, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                leds[p.Pin] = dot;
                var act = p.ActionVerb is null ? "" : $" → {p.ActionVerb} {p.ActionArg}";
                var row = new DockPanel { Children = { DockRight(right), dot, Label($"GPIO{p.Pin,-3} {p.Kind} \"{p.Name}\"{act}", 13) } };
                DockPanel.SetDock(dot, Dock.Left);
                list.Children.Add(row);
            }
        }
        Action<int, bool> changed = (pin, on) => Dispatcher.UIThread.Post(() => { if (leds.TryGetValue(pin, out var b)) b.Background = on ? Accent : SurfaceHi; });
        Build();
        var s = Scroll(list);
        s.WhileAttached(() => { ctx.Circuit.PinChanged += changed; ctx.LayoutChanged += Build; }, () => { ctx.Circuit.PinChanged -= changed; ctx.LayoutChanged -= Build; });
        return s;
    }

    // ── 설정: 신원 = 계정 = 번호 = 열쇠 ───────────────────────────────
    static Control Settings(ShellContext ctx)
    {
        var root = new StackPanel { Spacing = 8 };
        void Build()
        {
            root.Children.Clear();
            var s = ctx.Status;
            root.Children.Add(Label("AlliedID " + s.Version, 20, Text, FontWeight.SemiBold));
            if (!s.Initialised)
            {
                root.Children.Add(Label($"기기 신원이 없습니다: {s.Error}", 13, Locked));
                root.Children.Add(Label("터미널에서 `aid identity init` 실행 (첫 부팅 시 자동)", 12, Muted));
            }
            else
            {
                root.Children.Add(Label($"번호 (= 계정)  {s.Display}", 15, Accent));
                root.Children.Add(Label("비밀번호 없음 — 이 기기가 열쇠입니다.", 12, Muted));
                root.Children.Add(Label("권한", 14, Text, FontWeight.SemiBold));
                foreach (var (cap, desc) in new[]
                {
                    ("call", "통화"), ("net.vpn", "VPN"), ("net.wifi", "Wi-Fi 탐색"), ("net.location", "위치"),
                    ("apps.install", "앱 설치"), ("desktop.edit", "바탕화면 조립"), ("system.admin", "시스템 관리"),
                })
                {
                    var ok = ctx.Has(cap);
                    root.Children.Add(Label($"{(ok ? "🔓" : "🔒")}  {cap,-14} {desc}", 13, ok ? Text : Locked));
                }
            }
            root.Children.Add(Label("바탕화면", 14, Text, FontWeight.SemiBold));
            root.Children.Add(Label($"{ctx.Desk.Path}", 12, Muted));
            foreach (var e in ctx.LayoutErrors) root.Children.Add(Label("⚠ " + e, 12, Danger));
            root.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    Flat("기본 바탕화면으로", () => ctx.Desk.Save(Core.DeskScript.DeskScriptCompiler.Compile(DeskLayout.DefaultScript).Layout!), 12),
                    Flat("desk.aid 편집", () => ctx.Launch("xdg-open", ctx.Desk.Path), 12),
                },
            });
        }
        root.OnStatus(ctx, Build);
        return Scroll(root);
    }

    // ── 조립대 (Blockly) ─────────────────────────────────────────────
    static Control Blocks(ShellContext ctx)
    {
        var open = new Button { Content = "🧩 조립대 열기" };
        open.Classes.Add("go");
        open.Click += (_, _) =>
        {
            if (!ctx.Has("desktop.edit")) { ctx.Notify("🔒 desktop.edit 권한이 없습니다"); return; }
            ctx.Launch("xdg-open", BlocksUrl);
        };
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Label("블록을 끼워 맞춰 바탕화면과 회로도를 조립합니다.", 14),
                Label("저장하면 이 화면이 즉시 다시 조립됩니다 (desk.aid).", 12, Muted),
                open,
            },
        };
    }

    // ── 안드로이드 (Waydroid) ─────────────────────────────────────────
    static Control Android(ShellContext ctx)
    {
        var state = Label("확인 중…", 13, Muted);
        async Task Check()
        {
            var r = await SystemInfo.ShellAsync("waydroid status 2>/dev/null | head -3");
            state.Text = r.Stdout.Length > 0 ? r.Stdout.Trim() : "Waydroid 가 설치되지 않았습니다 (android/waydroid-setup.sh)";
        }
        var open = new Button { Content = "🤖 안드로이드 열기" };
        open.Classes.Add("go");
        open.Click += (_, _) => ctx.Launch("waydroid", "show-full-ui");
        var root = new StackPanel { Spacing = 8, Children = { Label("안드로이드 앱을 리눅스 컨테이너(Waydroid)에서 실행합니다.", 14), state, open } };
        root.AttachedToVisualTree += async (_, _) => await Check();
        return root;
    }
}
