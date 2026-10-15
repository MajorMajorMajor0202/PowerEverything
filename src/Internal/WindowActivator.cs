// 让「刚由资源管理器打开的文件夹窗口」真正浮到最前。

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace EverythingExtension.Internal;

/// <summary>
/// 把新打开的资源管理器窗口提到最前。
///
/// 为什么需要它：扩展进程在后台跑（当时处于前台的是命令面板自己）。Windows 的
/// 「前台锁定」规则下，后台进程间接拉起的窗口没有资格抢前台 —— 于是文件夹窗口
/// 确实开了，却停在其它窗口后面，用户按回车后看不到它。
///
/// 做法：打开之后轮询查找标题匹配的资源管理器窗口，再用
/// <c>AttachThreadInput</c> 把本线程与前台线程的输入队列接上（这是从后台进程
/// 强行置前的标准手法），然后 <c>BringWindowToTop</c> + <c>SetForegroundWindow</c>。
/// 全程不改动用例外的输入模拟，失败也只是退化成原来的行为（窗口在后台）。
/// </summary>
internal static class WindowActivator
{
    #region Constants

    private const int SwRestore = 9;

    /// <summary>等窗口出现的最长时间：资源管理器通常 100~300ms 就绪。</summary>
    private const int MaxWaitMs = 2500;

    private const int PollIntervalMs = 80;

    /// <summary>Win11 与旧版资源管理器的窗口类名。</summary>
    private static readonly string[] ExplorerWindowClasses = ["CabinetWClass", "ExploreWClass"];

    #endregion Constants

    #region Public Methods

    /// <summary>
    /// 等待 <paramref name="folderPath"/> 对应的资源管理器窗口出现并把它置前。
    /// 找不到就静默返回（不影响「打开」本身）。
    /// </summary>
    internal static void ActivateExplorerWindowFor(string folderPath)
    {
        try
        {
            var fragments = BuildTitleFragments(folderPath);
            if (fragments.Length == 0)
            {
                return;
            }

            var flowForeground = GetForegroundWindow();
            var deadline = Environment.TickCount64 + MaxWaitMs;

            while (Environment.TickCount64 < deadline)
            {
                var hwnd = FindExplorerWindow(fragments, flowForeground);
                if (hwnd != IntPtr.Zero)
                {
                    ForceForeground(hwnd);
                    return;
                }

                Thread.Sleep(PollIntervalMs);
            }
        }
        catch
        {
            // 置前只是体验优化，任何异常都不该影响「打开」这个动作本身。
        }
    }

    #endregion Public Methods

    #region Private Methods

    /// <summary>
    /// 资源管理器窗口标题就是文件夹显示名（例如 <c>D:\Work\Reports</c> → "Reports"）。
    /// 驱动器根没有名字，标题形如 "本地磁盘 (D:)"，退化用 "(D:)" 匹配。
    /// </summary>
    private static string[] BuildTitleFragments(string folderPath)
    {
        var name = new DirectoryInfo(folderPath).Name;
        if (name.Length > 0)
        {
            return [name];
        }

        var root = Path.GetPathRoot(folderPath);
        return root is { Length: >= 2 }
            ? [$"({root.TrimEnd('\\', '/')})", root.TrimEnd('\\', '/')]
            : [];
    }

    private static IntPtr FindExplorerWindow(string[] fragments, IntPtr currentForeground)
    {
        var found = IntPtr.Zero;

        _ = EnumWindows((hwnd, lParam) =>
        {
            if (hwnd == currentForeground || !IsWindowVisible(hwnd))
            {
                return true; // 已经是前台的窗口不用管，继续找
            }

            var className = ClassNameOf(hwnd);
            if (Array.IndexOf(ExplorerWindowClasses, className) < 0)
            {
                return true;
            }

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (!IsExplorerProcess(pid))
            {
                return true;
            }

            var title = TitleOf(hwnd);
            foreach (var fragment in fragments)
            {
                if (title.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                {
                    found = hwnd;
                    return false; // 命中，停止枚举
                }
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static bool IsExplorerProcess(uint pid)
    {
        try
        {
            return string.Equals(Process.GetProcessById((int)pid).ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void ForceForeground(IntPtr hwnd)
    {
        if (IsIconic(hwnd))
        {
            _ = ShowWindow(hwnd, SwRestore);
        }

        if (GetForegroundWindow() == hwnd)
        {
            return;
        }

        // 接上前台线程的输入队列：这样本线程做出的前台变更才不会被系统拒绝。
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var thisThread = GetCurrentThreadId();
        var attached = foregroundThread != 0
            && foregroundThread != thisThread
            && AttachThreadInput(thisThread, foregroundThread, true);

        try
        {
            _ = BringWindowToTop(hwnd);
            _ = SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(thisThread, foregroundThread, false);
            }
        }

        if (GetForegroundWindow() == hwnd)
        {
            return;
        }

        // 第一招没成：改成接上【目标窗口】自己的线程再试一次。
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        if (targetThread == 0 || targetThread == thisThread)
        {
            return;
        }

        var attachedToTarget = AttachThreadInput(thisThread, targetThread, true);
        try
        {
            _ = BringWindowToTop(hwnd);
            _ = SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedToTarget)
            {
                _ = AttachThreadInput(thisThread, targetThread, false);
            }
        }
    }

    private static string ClassNameOf(IntPtr hwnd)
    {
        var buffer = new char[64];
        var length = GetClassName(hwnd, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    private static string TitleOf(IntPtr hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowText(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    #endregion Private Methods

    #region Native Methods

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    // 字符串出口一律用 char[]（CA1838：不要对 P/Invoke 用 StringBuilder）。
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr hwnd, [Out] char[] className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr hwnd, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    #endregion Native Methods
}
