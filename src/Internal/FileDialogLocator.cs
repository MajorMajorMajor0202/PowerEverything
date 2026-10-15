using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace QuickJumpCmdPal.Internal;

/// <summary>一个「打开 / 另存为」文件选择窗口。</summary>
/// <param name="Hwnd">对话框顶层窗口句柄。</param>
/// <param name="Title">窗口标题（例如「另存为」「打开」）。</param>
/// <param name="ProcessName">拥有该窗口的进程名（例如 notepad）。</param>
/// <param name="ProcessId">进程 Id。</param>
internal sealed record FileDialogWindow(IntPtr Hwnd, string Title, string ProcessName, uint ProcessId);

/// <summary>
/// 找出当前所有打开的文件选择窗口（Vista+ 通用对话框），并定位其中的「文件名」输入框。
///
/// 识别对话框（不依赖窗口标题，中英文/多语言都成立）：
///   顶层窗口类名 == <c>#32770</c>，且能看到文件视图宿主
///   （<c>DUIViewWndClassName</c> / <c>DirectUIHWND</c> / 老式的 <c>SHELLDLL_DefView</c>）。
///
/// ★ 定位「文件名」输入框是本扩展最容易踩坑的地方，实测（Windows 11 24H2 的
///   <c>GetSaveFileNameW</c> / <c>IFileDialog</c> 对话框）真实结构是：
///
///   #32770
///     ├─ DUIViewWndClassName
///     │    └─ DirectUIHWND
///     │         ├─ FloatNotifySink
///     │         │    └─ ComboBox            ← CBS_DROPDOWN（可输入）
///     │         │         └─ Edit           ← **文件名输入框就在这里**
///     │         ├─ CtrlNotifySink → NamespaceTreeControl → SysTreeView32   （导航窗格）
///     │         ├─ CtrlNotifySink → SHELLDLL_DefView → DirectUIHWND        （文件列表）
///     │         ├─ FloatNotifySink → Button
///     │         └─ FloatNotifySink → ComboBox   ← CBS_DROPDOWNLIST（「文件类型」，不可输入）
///     ├─ ToolbarWindow32 / Button(保存) / Button(取消) / Static / ListBox
///     └─ WorkerW → ReBarWindow32 → Address Band Root → msctls_progress32
///                  → ComboBoxEx32 → ComboBox → Edit      ← 这是**地址栏**，不是文件名框！
///
///   也就是说：现代对话框里 <c>ComboBoxEx32</c> 依然存在，但它在地址栏那一支里，
///   而且<b>不是</b>对话框的直属子窗口 —— 只查直属 <c>ComboBoxEx32</c> 的老写法永远找不到
///   文件名框，于是所有文件对话框都会被当成「不是文件对话框」而漏掉。
///   区分「文件名」和「文件类型」两个 ComboBox 的可靠办法是看样式：
///   前者是 <c>CBS_DROPDOWN</c>（带 Edit 子窗口），后者是 <c>CBS_DROPDOWNLIST</c>（没有 Edit 子窗口）。
/// </summary>
internal static class FileDialogLocator
{
    #region Constants

    private const string DialogClass = "#32770";
    private const string ModernViewClass = "DUIViewWndClassName";
    private const string DirectUiClass = "DirectUIHWND";
    private const string LegacyViewClass = "SHELLDLL_DefView";
    private const string ComboExClass = "ComboBoxEx32";
    private const string ComboClass = "ComboBox";
    private const string EditClass = "Edit";

    /// <summary>ComboBox 的样式低 2 位：1=简单、2=下拉（可输入）、3=下拉列表（只读）。</summary>
    private const int CbsTypeMask = 0x3;
    private const int CbsSimple = 0x1;
    private const int CbsDropDown = 0x2;

    /// <summary>
    /// 递归时整支跳过的控件：地址栏 / 工具条 / 树视图里也有 ComboBox 和 Edit，
    /// 但它们不是「文件名」输入框，误判会把路径写错地方。
    /// </summary>
    private static readonly string[] SkippedBranches =
    [
        "ToolbarWindow32",
        "ReBarWindow32",
        "msctls_progress32",
        "SysTreeView32",
        "ScrollBar",
        "Breadcrumb Parent",
    ];

    /// <summary>
    /// 枚举结果缓存时长。宿主要求重取列表时可能连着来好几次
    /// （每敲一个字一次），而枚举本身要遍历所有顶层窗口并查进程名，
    /// 缓存几百毫秒既能让输入保持跟手，又不会让结果过期太久。
    /// </summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMilliseconds(400);

    private static readonly object Gate = new();

    private static List<FileDialogWindow> _cache = [];

    private static DateTime _cacheStamp = DateTime.MinValue;

    #endregion Constants

    #region Public Methods

