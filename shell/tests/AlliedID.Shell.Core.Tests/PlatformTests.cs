using AlliedID.Shell.Core.Model;
using AlliedID.Shell.Core.Platform;

namespace AlliedID.Shell.Core.Tests;

public class PlatformTests
{
    [Theory]
    [InlineData("AID 4368-4688-2187", true)]   // aid CLI 가 실제로 생성한 번호
    [InlineData("436846882187", true)]
    [InlineData("436846882188", false)]        // 체크 숫자 틀림
    [InlineData("+82 4368-4688-2187", false)]  // 국가번호 없음
    [InlineData("4368", false)]
    public void Number_validation_matches_rust(string input, bool valid) =>
        Assert.Equal(valid, AidNumber.IsValid(input));

    [Fact]
    public void Partial_formatting()
    {
        Assert.Equal("4368-46", AidNumber.FormatPartial("436846"));
        Assert.Equal("4368-4688-2187", AidNumber.FormatPartial("43684688218799"));
        Assert.Equal("AID 4368-4688-2187", AidNumber.Display("436846882187"));
    }

    [Fact]
    public void Mode_file_is_parsed()
    {
        var f = Path.GetTempFileName();
        File.WriteAllText(f, """{"mode":"phone","forced":false,"connectors":[]}""");
        Assert.Equal(ShellMode.Phone, ModeSource.Read(f));
        File.WriteAllText(f, "garbage");
        Assert.Null(ModeSource.Read(f));
        File.Delete(f);
        Assert.Null(ModeSource.Read(f));
    }

    [Fact]
    public void Aspect_fallback()
    {
        Assert.Equal(ShellMode.Phone, ModeSource.FromAspect(720, 1280));
        Assert.Equal(ShellMode.Desktop, ModeSource.FromAspect(1920, 1080));
    }

    [Fact]
    public void Broken_desk_file_falls_back_to_default_with_errors()
    {
        var f = Path.GetTempFileName();
        File.WriteAllText(f, "desktop \"x\" { mode banana; }");
        var store = new DeskStore(f);
        var r = store.Load();
        Assert.NotNull(r.Layout);
        Assert.NotEmpty(r.Errors);
        store.Save(r.Layout!);
        Assert.True(store.Load().Ok);
        File.Delete(f);
    }

    [Fact]
    public void Circuit_runner_simulates_without_gpio()
    {
        using var c = new CircuitRunner("gpiochip-does-not-exist");
        Assert.True(c.Simulated);
        var hits = 0;
        c.Triggered += _ => hits++;
        c.Toggle(17);
        Assert.True(c.Get(17));
        c.Press(new CircuitPart("button", "b", 27, "toggle", "17"));
        Assert.Equal(1, hits);
    }

    [Fact]
    public async Task Aid_client_reports_missing_binary()
    {
        var r = await new AidClient("/nonexistent/aid").RunAsync(["status"]);
        Assert.Equal(127, r.ExitCode);
    }
}

public class GpiodTests
{
    [Theory]
    [InlineData("gpioset (libgpiod) v1.6.3\nCopyright (C) 2017-2018 Bartosz Golaszewski", 1)]
    [InlineData("gpioset (libgpiod) v2.1.3", 2)]
    [InlineData("", 0)]
    public void Version_is_parsed(string text, int expected) =>
        Assert.Equal(expected, CircuitRunner.ParseVersion(text));

    [Fact]
    public void Rp1_chip_is_found_on_pi5()
    {
        const string detect = "gpiochip0 [gpio-brcmstb@107d508500] (32 lines)\ngpiochip4 [pinctrl-rp1] (54 lines)\n";
        Assert.Equal("gpiochip4", CircuitRunner.FindRp1(detect));
        Assert.Null(CircuitRunner.FindRp1("gpiochip0 [INT34C6:00] (463 lines)"));
    }

    [Fact]
    public void Ubuntu_2404_uses_v1_syntax()
    {
        Assert.Equal(["--mode=signal", "gpiochip4", "17=1"], CircuitRunner.SetArgs(1, "gpiochip4", 17, true));
        Assert.Equal(["--falling-edge", "--bias=pull-up", "gpiochip4", "27"], CircuitRunner.MonitorArgs(1, "gpiochip4", 27));
        Assert.Equal(["-c", "gpiochip0", "17=0"], CircuitRunner.SetArgs(2, "gpiochip0", 17, false));
        Assert.Contains("--debounce-period", CircuitRunner.MonitorArgs(2, "gpiochip0", 27));
    }
}
