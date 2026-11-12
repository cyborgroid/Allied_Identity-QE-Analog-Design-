using System.Globalization;
using System.Text;
using AlliedID.Shell.Core.DeskScript.Generated;
using AlliedID.Shell.Core.Model;
using Antlr4.Runtime;

namespace AlliedID.Shell.Core.DeskScript;

public sealed record Diagnostic(int Line, int Column, string Message)
{
    public override string ToString() => $"{Line}:{Column}: {Message}";
}

public sealed record CompileResult(DeskLayout? Layout, IReadOnlyList<Diagnostic> Errors)
{
    public bool Ok => Layout is not null && Errors.Count == 0;
}

/// <summary>DeskScript 텍스트 ↔ <see cref="DeskLayout"/>.</summary>
public static class DeskScriptCompiler
{
    public static CompileResult Compile(string source)
    {
        var errors = new List<Diagnostic>();
        var lexer = new DeskScriptLexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new LexerErrors(errors));
        var parser = new DeskScriptParser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new ParserErrors(errors));
        var tree = parser.file();
        if (errors.Count > 0) return new(null, errors);

        var v = new LayoutBuilder(errors);
        var layout = v.Build(tree.desktop());
        return new(errors.Count == 0 ? layout : null, errors);
    }

    /// <summary>레이아웃을 DeskScript 로 직렬화 (블록 편집기 ↔ 텍스트 왕복용).</summary>
    public static string Emit(DeskLayout l)
    {
        var sb = new StringBuilder();
        sb.Append("desktop ").Append(Quote(l.Name)).AppendLine(" {");
        sb.Append("  mode ").Append(l.Mode.ToString().ToLowerInvariant()).AppendLine(";");
        sb.Append("  wallpaper ").Append(Quote(l.Wallpaper)).AppendLine(";");
        sb.Append("  sidebar ").Append(l.SidebarSide.ToString().ToLowerInvariant()).Append(" {");
        foreach (var a in l.SidebarApps) sb.Append(' ').Append(a).Append(';');
        sb.AppendLine(" }");
        foreach (var w in l.Widgets)
        {
            sb.Append("  widget ").Append(w.Kind);
            if (w.Title is not null) sb.Append(' ').Append(Quote(w.Title));
            sb.Append(" at (").Append(N(w.X)).Append(", ").Append(N(w.Y)).Append(") size (")
              .Append(N(w.Width)).Append(", ").Append(N(w.Height)).Append(')');
            if (w.Props.Count == 0) { sb.AppendLine(";"); continue; }
            sb.Append(" {");
            foreach (var (k, val) in w.Props) sb.Append(' ').Append(k).Append(' ').Append(Quote(val)).Append(';');
            sb.AppendLine(" }");
        }
        if (l.Circuit.Count > 0)
        {
            sb.AppendLine("  circuit {");
            foreach (var p in l.Circuit)
            {
                sb.Append("    ").Append(p.Kind).Append(' ').Append(Quote(p.Name)).Append(" pin ").Append(p.Pin);
                if (p.ActionVerb is not null)
                {
                    sb.Append(" -> ").Append(p.ActionVerb);
                    if (p.ActionArg is not null) sb.Append(' ').Append(Quote(p.ActionArg));
                }
                sb.AppendLine(";");
            }
            sb.AppendLine("  }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    static string N(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    internal static string Unquote(string token)
    {
        var body = token[1..^1];
        var sb = new StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] == '\\' && i + 1 < body.Length)
            {
                i++;
                sb.Append(body[i] switch { 'n' => '\n', 't' => '\t', var c => c });
            }
            else sb.Append(body[i]);
        }
        return sb.ToString();
    }

    sealed class LayoutBuilder(List<Diagnostic> errors)
    {
        static readonly string[] Parts = ["led", "button", "buzzer", "relay"];
        static readonly string[] Verbs = ["call", "launch", "toggle", "run", "notify"];
        // Raspberry Pi 5 40핀 헤더에서 쓸 수 있는 BCM GPIO
        const int MaxGpio = 27;

        public DeskLayout Build(DeskScriptParser.DesktopContext ctx)
        {
            var mode = ShellMode.Auto;
            var wallpaper = "#14182b";
            var side = SidebarSide.Left;
            var apps = new List<string>();
            var widgets = new List<Widget>();
            var circuit = new List<CircuitPart>();

            foreach (var st in ctx.statement())
            {
                if (st.modeStmt() is { } m)
                {
                    var t = m.modeName;
                    if (!Enum.TryParse(t.GetText(), ignoreCase: true, out mode))
                        Err(t.Start, $"mode 는 auto | phone | desktop 중 하나여야 합니다 ('{t.GetText()}')");
                }
                else if (st.wallpaperStmt() is { } wp)
                {
                    wallpaper = Unquote(wp.color.Text);
                    if (!IsColor(wallpaper)) Err(wp.color, $"색상은 #RRGGBB 형식이어야 합니다 ('{wallpaper}')");
                }
                else if (st.sidebarStmt() is { } sb)
                {
                    if (!Enum.TryParse(sb.side.GetText(), ignoreCase: true, out side))
                        Err(sb.side.Start, $"sidebar 위치는 left | right | bottom ('{sb.side.GetText()}')");
                    foreach (var item in sb.sidebarItem())
                    {
                        var app = item.app.GetText();
                        if (!DeskLayout.KnownApps.Contains(app)) Err(item.app.Start, $"알 수 없는 앱 '{app}'");
                        else if (!apps.Contains(app)) apps.Add(app);
                    }
                }
                else if (st.widgetStmt() is { } w)
                {
                    var kind = w.kind.GetText();
                    if (!DeskLayout.KnownWidgets.Contains(kind)) Err(w.kind.Start, $"알 수 없는 위젯 '{kind}'");
                    var props = new Dictionary<string, string>();
                    foreach (var p in w.prop()) props[p.key.GetText()] = ValueText(p.value());
                    var (x, y) = Point(w.pos);
                    var (width, height) = Point(w.size);
                    if (width <= 0 || height <= 0) Err(w.size.Start, "size 는 양수여야 합니다");
                    widgets.Add(new Widget(kind, w.title is null ? null : Unquote(w.title.Text), x, y, width, height, props));
                }
                else if (st.circuitStmt() is { } c)
                {
                    foreach (var p in c.part())
                    {
                        var partKind = p.kind.GetText();
                        if (!Parts.Contains(partKind)) Err(p.kind.Start, $"알 수 없는 부품 '{partKind}' ({string.Join(", ", Parts)})");
                        var pin = int.Parse(p.pin.Text, CultureInfo.InvariantCulture);
                        if (pin < 2 || pin > MaxGpio) Err(p.pin, $"GPIO 핀은 2–{MaxGpio} 범위여야 합니다 ({pin})");
                        if (circuit.Any(o => o.Pin == pin)) Err(p.pin, $"GPIO {pin} 이 이미 사용 중입니다");
                        string? verb = null, arg = null;
                        if (p.action() is { } a)
                        {
                            verb = a.verb.GetText();
                            if (!Verbs.Contains(verb)) Err(a.verb.Start, $"알 수 없는 동작 '{verb}' ({string.Join(", ", Verbs)})");
                            arg = a.value() is { } v ? ValueText(v) : null;
                        }
                        circuit.Add(new CircuitPart(partKind, Unquote(p.name.Text), pin, verb, arg));
                    }
                }
            }
            return new DeskLayout(Unquote(ctx.name.Text), mode, wallpaper, side, apps, widgets, circuit);
        }

        static (double, double) Point(DeskScriptParser.PointContext p) =>
            (double.Parse(p.x.Text, CultureInfo.InvariantCulture), double.Parse(p.y.Text, CultureInfo.InvariantCulture));

        static string ValueText(DeskScriptParser.ValueContext v) =>
            v.STRING() is { } s ? Unquote(s.GetText()) : v.GetText();

        static bool IsColor(string s) =>
            s.Length is 7 or 9 && s[0] == '#' && s[1..].All(Uri.IsHexDigit);

        void Err(IToken t, string msg) => errors.Add(new Diagnostic(t.Line, t.Column + 1, msg));
    }

    sealed class LexerErrors(List<Diagnostic> errors) : IAntlrErrorListener<int>
    {
        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int col, string msg, RecognitionException e)
            => errors.Add(new Diagnostic(line, col + 1, msg));
    }

    sealed class ParserErrors(List<Diagnostic> errors) : IAntlrErrorListener<IToken>
    {
        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int col, string msg, RecognitionException e)
            => errors.Add(new Diagnostic(line, col + 1, msg));
    }
}
