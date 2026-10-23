using System.Diagnostics;

namespace SwallowIt;

internal static class Program
{
    private const string SingleInstanceName = @"Local\SwallowIt.SingleInstance";

    private static bool _showingError;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            ActivateExistingInstance();
            return;
        }

        var startHidden = args.Any(arg =>
            string.Equals(arg, "--autostart", StringComparison.OrdinalIgnoreCase));

        Application.Run(new TrayApp(startHidden));
    }

    private static void ActivateExistingInstance()
    {
        var window = NativeMethods.FindWindow(null, MainForm.WindowTitle);
        if (window == IntPtr.Zero)
        {
            using var process = Process.GetProcessesByName("SwallowIt")
                .FirstOrDefault(p => p.Id != Environment.ProcessId);
            window = process?.MainWindowHandle ?? IntPtr.Zero;
        }

        if (window == IntPtr.Zero)
        {
            AppDialog.ShowInfo(null, "SwallowIt 已在运行，请查看系统托盘。");
            return;
        }

        NativeMethods.AllowSetForegroundWindow(-1);
        NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
        NativeMethods.ShowWindow(window, NativeMethods.SW_SHOW);
        NativeMethods.SetForegroundWindow(window);
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        AppLog.Write("界面线程未处理异常", e.Exception);

        if (_showingError)
        {
            return;
        }

        _showingError = true;
        try
        {
            AppDialog.ShowError(null,
                "程序遇到一个问题，当前操作未能完成。\n\n" +
                "你可以继续使用其他功能；如果问题反复出现，请关闭程序后重新打开。\n\n" +
                "详细信息已记录到日志文件。");
        }
        finally
        {
            _showingError = false;
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "未知错误。");
        AppLog.Write("后台线程未处理异常", exception);

        try
        {
            AppDialog.ShowError(null,
                "程序遇到无法恢复的问题，即将退出。\n\n" +
                "详细信息已记录到日志文件。");
        }
        catch
        {
            // 进程正在退出，提示失败时不再做额外处理。
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Write("后台任务未处理异常", e.Exception);
        e.SetObserved();
    }
}