    /// <summary>列出所有文件选择窗口，顺序为窗口 Z 序（最上面的在前）。</summary>
    /// <param name="force">true 表示忽略缓存重新枚举。</param>
    internal static IReadOnlyList<FileDialogWindow> FindAll(bool force = false)
    {
        lock (Gate)
        {
            if (!force && DateTime.UtcNow - _cacheStamp < CacheLifetime)
            {
                return _cache;
            }
        }

        var found = Enumerate();

        lock (Gate)
        {
            _cache = found;
            _cacheStamp = DateTime.UtcNow;
        }

        return found;
    }

    /// <summary>取对话框里「文件名」输入框的句柄（找不到返回 <see cref="IntPtr.Zero"/>）。</summary>
    internal static IntPtr FindFileNameEdit(IntPtr dialog)
    {
        if (dialog == IntPtr.Zero || !NativeMethods.IsWindow(dialog))
        {
            return IntPtr.Zero;
        }

        // 1) Win10/11 的 DirectUI 对话框（主流情况）
        var dui = NativeMethods.FindWindowEx(dialog, IntPtr.Zero, ModernViewClass, null);
        if (dui != IntPtr.Zero)
        {
            var edit = FindEditableComboEdit(dui, 0);
            if (edit != IntPtr.Zero)
            {
                return edit;
            }
        }

        // 2) Vista / Win7 风格：直属 ComboBoxEx32 > ComboBox > Edit
        var comboEx = NativeMethods.FindWindowEx(dialog, IntPtr.Zero, ComboExClass, null);
        if (comboEx != IntPtr.Zero)
        {
            var edit = NativeMethods.FindWindowEx(comboEx, IntPtr.Zero, EditClass, null);
            if (edit != IntPtr.Zero)
            {
                return edit;
            }

            var inner = NativeMethods.FindWindowEx(comboEx, IntPtr.Zero, ComboClass, null);
            if (inner != IntPtr.Zero)
            {
                edit = NativeMethods.FindWindowEx(inner, IntPtr.Zero, EditClass, null);
                if (edit != IntPtr.Zero)
                {
                    return edit;
                }
            }
        }

        // 3) 整个对话框里再兜一层（会自动跳过地址栏那一支）
        var deep = FindEditableComboEdit(dialog, 0);
        if (deep != IntPtr.Zero)
        {
            return deep;
        }

        // 4) XP 老式对话框：文件名框是对话框的直属 Edit（edt1）
        return NativeMethods.FindWindowEx(dialog, IntPtr.Zero, EditClass, null);
    }

