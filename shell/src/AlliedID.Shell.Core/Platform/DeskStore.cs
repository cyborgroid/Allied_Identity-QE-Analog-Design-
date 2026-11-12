using AlliedID.Shell.Core.DeskScript;
using AlliedID.Shell.Core.Model;

namespace AlliedID.Shell.Core.Platform;

/// <summary>
/// <c>~/.config/alliedid/desk.aid</c> (DeskScript) 를 읽고 감시한다.
/// 블록 편집기가 이 파일을 저장하면 바탕화면이 즉시 재조립된다.
/// </summary>
public sealed class DeskStore : IDisposable
{
    public string Path { get; }
    FileSystemWatcher? _watcher;
    Timer? _debounce;

    public event Action<CompileResult>? Reloaded;

    public DeskStore(string? path = null)
    {
        Path = path ?? Environment.GetEnvironmentVariable("AID_DESK_FILE") ?? DefaultPath();
    }

    public static string DefaultPath()
    {
        var cfg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(cfg))
            cfg = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        return System.IO.Path.Combine(cfg, "alliedid", "desk.aid");
    }

    public CompileResult Load()
    {
        string src;
        try { src = File.ReadAllText(Path); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { src = DeskLayout.DefaultScript; }
        var r = DeskScriptCompiler.Compile(src);
        // 사용자 파일이 깨졌으면 기본 바탕화면 + 오류 목록
        return r.Ok ? r : new CompileResult(DeskScriptCompiler.Compile(DeskLayout.DefaultScript).Layout, r.Errors);
    }

    public void Save(DeskLayout layout)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, DeskScriptCompiler.Emit(layout));
    }

    public void Watch()
    {
        var dir = System.IO.Path.GetDirectoryName(Path)!;
        Directory.CreateDirectory(dir);
        _watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        FileSystemEventHandler h = (_, _) => _debounce?.Change(250, Timeout.Infinite);
        _debounce = new Timer(_ => Reloaded?.Invoke(Load()));
        _watcher.Changed += h;
        _watcher.Created += h;
        _watcher.Renamed += (_, _) => _debounce.Change(250, Timeout.Infinite);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
