using System.Diagnostics;
using System.Text.RegularExpressions;
using AlliedID.Shell.Core.Model;

namespace AlliedID.Shell.Core.Platform;

/// <summary>
/// 블록으로 조립한 회로도를 실제 GPIO 에 연결한다 (libgpiod CLI: gpioset / gpiomon).
/// Ubuntu 24.04 는 libgpiod v1(1.6), 최신 배포판은 v2 라서 두 문법을 모두 지원한다.
/// Raspberry Pi 5 의 40핀 헤더는 RP1 칩("pinctrl-rp1", 커널에 따라 gpiochip0 또는 gpiochip4)에 붙어 있다.
/// GPIO 가 없는 환경(VMware, 개발 PC)에서는 시뮬레이션으로 동작한다.
/// </summary>
public sealed class CircuitRunner : IDisposable
{
    readonly List<Process> _monitors = [];
    readonly Dictionary<int, Process> _holders = [];
    readonly Dictionary<int, bool> _state = [];

    public string Chip { get; }
    public bool Simulated { get; }
    /// <summary>libgpiod CLI 주 버전 (1 또는 2). 시뮬레이션이면 0.</summary>
    public int GpiodVersion { get; }

    /// <summary>버튼이 눌렸을 때 (부품, 동작).</summary>
    public event Action<CircuitPart>? Triggered;
    public event Action<int, bool>? PinChanged;

    public CircuitRunner(string? chip = null)
    {
        GpiodVersion = HasTool("gpioset") ? DetectVersion() : 0;
        Chip = chip ?? Environment.GetEnvironmentVariable("AID_GPIOCHIP") ?? DetectChip();
        Simulated = GpiodVersion == 0 || !File.Exists($"/dev/{Chip}");
    }

    static string? Run(string file, params string[] args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return o;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    /// <summary>"gpioset (libgpiod) v1.6.3" → 1, "v2.1" → 2</summary>
    public static int ParseVersion(string? text)
    {
        var m = Regex.Match(text ?? "", @"v(\d+)\.");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    static int DetectVersion() => ParseVersion(Run("gpioset", "--version")) is var v and > 0 ? v : 1;

    /// <summary>gpiodetect 출력에서 RP1 칩을 찾는다: "gpiochip0 [pinctrl-rp1] (54 lines)"</summary>
    public static string? FindRp1(string? gpiodetect) =>
        gpiodetect?.Split('\n').FirstOrDefault(l => l.Contains("rp1"))?.Split(' ', 2)[0] is { Length: > 0 } c ? c : null;

    static string DetectChip() => FindRp1(HasTool("gpiodetect") ? Run("gpiodetect") : null) ?? "gpiochip0";

    static bool HasTool(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(p => File.Exists(Path.Combine(p, name)));

    /// <summary>핀에 값을 쓰는 명령 인자 (프로세스가 살아 있는 동안 값이 유지된다).</summary>
    public static string[] SetArgs(int version, string chip, int pin, bool on) => version >= 2
        ? ["-c", chip, $"{pin}={(on ? 1 : 0)}"]
        : ["--mode=signal", chip, $"{pin}={(on ? 1 : 0)}"];

    /// <summary>버튼(GND 로 연결, 내부 풀업)의 눌림(하강 에지)을 감시하는 명령 인자.</summary>
    public static string[] MonitorArgs(int version, string chip, int pin) => version >= 2
        ? ["-c", chip, "-e", "falling", "-b", "pull-up", "--debounce-period", "50ms", pin.ToString()]
        : ["--falling-edge", "--bias=pull-up", chip, pin.ToString()];

    public bool Get(int pin) => _state.TryGetValue(pin, out var v) && v;

    public void Set(int pin, bool on)
    {
        _state[pin] = on;
        PinChanged?.Invoke(pin, on);
        if (Simulated) return;
        Release(pin);
        try
        {
            var p = Process.Start(new ProcessStartInfo("gpioset", SetArgs(GpiodVersion, Chip, pin, on)) { UseShellExecute = false });
            if (p is not null) _holders[pin] = p;
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    void Release(int pin)
    {
        if (!_holders.Remove(pin, out var old)) return;
        try { old.Kill(); } catch (InvalidOperationException) { }
        old.Dispose();
    }

    public void Toggle(int pin) => Set(pin, !Get(pin));

    /// <summary>버튼(입력)을 감시 시작.</summary>
    public void Start(IEnumerable<CircuitPart> parts)
    {
        Stop();
        if (Simulated) return;
        foreach (var p in parts.Where(p => p.Kind == "button"))
        {
            try
            {
                var proc = Process.Start(new ProcessStartInfo("gpiomon", MonitorArgs(GpiodVersion, Chip, p.Pin))
                { RedirectStandardOutput = true, UseShellExecute = false })!;
                proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Triggered?.Invoke(p); };
                proc.BeginOutputReadLine();
                _monitors.Add(proc);
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
    }

    /// <summary>UI 의 가상 버튼 (시뮬레이션 또는 테스트용).</summary>
    public void Press(CircuitPart part) => Triggered?.Invoke(part);

    public void Stop()
    {
        foreach (var m in _monitors) { try { m.Kill(); } catch (InvalidOperationException) { } m.Dispose(); }
        _monitors.Clear();
    }

    public void Dispose()
    {
        Stop();
        foreach (var pin in _holders.Keys.ToList()) Release(pin);
    }
}
