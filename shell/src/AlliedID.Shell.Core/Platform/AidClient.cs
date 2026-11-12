using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlliedID.Shell.Core.Platform;

public sealed record AidStatus(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("initialised")] bool Initialised,
    [property: JsonPropertyName("number")] string? Number,
    [property: JsonPropertyName("display")] string? Display,
    [property: JsonPropertyName("ipv6")] string? Ipv6,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string>? Capabilities,
    [property: JsonPropertyName("error")] string? Error)
{
    public static AidStatus Offline(string why) => new("26.11.1 LTS", false, null, null, null, "desktop", [], why);
}

public sealed record AccessPoint(
    [property: JsonPropertyName("ssid")] string Ssid,
    [property: JsonPropertyName("bssid")] string Bssid,
    [property: JsonPropertyName("signal")] int Signal,
    [property: JsonPropertyName("freq_mhz")] int FreqMhz,
    [property: JsonPropertyName("security")] string Security,
    [property: JsonPropertyName("in_use")] bool InUse);

public sealed record CommandResult(int ExitCode, string Stdout, string Stderr)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Rust <c>aid</c> 명령과 대화하는 클라이언트. 셸 ↔ 시스템 경계는 이 클래스 하나.</summary>
public sealed class AidClient(string? executable = null)
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>명시 경로 → <c>$AID_BIN</c> → PATH 의 <c>aid</c></summary>
    public string Executable { get; } = executable
        ?? (Environment.GetEnvironmentVariable("AID_BIN") is { Length: > 0 } b ? b : "aid");

    public async Task<CommandResult> RunAsync(IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(Executable) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return new(p.ExitCode, await stdout, await stderr);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new(127, "", $"'{Executable}' 를 실행할 수 없습니다: {e.Message}");
        }
    }

    async Task<T?> JsonAsync<T>(CancellationToken ct, params string[] args)
    {
        var r = await RunAsync(["--json", .. args], ct);
        if (!r.Ok) return default;
        try { return JsonSerializer.Deserialize<T>(r.Stdout, Json); }
        catch (JsonException) { return default; }
    }

    public async Task<AidStatus> StatusAsync(CancellationToken ct = default)
    {
        var r = await RunAsync(["--json", "status"], ct);
        if (!r.Ok) return AidStatus.Offline(r.Stderr.Trim());
        return JsonSerializer.Deserialize<AidStatus>(r.Stdout, Json) ?? AidStatus.Offline("invalid status");
    }

    public async Task<bool> CheckAsync(string capability, CancellationToken ct = default) =>
        (await RunAsync(["check", capability], ct)).ExitCode == 0;

    public async Task<IReadOnlyList<AccessPoint>> WifiScanAsync(CancellationToken ct = default) =>
        await JsonAsync<List<AccessPoint>>(ct, "wifi", "scan") ?? [];

    public Task<CommandResult> CallAsync(string number, CancellationToken ct = default) =>
        RunAsync(["call", AidNumber.Digits(number)], ct);
}
