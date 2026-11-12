using System.Diagnostics;
using AlliedID.Shell.Core.Model;
using AlliedID.Shell.Core.Platform;
using Avalonia.Threading;

namespace AlliedID.Shell;

/// <summary>셸 전체가 공유하는 상태와 서비스.</summary>
public sealed class ShellContext : IDisposable
{
    public AidClient Aid { get; } = new();
    public DeskStore Desk { get; } = new();
    public CircuitRunner Circuit { get; } = new();
    public AidStatus Status { get; private set; } = AidStatus.Offline("loading");
    public DeskLayout Layout { get; private set; }
    public IReadOnlyList<string> LayoutErrors { get; private set; } = [];

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    int _seconds;

    /// <summary>1초마다 (UI 스레드)</summary>
    public event Action? Tick;
    public event Action? StatusChanged;
    public event Action? LayoutChanged;
    /// <summary>앱 열기 요청 (kind, 인자)</summary>
    public event Action<string, string?>? OpenRequested;
    public event Action<string>? Notified;

    public ShellContext()
    {
        var r = Desk.Load();
        Layout = r.Layout!;
        LayoutErrors = r.Errors.Select(e => e.ToString()).ToList();
        Desk.Reloaded += res => Dispatcher.UIThread.Post(() => ApplyLayout(res));
        Desk.Watch();

        Circuit.Triggered += part => Dispatcher.UIThread.Post(() => RunAction(part));
        Circuit.Start(Layout.Circuit);

        _tick.Tick += (_, _) =>
        {
            Tick?.Invoke();
            if (++_seconds % 10 == 0) _ = RefreshStatusAsync();
        };
        _tick.Start();
        _ = RefreshStatusAsync();
    }

    void ApplyLayout(Core.DeskScript.CompileResult res)
    {
        Layout = res.Layout!;
        LayoutErrors = res.Errors.Select(e => e.ToString()).ToList();
        Circuit.Start(Layout.Circuit);
        LayoutChanged?.Invoke();
        Notify(res.Ok ? "바탕화면이 다시 조립되었습니다" : $"desk.aid 오류 {res.Errors.Count}개 — 기본 바탕화면 사용");
    }

    public async Task RefreshStatusAsync()
    {
        var s = await Aid.StatusAsync();
        Dispatcher.UIThread.Post(() =>
        {
            Status = s;
            StatusChanged?.Invoke();
        });
    }

    public bool Has(string capability) =>
        Status.Capabilities?.Any(c => c == "*" || c == capability ||
            (c.EndsWith(".*") && capability.StartsWith(c[..^1]))) ?? false;

    public void Open(string kind, string? arg = null) => OpenRequested?.Invoke(kind, arg);

    public void Notify(string message) => Notified?.Invoke(message);

    /// <summary>외부 프로그램 실행 (안드로이드, 브라우저 등).</summary>
    public bool Launch(string file, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Notify($"'{file}' 를 실행할 수 없습니다");
            return false;
        }
    }

    /// <summary>회로도 버튼 동작.</summary>
    public void RunAction(CircuitPart part)
    {
        switch (part.ActionVerb)
        {
            case "call" when part.ActionArg is { } n:
                Open("dialer", n);
                break;
            case "launch" when part.ActionArg is { } app:
                Open(app);
                break;
            case "toggle" when int.TryParse(part.ActionArg, out var pin):
                Circuit.Toggle(pin);
                break;
            case "run" when part.ActionArg is { } cmd:
                _ = SystemInfo.ShellAsync(cmd);
                break;
            case "notify":
                Notify(part.ActionArg ?? part.Name);
                break;
        }
        Notify($"🔌 {part.Name} → {part.ActionVerb} {part.ActionArg}");
    }

    public void Dispose()
    {
        _tick.Stop();
        Desk.Dispose();
        Circuit.Dispose();
    }
}
