using AlliedID.Shell.Core.DeskScript;
using AlliedID.Shell.Core.Model;

namespace AlliedID.Shell.Core.Tests;

public class DeskScriptTests
{
    const string Sample = """
        // 조립 예시
        desktop "내 책상" {
          mode phone;
          wallpaper "#202040";
          sidebar bottom { dialer; terminal; files; }
          widget clock at (10, 20) size (300, 120);
          widget circuit "회로" at (0, 200) size (300, 120);
          widget terminal "Shell \"1\"" at (0, 0) size (640.5, 400) { command "ls -la"; rows 30; }
          circuit {
            led "상태" pin 17;
            button "긴급" pin 27 -> call "436846882187";
            button "토글" pin 22 -> toggle 17;
          }
        }
        """;

    [Fact]
    public void Default_script_compiles()
    {
        var r = DeskScriptCompiler.Compile(DeskLayout.DefaultScript);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        Assert.Contains("terminal", r.Layout!.SidebarApps);
    }

    [Fact]
    public void Sample_compiles_with_all_parts()
    {
        var r = DeskScriptCompiler.Compile(Sample);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        var l = r.Layout!;
        Assert.Equal("내 책상", l.Name);
        Assert.Equal(ShellMode.Phone, l.Mode);
        Assert.Equal(SidebarSide.Bottom, l.SidebarSide);
        Assert.Equal(["dialer", "terminal", "files"], l.SidebarApps);
        Assert.Equal("circuit", l.Widgets[1].Kind);
        Assert.Equal("Shell \"1\"", l.Widgets[2].Title);
        Assert.Equal(640.5, l.Widgets[2].Width);
        Assert.Equal("ls -la", l.Widgets[2].Prop("command"));
        Assert.Equal("30", l.Widgets[2].Prop("rows"));
        Assert.Equal(3, l.Circuit.Count);
        Assert.Equal(new CircuitPart("button", "긴급", 27, "call", "436846882187"), l.Circuit[1]);
        Assert.Equal("17", l.Circuit[2].ActionArg);
    }

    [Fact]
    public void Emit_roundtrips()
    {
        var a = DeskScriptCompiler.Compile(Sample).Layout!;
        var text = DeskScriptCompiler.Emit(a);
        var b = DeskScriptCompiler.Compile(text);
        Assert.True(b.Ok, text + "\n" + string.Join("\n", b.Errors));
        Assert.Equal(DeskScriptCompiler.Emit(b.Layout!), text);
    }

    [Theory]
    [InlineData("desktop \"x\" { mode tablet; }", "mode")]
    [InlineData("desktop \"x\" { wallpaper \"red\"; }", "#RRGGBB")]
    [InlineData("desktop \"x\" { sidebar left { minecraft; } }", "알 수 없는 앱")]
    [InlineData("desktop \"x\" { widget clock at (0,0) size (0,10); }", "양수")]
    [InlineData("desktop \"x\" { circuit { led \"a\" pin 40; } }", "GPIO")]
    [InlineData("desktop \"x\" { circuit { led \"a\" pin 5; led \"b\" pin 5; } }", "이미 사용")]
    [InlineData("desktop \"x\" { circuit { button \"a\" pin 5 -> explode; } }", "알 수 없는 동작")]
    [InlineData("desktop \"x\" { widget clock at (0,0) }", "")] // 구문 오류
    public void Semantic_and_syntax_errors_are_reported(string src, string expected)
    {
        var r = DeskScriptCompiler.Compile(src);
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Message.Contains(expected));
        Assert.All(r.Errors, e => Assert.True(e.Line >= 1 && e.Column >= 1));
    }
}
