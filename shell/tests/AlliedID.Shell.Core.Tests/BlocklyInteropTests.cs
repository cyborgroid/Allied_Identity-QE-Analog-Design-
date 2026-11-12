using System.Runtime.CompilerServices;
using AlliedID.Shell.Core.DeskScript;
using AlliedID.Shell.Core.Model;

namespace AlliedID.Shell.Core.Tests;

/// <summary>
/// 조립대(Blockly, JS)가 만든 DeskScript 를 셸(C#, ANTLR)이 그대로 읽는지 교차 검증.
/// 산출물은 <c>blocks/ npm test</c> 가 <c>blocks/test/out/*.aid</c> 로 만든다.
/// </summary>
public class BlocklyInteropTests
{
    static string OutDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../../../blocks/test/out"));

    public static TheoryData<string> Files()
    {
        var d = new TheoryData<string>();
        if (Directory.Exists(OutDir()))
            foreach (var f in Directory.GetFiles(OutDir(), "*.aid")) d.Add(Path.GetFileName(f));
        if (d.Count == 0) d.Add("(none)");
        return d;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Blockly_output_compiles_and_roundtrips(string file)
    {
        if (file == "(none)") return; // npm test 를 아직 안 돌렸으면 건너뜀
        var src = File.ReadAllText(Path.Combine(OutDir(), file));
        var r = DeskScriptCompiler.Compile(src);
        Assert.True(r.Ok, string.Join("\n", r.Errors));
        // 두 생성기의 출력 형식이 같아야 한다 (블록 ↔ 텍스트 왕복)
        Assert.Equal(src, DeskScriptCompiler.Emit(r.Layout!));
    }

    [Fact]
    public void Shell_default_equals_blockly_default()
    {
        var f = Path.Combine(OutDir(), "default.aid");
        if (!File.Exists(f)) return;
        var shell = DeskScriptCompiler.Emit(DeskScriptCompiler.Compile(DeskLayout.DefaultScript).Layout!);
        Assert.Equal(File.ReadAllText(f), shell);
    }
}
