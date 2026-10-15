using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickJumpCmdPal.Internal;

/// <summary>
/// 与文件选择窗口（「打开 / 另存为」对话框）打交道的 Win32 入口。
///
/// 关于跨进程 <c>SendMessage(WM_SETTEXT / WM_GETTEXT)</c>：
/// 这两个都是「系统消息」（&lt; WM_USER），user32 会替调用方在目标进程里
/// 分配/读取缓冲区，所以从本进程直接设置另一个进程窗口的文本是安全的、
/// 也是所有同类工具（AutoHotkey、Listary 等）的常规做法。
/// </summary>
internal static class NativeMethods
{
    #region Constants

    internal const int WM_SETTEXT = 0x000C;
    internal const int WM_GETTEXT = 0x000D;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_COMMAND = 0x0111;
    internal const int VK_RETURN = 0x0D;
    internal const int IDOK = 1;
    internal const int SW_RESTORE = 9;
    internal const uint SMTO_ABORTIFHUNG = 0x0002;
    internal const uint KEYEVENTF_KEYUP = 0x0002;

    #endregion Constants

    #region Delegates

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    #endregion Delegates

    #region Window enumeration

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetParent(IntPtr hWnd);

    /// <summary><c>GWL_STYLE</c> 固定是 -16。样式本身是 32 位，所以用 GetWindowLongW 不会被截断。</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static extern int GetWindowLongW(IntPtr hWnd, int nIndex);

    #endregion Window enumeration

    #region Foreground / input

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    /// <summary>
    /// 模拟一次回车。用老大难的 <c>keybd_event</c> 而不是 <c>SendInput</c>：
    /// SendInput 要求 <c>cbSize</c> 与系统里的 <c>INPUT</c> 结构体大小完全一致
    /// （x64 上是 40 字节），自己定义的结构体稍有出入就会静默失败（返回 0），
    /// 而 keybd_event 没有这个坑，且对「给当前前台窗口发回车」这个用途足够。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    #endregion Foreground / input

    #region Messages

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SendMessageTimeout(IntPtr hWnd, int msg, IntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    #endregion Messages

    #region Helpers

    /// <summary>读取窗口类名（取不到时返回空串）。</summary>
    internal static string ClassNameOf(IntPtr hWnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(hWnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    /// <summary>读取窗口标题（取不到时返回空串）。</summary>
    internal static string TitleOf(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(length + 2);
        var copied = GetWindowText(hWnd, buffer, buffer.Capacity);
        return copied > 0 ? buffer.ToString(0, copied) : string.Empty;
    }

    /// <summary>读取窗口样式（<c>GWL_STYLE</c>）。</summary>
    internal static int StyleOf(IntPtr hWnd) => GetWindowLongW(hWnd, -16);

    /// <summary>
    /// 把某个窗口强行拉到前台。
    /// 跨进程 <c>SetForegroundWindow</c> 会被前台锁定（foreground lock）挡住，
    /// 常规解法是先把本线程的输入队列附着到当前前台线程上。
    /// </summary>
    internal static void TryBringToForeground(IntPtr hWnd)
    {
        try
        {
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }

            var foreground = GetForegroundWindow();
            var currentThread = GetCurrentThreadId();
            var foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);

            var attached = false;
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attached = AttachThreadInput(currentThread, foregroundThread, true);
            }

            SetForegroundWindow(hWnd);

            if (attached)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"把窗口拉到前台失败：{ex.Message}");
        }
    }

    #endregion Helpers
}
