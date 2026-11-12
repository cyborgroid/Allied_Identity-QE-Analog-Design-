using System.Text.Json;
using AlliedID.Shell.Core.Model;

namespace AlliedID.Shell.Core.Platform;

/// <summary>
/// <c>aid mode watch</c> 데몬이 쓰는 <c>/run/alliedid/mode.json</c> 을 감시한다.
/// 파일이 없으면 창 크기(세로/가로)로 추정한다.
/// </summary>
public sealed class ModeSource : IDisposable
{
    public const string DefaultPath = "/run/alliedid/mode.json";
    readonly string _path;
    readonly Timer _timer;
    ShellMode? _last;

    public event Action<ShellMode>? Changed;

    public ModeSource(string? path = null, TimeSpan? interval = null)
    {
        _path = path ?? Environment.GetEnvironmentVariable("AID_MODE_FILE") ?? DefaultPath;
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, interval ?? TimeSpan.FromSeconds(1));
    }

    public ShellMode? Current => Read(_path);

    public static ShellMode? Read(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("mode").GetString() switch
            {
                "phone" => ShellMode.Phone,
                "desktop" => ShellMode.Desktop,
                _ => null,
            };
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>모드 파일이 없을 때: 세로 화면이면 Phone.</summary>
    public static ShellMode FromAspect(double width, double height) =>
        height > width * 1.15 ? ShellMode.Phone : ShellMode.Desktop;

    void Poll()
    {
        var m = Read(_path);
        if (m is null || m == _last) return;
        _last = m;
        Changed?.Invoke(m.Value);
    }

    public void Dispose() => _timer.Dispose();
}
