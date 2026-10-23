using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace SwallowIt;

internal static class WindowTools
{
    private const int TitleBufferLength = 1024;
    private const int ClassBufferLength = 256;

    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "ForegroundStaging"
    };

    public static IntPtr GetRootWindow(IntPtr window)
    {
        return NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT);
    }

    public static string GetTitle(IntPtr window)
    {
        var buffer = new StringBuilder(TitleBufferLength);
        NativeMethods.GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static string GetClassName(IntPtr window)
    {
        var buffer = new StringBuilder(ClassBufferLength);
        NativeMethods.GetClassName(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    public static int GetProcessId(IntPtr window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return unchecked((int)processId);
    }

    public static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return "未知进程";
        }
    }

    public static long GetProcessStartTimeTicks(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime.ToUniversalTime().Ticks;
        }
        catch
        {
            return 0;
        }
    }

    public static bool IsShellWindow(string className)
    {
        return ShellWindowClasses.Contains(className);
    }

    public static Rectangle GetWindowBounds(IntPtr window)
    {
        if (NativeMethods.DwmGetWindowAttribute(
                window,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeMethods.RECT rect,
                Marshal.SizeOf<NativeMethods.RECT>()) == 0
            && rect.Right > rect.Left
            && rect.Bottom > rect.Top)
        {
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        if (NativeMethods.GetWindowRect(window, out rect)
            && rect.Right > rect.Left
            && rect.Bottom > rect.Top)
        {
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        return Rectangle.Empty;
    }

    public static bool IsWindowCloaked(IntPtr window)
    {
        return NativeMethods.DwmGetWindowAttribute(
                window,
                NativeMethods.DWMWA_CLOAKED,
                out int cloaked,
                Marshal.SizeOf<int>()) == 0
            && cloaked != 0;
    }

    public static bool MatchesStoredIdentity(ServiceEntry entry)
    {
        if (entry.WindowHandle == 0 || !NativeMethods.IsWindow(entry.Handle))
        {
            return false;
        }

        if (GetProcessId(entry.Handle) != entry.ProcessId)
        {
            return false;
        }

        if (entry.ProcessStartTimeTicks == 0)
        {
            return true;
        }

        var currentTicks = GetProcessStartTimeTicks(entry.ProcessId);
        return currentTicks == 0 || currentTicks == entry.ProcessStartTimeTicks;
    }

    public static bool TryValidatePickTarget(IntPtr rootWindow, int ownProcessId, out string error)
    {
        error = string.Empty;

        if (rootWindow == IntPtr.Zero || !NativeMethods.IsWindow(rootWindow))
        {
            error = "没有识别到可收纳的窗口，请重试。";
            return false;
        }

        if (!NativeMethods.IsWindowVisible(rootWindow))
        {
            error = "这个窗口当前不可见，无法收纳。";
            return false;
        }

        var processId = GetProcessId(rootWindow);
        if (processId == 0)
        {
            error = "桌面或系统窗口不能收纳。";
            return false;
        }

        if (processId == ownProcessId)
        {
            error = "不能收纳 SwallowIt 自己的窗口。";
            return false;
        }

        if (IsShellWindow(GetClassName(rootWindow)))
        {
            error = "桌面或任务栏不能收纳。";
            return false;
        }

        return true;
    }

    public static bool Hide(IntPtr window)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return false;
        }

        NativeMethods.ShowWindow(window, NativeMethods.SW_HIDE);
        return !NativeMethods.IsWindowVisible(window);
    }

    public static bool Show(IntPtr window, bool activate = true)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return false;
        }

        if (!activate)
        {
            NativeMethods.ShowWindow(window, NativeMethods.SW_SHOWNOACTIVATE);
            return NativeMethods.IsWindowVisible(window);
        }

        NativeMethods.ShowWindow(window,
            NativeMethods.IsIconic(window) ? NativeMethods.SW_RESTORE : NativeMethods.SW_SHOW);
        NativeMethods.SetForegroundWindow(window);

        return NativeMethods.IsWindowVisible(window);
    }

    public static void BringToFront(IntPtr window)
    {
        if (!NativeMethods.IsWindow(window))
        {
            return;
        }

        if (NativeMethods.IsIconic(window))
        {
            NativeMethods.ShowWindow(window, NativeMethods.SW_RESTORE);
        }

        NativeMethods.SetForegroundWindow(window);
    }

    public static Image GetWindowIcon(IntPtr window, int processId)
    {
        try
        {
            var iconHandle = NativeMethods.SendMessage(
                window, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_BIG), IntPtr.Zero);

            if (iconHandle == IntPtr.Zero)
            {
                iconHandle = NativeMethods.SendMessage(
                    window, NativeMethods.WM_GETICON, new IntPtr(NativeMethods.ICON_SMALL), IntPtr.Zero);
            }

            if (iconHandle == IntPtr.Zero)
            {
                iconHandle = NativeMethods.GetClassLongPtr(window, NativeMethods.GCLP_HICON);
            }

            if (iconHandle == IntPtr.Zero)
            {
                iconHandle = NativeMethods.GetClassLongPtr(window, NativeMethods.GCLP_HICONSM);
            }

            if (iconHandle != IntPtr.Zero)
            {
                using var sharedIcon = Icon.FromHandle(iconHandle);
                using var bitmap = sharedIcon.ToBitmap();
                return ResizeIcon(bitmap);
            }
        }
        catch
        {
            // Fall through to the process icon.
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            var executablePath = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                using var processIcon = Icon.ExtractAssociatedIcon(executablePath);
                if (processIcon is not null)
                {
                    using var bitmap = processIcon.ToBitmap();
                    return ResizeIcon(bitmap);
                }
            }
        }
        catch
        {
            // Fall through to the generic icon.
        }

        return SystemIcons.Application.ToBitmap();
    }

    private static Image ResizeIcon(Image source)
    {
        var target = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, 0, 0, 24, 24);
        return target;
    }
}
