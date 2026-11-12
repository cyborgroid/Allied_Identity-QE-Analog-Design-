using System.Diagnostics;

namespace AlliedID.Shell.Core.Platform;

public sealed record ProcessEntry(int Pid, string Name, long RssKb);
public sealed record FileEntry(string Path, string Name, DateTime Modified, long Size);
public sealed record PackageEntry(string Name, string Version);

/// <summary>"작업 / 열린 파일 & 패키지" 위젯의 데이터 소스.</summary>
public static class SystemInfo
{
    /// <summary>/proc 에서 메모리 사용량 상위 프로세스.</summary>
    public static IReadOnlyList<ProcessEntry> TopProcesses(int count = 12)
    {
        var list = new List<ProcessEntry>();
        if (!Directory.Exists("/proc")) return list;
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(System.IO.Path.GetFileName(dir), out var pid)) continue;
            try
            {
                string? name = null; long rss = 0;
                foreach (var line in File.ReadLines(System.IO.Path.Combine(dir, "status")))
                {
                    if (line.StartsWith("Name:")) name = line[5..].Trim();
                    else if (line.StartsWith("VmRSS:")) rss = long.Parse(line[6..].Trim().Split(' ')[0]);
                }
                if (name is not null && rss > 0) list.Add(new(pid, name, rss));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { }
        }
        return list.OrderByDescending(p => p.RssKb).Take(count).ToList();
    }

    /// <summary>홈 디렉터리의 최근 수정 파일.</summary>
    public static IReadOnlyList<FileEntry> RecentFiles(int count = 12, string? root = null)
    {
        root ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var opts = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden };
        try
        {
            return new DirectoryInfo(root).EnumerateFiles("*", opts)
                .Where(f => !f.FullName.Contains("/."))
                .OrderByDescending(f => f.LastWriteTime)
                .Take(count)
                .Select(f => new FileEntry(f.FullName, f.Name, f.LastWriteTime, f.Length))
                .ToList();
        }
        catch (IOException) { return []; }
    }

    /// <summary>설치된 데비안 패키지 (dpkg-query).</summary>
    public static async Task<IReadOnlyList<PackageEntry>> PackagesAsync(string? filter = null)
    {
        try
        {
            var psi = new ProcessStartInfo("dpkg-query", ["-W", "-f=${Package}\t${Version}\n"]) { RedirectStandardOutput = true };
            using var p = Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Split('\t'))
                .Where(a => a.Length == 2 && (filter is null || a[0].Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .Select(a => new PackageEntry(a[0], a[1]))
                .ToList();
        }
        catch (System.ComponentModel.Win32Exception) { return []; }
    }

    /// <summary>터미널 위젯: 한 줄 명령 실행.</summary>
    public static async Task<CommandResult> ShellAsync(string command, string? cwd = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("/bin/bash", ["-lc", command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = cwd ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        try
        {
            using var p = Process.Start(psi)!;
            var o = p.StandardOutput.ReadToEndAsync(ct);
            var e = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return new(p.ExitCode, await o, await e);
        }
        catch (System.ComponentModel.Win32Exception e) { return new(127, "", e.Message); }
    }
}
