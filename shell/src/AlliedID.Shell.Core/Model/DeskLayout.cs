namespace AlliedID.Shell.Core.Model;

public enum ShellMode { Auto, Phone, Desktop }

public enum SidebarSide { Left, Right, Bottom }

/// <summary>위젯 하나. Kind: clock, terminal, files, packages, tasks, dialer, note, network, circuit, blocks</summary>
public sealed record Widget(
    string Kind,
    string? Title,
    double X, double Y, double Width, double Height,
    IReadOnlyDictionary<string, string> Props)
{
    public string Prop(string key, string fallback = "") => Props.TryGetValue(key, out var v) ? v : fallback;
}

/// <summary>회로도 부품. Kind: led, button, buzzer, relay</summary>
public sealed record CircuitPart(string Kind, string Name, int Pin, string? ActionVerb, string? ActionArg);

/// <summary>DeskScript 한 파일 = 바탕화면 한 장.</summary>
public sealed record DeskLayout(
    string Name,
    ShellMode Mode,
    string Wallpaper,
    SidebarSide SidebarSide,
    IReadOnlyList<string> SidebarApps,
    IReadOnlyList<Widget> Widgets,
    IReadOnlyList<CircuitPart> Circuit)
{
    public static readonly IReadOnlyList<string> KnownApps =
        ["terminal", "files", "packages", "tasks", "dialer", "blocks", "network", "settings", "android"];

    public static readonly IReadOnlyList<string> KnownWidgets =
        ["clock", "terminal", "files", "packages", "tasks", "dialer", "note", "network", "circuit"];

    /// <summary>설정 파일이 없을 때의 기본 바탕화면 (Ubuntu 유지항목: 사이드바/스크립트/터미널/파일&패키지/작업).</summary>
    public const string DefaultScript = """
        desktop "AlliedID" {
          mode auto;
          wallpaper "#14182b";
          sidebar left { terminal; files; packages; tasks; dialer; blocks; network; android; settings; }
          widget clock at (24, 24) size (320, 152);
          widget network "네트워크" at (24, 192) size (320, 232);
          widget terminal "터미널" at (360, 24) size (640, 400) { command "uname -a"; }
          widget files "열린 파일" at (24, 440) size (480, 264);
          widget tasks "작업" at (520, 440) size (480, 264);
        }
        """;
}