    /// <summary>
    /// 把当前所有顶层窗口的普查结果写成多行文本（写日志用）。
    /// 检测不到文件窗口时，这份快照是唯一能看出「对方到底是什么窗口」的东西。
    /// </summary>
    internal static string Census()
    {
        var sb = new StringBuilder();
        var total = 0;
        var visible = 0;
        var unreadable = 0;
        var histogram = new Dictionary<string, int>(StringComparer.Ordinal);
        var dialogs = new List<IntPtr>();

        NativeMethods.EnumWindows(
            (hwnd, _) =>
            {
                total++;
                if (NativeMethods.IsWindowVisible(hwnd))
                {
                    visible++;
                }

                var cls = NativeMethods.ClassNameOf(hwnd);
                if (cls.Length == 0)
                {
                    unreadable++;
                }

                var key = cls.Length == 0 ? "(类名读不到)" : cls;
                histogram[key] = histogram.TryGetValue(key, out var count) ? count + 1 : 1;

                if (string.Equals(cls, DialogClass, StringComparison.Ordinal))
                {
                    dialogs.Add(hwnd);
                }

                return true;
            },
            IntPtr.Zero);

        sb.AppendLine(CultureInfo.InvariantCulture, $"顶层窗口共 {total} 个（可见 {visible}，类名读不到 {unreadable}）");
        sb.AppendLine("类名分布（前 15）：");
        foreach (var pair in histogram.OrderByDescending(p => p.Value).Take(15))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"    {pair.Value,4}  {pair.Key}");
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"#32770 对话框共 {dialogs.Count} 个：");
        foreach (var dialog in dialogs)
        {
            if (NativeMethods.GetWindowThreadProcessId(dialog, out var pid) == 0)
            {
                pid = 0;
            }

            var edit = FindFileNameEdit(dialog);
            var view = NativeMethods.FindWindowEx(dialog, IntPtr.Zero, ModernViewClass, null) != IntPtr.Zero
                || NativeMethods.FindWindowEx(dialog, IntPtr.Zero, DirectUiClass, null) != IntPtr.Zero
                || FindChildRecursive(dialog, LegacyViewClass, 4) != IntPtr.Zero;

            sb.AppendLine(
                CultureInfo.InvariantCulture,
                $"    0x{dialog.ToInt64():X} pid={pid} visible={NativeMethods.IsWindowVisible(dialog)} "
                + $"title=\"{NativeMethods.TitleOf(dialog)}\" 文件名框=0x{edit.ToInt64():X} 文件视图={view}");

            if (edit != IntPtr.Zero)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"        文件名框的祖先链：{AncestorPath(edit, dialog)}");
            }
        }

        return sb.ToString();
    }

    #endregion Public Methods

    #region Private Methods

    private static List<FileDialogWindow> Enumerate()
    {
        var result = new List<FileDialogWindow>();
        var ownProcessId = (uint)Environment.ProcessId;

        try
        {
            NativeMethods.EnumWindows(
                (hwnd, _) =>
                {
                    try
                    {
                        if (!NativeMethods.IsWindowVisible(hwnd))
                        {
                            return true;
                        }

                        if (!string.Equals(NativeMethods.ClassNameOf(hwnd), DialogClass, StringComparison.Ordinal))
                        {
                            return true;
                        }

                        if (!LooksLikeFileDialog(hwnd))
                        {
                            return true;
                        }

                        if (FindFileNameEdit(hwnd) == IntPtr.Zero)
                        {
                            return true;
                        }

                        // 返回值是窗口线程 id，这里只用来判断调用是否成功（失败时没法确认窗口归属，
                        // 宁可跳过也不要往别人的窗口里塞东西）。
                        var windowThread = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                        if (windowThread == 0)
                        {
                            return true;
                        }

                        if (pid == ownProcessId)
                        {
                            return true;
                        }

                        result.Add(new FileDialogWindow(hwnd, NativeMethods.TitleOf(hwnd), ProcessNameOf(pid), pid));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"检查窗口 {hwnd} 时出错：{ex.Message}");
                    }

                    return true;
                },
                IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "枚举顶层窗口失败");
        }

        Log.Info($"检测到 {result.Count} 个文件选择窗口：{string.Join(" | ", result.Select(d => $"{d.Title}({d.ProcessName})"))}");
        return result;
    }

    /// <summary>是不是「文件」对话框：得有字面意义上的文件视图（导航窗格 / 文件列表）。</summary>
    private static bool LooksLikeFileDialog(IntPtr dialog)
    {
        if (NativeMethods.FindWindowEx(dialog, IntPtr.Zero, ModernViewClass, null) != IntPtr.Zero)
        {
            return true;
        }

        if (NativeMethods.FindWindowEx(dialog, IntPtr.Zero, DirectUiClass, null) != IntPtr.Zero)
        {
            return true;
        }

        return FindChildRecursive(dialog, LegacyViewClass, 4) != IntPtr.Zero;
    }

    /// <summary>
    /// 递归找「可输入的下拉框」里的 Edit —— 那就是「文件名」输入框。
    /// 只读下拉框（<c>CBS_DROPDOWNLIST</c>，也就是「文件类型」）没有 Edit 子窗口，天然被排除。
    /// </summary>
    private static IntPtr FindEditableComboEdit(IntPtr parent, int depth)
    {
        if (depth > 12)
        {
            return IntPtr.Zero;
        }

        var child = NativeMethods.FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var cls = NativeMethods.ClassNameOf(child);

            if (Array.IndexOf(SkippedBranches, cls) >= 0)
            {
                child = NativeMethods.FindWindowEx(parent, child, null, null);
                continue;
            }

            if (string.Equals(cls, ComboClass, StringComparison.Ordinal))
            {
                var kind = NativeMethods.StyleOf(child) & CbsTypeMask;
                if (kind == CbsDropDown || kind == CbsSimple)
                {
                    var edit = NativeMethods.FindWindowEx(child, IntPtr.Zero, EditClass, null);
                    if (edit != IntPtr.Zero)
                    {
                        return edit;
                    }
                }
            }

            var found = FindEditableComboEdit(child, depth + 1);
            if (found != IntPtr.Zero)
            {
                return found;
            }

            child = NativeMethods.FindWindowEx(parent, child, null, null);
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindChildRecursive(IntPtr parent, string className, int depth)
    {
        if (depth <= 0)
        {
            return IntPtr.Zero;
        }

        var direct = NativeMethods.FindWindowEx(parent, IntPtr.Zero, className, null);
        if (direct != IntPtr.Zero)
        {
            return direct;
        }

        var child = NativeMethods.FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var found = FindChildRecursive(child, className, depth - 1);
            if (found != IntPtr.Zero)
            {
                return found;
            }

            child = NativeMethods.FindWindowEx(parent, child, null, null);
        }

        return IntPtr.Zero;
    }

    /// <summary>从某个控件往上走到 <paramref name="root"/>，拼出「类名 > 类名」的路径（诊断用）。</summary>
    private static string AncestorPath(IntPtr child, IntPtr root)
    {
        var parts = new List<string>();
        var current = child;
        var guard = 0;
        while (current != IntPtr.Zero && current != root && guard++ < 20)
        {
            parts.Add(NativeMethods.ClassNameOf(current));
            current = NativeMethods.GetParent(current);
        }

        parts.Reverse();
        return string.Join(" > ", parts);
    }

    private static string ProcessNameOf(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            // 目标是提权进程时可能读不到，属于正常情况。
            return "未知进程";
        }
    }

    #endregion Private Methods
}
