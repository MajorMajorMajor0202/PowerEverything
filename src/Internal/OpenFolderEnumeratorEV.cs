using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace EverythingExtension.Internal;

/// <summary>
/// 枚举当前所有已打开的资源管理器（Explorer）文件夹窗口，返回其本地路径。
/// 通过 Shell.Application COM 对象（IShellWindows）实现。
///
/// 注意 1：COM 后期绑定要求在 STA 线程上执行，而命令面板是在宿主线程上回调
///         <c>ListPage.GetItems()</c> 的，线程套间不确定。因此这里显式起一个 STA
///         线程并设置超时，避免把宿主线程挂死。
/// 注意 2：此处使用后期绑定 COM（dynamic + Type.GetTypeFromProgID）。在调试 / 侧载构建中可用；
///         若要以 AOT 剪裁方式发布，应改为静态 ComImport 互操作（IShellWindows / IWebBrowser2），
///         否则 dynamic 与反射式 COM 在剪裁后运行时会失败。
/// </summary>
internal static class OpenFolderEnumerator
{
    /// <summary>枚举超时，避免异常情况下卡住命令面板。</summary>
    private static readonly TimeSpan EnumerationTimeout = TimeSpan.FromSeconds(3);

    public sealed record OpenFolder(string Path, string DisplayName);

    public static IReadOnlyList<OpenFolder> GetOpenFolders()
    {
        List<OpenFolder>? collected = null;
        Exception? failure = null;

        var worker = new Thread(() =>
        {
            try
            {
                collected = CollectOnStaThread();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "EverythingExtension.EnumerateOpenFolders",
        };

        worker.SetApartmentState(ApartmentState.STA);

        try
        {
            worker.Start();
            if (!worker.Join(EnumerationTimeout))
            {
                Debug.WriteLine("枚举打开的文件夹窗口超时。");
                return [];
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动枚举线程失败: {ex}");
            return [];
        }

        if (failure is not null)
            Debug.WriteLine($"枚举打开的文件夹窗口失败: {failure}");

        return collected ?? [];
    }

    private static List<OpenFolder> CollectOnStaThread()
    {
        // 同一个文件夹可能开了多个窗口（或开了多标签），按路径去重。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<OpenFolder>();

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null)
            return result;

        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            if (shell is null)
                return result;

            dynamic windows = shell.Windows();
            if (windows is not System.Collections.IEnumerable enumerable)
                return result;

            foreach (dynamic window in enumerable)
            {
                try
                {
                    string? url = window.LocationURL;
                    if (string.IsNullOrWhiteSpace(url) ||
                        !url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        // 「此电脑」「库」「控制面板」等虚拟位置会走到这里，直接忽略。
                        continue;
                    }

                    var path = new Uri(url).LocalPath.TrimEnd('\\', '/');
                    if (string.IsNullOrEmpty(path) || !seen.Add(path))
                        continue;

                    string name = window.LocationName;
                    if (string.IsNullOrWhiteSpace(name))
                        name = System.IO.Path.GetFileName(path);
                    if (string.IsNullOrWhiteSpace(name))
                        name = path;

                    result.Add(new OpenFolder(path, name));
                }
                catch
                {
                    // 某些窗口无法转换为本地路径，忽略。
                }
            }
        }
        finally
        {
            if (shell is not null)
            {
                try
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                }
                catch
                {
                    // 释放失败无所谓，进程退出时会回收。
                }
            }
        }

        return result
            .OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
