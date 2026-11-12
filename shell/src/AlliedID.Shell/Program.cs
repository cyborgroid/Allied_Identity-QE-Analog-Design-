using Avalonia;
using AlliedID.Shell.Core.Model;

namespace AlliedID.Shell;

public sealed record ShellOptions(ShellMode Mode, bool FullScreen, int Width, int Height);

internal static class Program
{
    public static ShellOptions Options { get; private set; } = new(ShellMode.Auto, false, 1280, 800);

    /// <summary>
    /// alliedid-shell [--mode auto|phone|desktop] [--fullscreen] [--size 720x1280]
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var mode = ShellMode.Auto;
        var full = false;
        int w = 1280, h = 800;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" when i + 1 < args.Length:
                    if (!Enum.TryParse(args[++i], true, out mode)) return Usage($"unknown mode '{args[i]}'");
                    break;
                case "--fullscreen":
                    full = true;
                    break;
                case "--size" when i + 1 < args.Length:
                    var p = args[++i].Split('x');
                    if (p.Length != 2 || !int.TryParse(p[0], out w) || !int.TryParse(p[1], out h)) return Usage("size must be WxH");
                    break;
                case "--help" or "-h":
                    return Usage(null);
                default:
                    return Usage($"unknown argument '{args[i]}'");
            }
        }
        if (mode == ShellMode.Phone && args.All(a => a != "--size")) (w, h) = (420, 860);
        Options = new ShellOptions(mode, full, w, h);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime([]);
    }

    static int Usage(string? error)
    {
        if (error is not null) Console.Error.WriteLine($"alliedid-shell: {error}");
        Console.Error.WriteLine("usage: alliedid-shell [--mode auto|phone|desktop] [--fullscreen] [--size WxH]");
        return error is null ? 0 : 2;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
