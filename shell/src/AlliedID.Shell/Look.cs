using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AlliedID.Shell;

/// <summary>AlliedID 색/글꼴 토큰과 자주 쓰는 컨트롤 생성기.</summary>
public static class Look
{
    public static readonly IBrush Surface = Brush("#1c2140");
    public static readonly IBrush SurfaceHi = Brush("#262c52");
    public static readonly IBrush Bar = Brush("#0e1122");
    public static readonly IBrush Text = Brush("#e8eaf6");
    public static readonly IBrush Muted = Brush("#8b90b8");
    public static readonly IBrush Accent = Brush("#2fbf8a");   // 문이 열림
    public static readonly IBrush Locked = Brush("#f0a64a");   // 권한 없음
    public static readonly IBrush Danger = Brush("#ef5b6b");
    public static readonly FontFamily Mono = new("DejaVu Sans Mono, Noto Sans Mono CJK KR, monospace");

    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static readonly IReadOnlyDictionary<string, (string Icon, string Label)> Apps = new Dictionary<string, (string, string)>
    {
        ["terminal"] = ("⌨", "터미널"),
        ["files"] = ("🗂", "파일"),
        ["packages"] = ("📦", "패키지"),
        ["tasks"] = ("📊", "작업"),
        ["dialer"] = ("📞", "전화"),
        ["blocks"] = ("🧩", "조립대"),
        ["network"] = ("📡", "네트워크"),
        ["android"] = ("🤖", "안드로이드"),
        ["settings"] = ("⚙", "설정"),
        ["clock"] = ("🕒", "시계"),
        ["note"] = ("📝", "메모"),
        ["circuit"] = ("🔌", "회로"),
    };

    public static (string Icon, string Label) App(string kind) =>
        Apps.TryGetValue(kind, out var a) ? a : ("▫", kind);

    public static TextBlock Label(string text, double size = 14, IBrush? fg = null, FontWeight weight = FontWeight.Normal) =>
        new() { Text = text, FontSize = size, Foreground = fg ?? Text, FontWeight = weight, TextWrapping = TextWrapping.Wrap };

    public static Border Card(Control child, double padding = 12) =>
        new() { Background = Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(padding), Child = child };

    public static Button Flat(string content, Action onClick, double size = 14)
    {
        var b = new Button { Content = content, FontSize = size, HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Classes.Add("flat");
        b.Click += (_, _) => onClick();
        return b;
    }
}
