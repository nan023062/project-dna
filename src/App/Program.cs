using Avalonia;
using Dna.App.Desktop;
using Dna.App.Interfaces.Cli;
using Dna.Core.Runtime;

return await ProgramEntry.RunAsync(args);

internal static class ProgramEntry
{
    [STAThread]
    public static Task<int> RunAsync(string[] args)
    {
        var launch = AppLaunchModeParser.Parse(args);
        if (launch.Kind == AppLaunchModeKind.Cli)
            return AppCliHandler.RunAsync(launch.Args);

        DesktopConsoleWindow.SuppressIfStandalone();
        return Task.FromResult(RunDesktop(launch.Args));
    }

    private static int RunDesktop(string[] args)
    {
        var desktopArgs = args
            .Where(a => !IsArg(a, "desktop", "--desktop"))
            .ToArray();

        if (!SingleInstanceLock.TryAcquire("agentic-os-app", out var instanceLock, out var lockName))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Agentic OS] 已有实例在运行（lock={lockName}）。");
            Console.ResetColor();
            return 1;
        }

        using (instanceLock)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(desktopArgs);
        }

        return 0;
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static bool IsArg(string? value, params string[] candidates)
        => value != null &&
           Array.Exists(candidates, candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
}

internal enum AppLaunchModeKind
{
    Desktop,
    Cli
}

internal sealed record AppLaunchMode(AppLaunchModeKind Kind, string[] Args);

internal static class AppLaunchModeParser
{
    public static AppLaunchMode Parse(string[] args)
    {
        if (args.Length == 0)
            return new AppLaunchMode(AppLaunchModeKind.Desktop, []);

        if (IsArg(args[0], "cli", "--cli"))
            return new AppLaunchMode(AppLaunchModeKind.Cli, args[1..]);

        if (IsArg(args[0], "desktop", "--desktop"))
            return new AppLaunchMode(AppLaunchModeKind.Desktop, args[1..]);

        return new AppLaunchMode(AppLaunchModeKind.Desktop, args);
    }

    private static bool IsArg(string? value, params string[] candidates)
        => value != null &&
           Array.Exists(candidates, candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
}

internal static partial class DesktopConsoleWindow
{
    public static void SuppressIfStandalone()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            if (GetConsoleWindow() == IntPtr.Zero)
                return;

            var attachedProcessIds = new uint[2];
            var attachedCount = GetConsoleProcessList(attachedProcessIds, (uint)attachedProcessIds.Length);

            if (attachedCount <= 1)
                FreeConsole();
        }
        catch
        {
            // Best effort: 无论是否成功，都不阻断桌面主窗口启动。
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
