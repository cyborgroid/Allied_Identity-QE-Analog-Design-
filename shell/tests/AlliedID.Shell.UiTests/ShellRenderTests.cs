using AlliedID.Shell;
using AlliedID.Shell.Core.Model;
using AlliedID.Shell.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;

[assembly: AvaloniaTestApplication(typeof(AlliedID.Shell.UiTests.TestApp))]

namespace AlliedID.Shell.UiTests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// 두 모드를 실제로 렌더링해 본다. AID_SCREENSHOT_DIR 이 지정되면 PNG 를 저장한다.
/// (테스트는 사용자 설정을 건드리지 않도록 임시 desk.aid / 모드 파일을 쓴다.)
/// </summary>
public class ShellRenderTests
{
    static ShellRenderTests()
    {
        var tmp = Directory.CreateTempSubdirectory("aid-ui-").FullName;
        Environment.SetEnvironmentVariable("AID_DESK_FILE", Path.Combine(tmp, "desk.aid"));
        Environment.SetEnvironmentVariable("AID_MODE_FILE", Path.Combine(tmp, "mode.json"));
        File.WriteAllText(Path.Combine(tmp, "desk.aid"), DeskLayout.DefaultScript.Replace("widget tasks", """
            widget circuit "회로" at (1016, 24) size (300, 200);
              widget tasks
            """).Replace("desktop \"AlliedID\" {", """
            desktop "AlliedID" {
              circuit { led "상태" pin 17; button "긴급전화" pin 27 -> call "436846882187"; }
            """));
    }

    static void Pump()
    {
        for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
    }

    static void Save(Window w, string name)
    {
        var dir = Environment.GetEnvironmentVariable("AID_SCREENSHOT_DIR");
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (!string.IsNullOrEmpty(dir)) frame!.Save(Path.Combine(dir, name));
    }

    [AvaloniaFact]
    public void Desktop_mode_renders_layout_widgets()
    {
        var w = new MainWindow(new ShellOptions(ShellMode.Desktop, false, 1340, 800));
        w.Show();
        Pump();
        var view = w.GetLogicalDescendants().OfType<DesktopView>().SingleOrDefault();
        Assert.NotNull(view);
        Save(w, "desktop.png");
        var texts = w.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(texts, t => t?.Contains("터미널") == true);
        Assert.True(texts.Any(t => t?.Contains("GPIO17") == true), string.Join(" | ", texts));
        w.Close();
    }

    [AvaloniaFact]
    public void Phone_mode_renders_home_and_dialer()
    {
        var w = new MainWindow(new ShellOptions(ShellMode.Phone, false, 480, 800));
        w.Show();
        Pump();
        Assert.NotNull(w.GetLogicalDescendants().OfType<PhoneView>().SingleOrDefault());
        Save(w, "phone-home.png");

        // 회로 버튼(긴급전화) → 다이얼러가 번호를 채운 채로 열린다
        var view = w.GetLogicalDescendants().OfType<PhoneView>().Single();
        var openDialer = typeof(PhoneView).GetMethod("Open", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        openDialer.Invoke(view, ["dialer", "436846882187"]);
        Pump();
        var texts = w.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("4368-4688-2187", texts);
        Assert.Contains(texts, t => t?.Contains("유효한 번호") == true);
        Save(w, "phone-dialer.png");
        w.Close();
    }
}
