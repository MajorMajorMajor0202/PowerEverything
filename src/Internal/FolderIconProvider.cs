using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace EverythingExtension.Internal;

/// <summary>
/// 读取文件夹在资源管理器里的「自定义图标」
/// （右键文件夹 → 属性 → 自定义 → 更改图标，或手工写 desktop.ini）。
///
/// 为什么不自己解析 desktop.ini 里的 IconResource：
/// 那个值可能是 <c>"imageres.dll,-3"</c>、<c>"x.ico"</c>、带环境变量的路径，
/// 或者是 <c>@{...}</c> 间接字符串，还带索引，自己解容易漏。这里直接问 Shell：
/// <see cref="ThumbnailHelper.GetThumbnail"/> 走 SHParseDisplayName + SHGetFileInfo，
/// 拿到的就是资源管理器实际显示的那个图标（含自定义图标、{CLSID} 图标等）。
///
/// 但 Shell 对「没有自定义图标」的文件夹会返回系统默认的文件夹图标，
/// 而列表里我们要用内置的 folder-default.png，所以先用 desktop.ini 判断
/// 「这个文件夹到底有没有自定义图标」，有才去问 Shell，没有就直接用内置图标。
///
/// 线程模型：问 Shell 会走 COM / WinRT，可能很慢甚至卡住，而
/// <c>ListPage.GetItems()</c> 是在命令面板的宿主线程上同步回调的。
/// 因此所有解析都放在自己起的 STA 工作线程上，并且只等一小会儿；
/// 超时或失败就当「没有自定义图标」，绝不让宿主线程卡住。
/// </summary>
internal static class FolderIconProvider
{
    #region Constants

    private const string DesktopIniName = "desktop.ini";

    /// <summary>单个批次解析图标的最长等待时间，超时则本次先用内置图标。</summary>
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(2);

    #endregion Constants

    #region Fields

    /// <summary>
    /// 路径 → 图标。值为 null 表示「该文件夹没有自定义图标」或「解析失败」。
    /// 缓存键是路径；同时记录 desktop.ini 的写入时间，用户改了图标会自动失效重取。
    /// </summary>
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    #endregion Fields

    #region Public Methods

    /// <summary>
    /// 在后台 STA 线程上预热这些文件夹的自定义图标，最多等 <see cref="ResolveTimeout"/>。
    /// 调用方随后用 <see cref="GetCached"/> 同步取结果即可。
    /// </summary>
    public static void Prefetch(IEnumerable<string> folderPaths)
    {
        var pending = new List<string>();

        foreach (var path in folderPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            if (NeedsResolve(path))
                pending.Add(path);
        }

        if (pending.Count == 0)
            return;

        var worker = new Thread(() =>
        {
            foreach (var path in pending)
            {
                try
                {
                    Resolve(path);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"解析文件夹自定义图标失败: {path} -> {ex.Message}");
                }
            }
        })
        {
            IsBackground = true,
            Name = "EverythingExtension.FolderIcons",
        };

        worker.SetApartmentState(ApartmentState.STA);

        try
        {
            worker.Start();
            if (!worker.Join(ResolveTimeout))
            {
                Debug.WriteLine("解析文件夹自定义图标超时，本次先用内置图标。");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动图标解析线程失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 取自定义图标。没有（或还没解析出来 / 解析失败）时返回 null，
    /// 调用方应回退到内置的 folder-default.png。
    /// </summary>
    public static IconInfo? GetCached(string folderPath)
        => Cache.TryGetValue(folderPath, out var entry) ? entry.Icon : null;

    #endregion Public Methods

    #region Private Methods

    private static bool NeedsResolve(string folderPath)
    {
        var stamp = CustomIconStamp(folderPath);

        // 没有自定义图标 —— 不需要问 Shell。
        if (stamp == DateTime.MinValue)
            return false;

        // 已经用同一个 desktop.ini 版本解析过了。
        if (Cache.TryGetValue(folderPath, out var entry) && entry.StampUtc == stamp)
            return false;

        return true;
    }

    private static void Resolve(string folderPath)
    {
        var stamp = CustomIconStamp(folderPath);
        if (stamp == DateTime.MinValue)
            return;

        IconInfo? icon = null;

        try
        {
            // jumbo: true —— 尽量取大图标（SHIL_JUMBO，256px），缩小时更清晰。
            var stream = ThumbnailHelper.GetThumbnail(folderPath, jumbo: true).GetAwaiter().GetResult();
            if (stream is not null)
                icon = IconInfo.FromStream(stream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"读取文件夹自定义图标失败: {folderPath} -> {ex.Message}");
        }

        Cache[folderPath] = new CacheEntry(stamp, icon);
    }

    /// <summary>
    /// 若该文件夹确实带有会被资源管理器应用的自定义图标，返回 desktop.ini 的写入时间（UTC）；
    /// 否则返回 <see cref="DateTime.MinValue"/>。
    /// </summary>
    private static DateTime CustomIconStamp(string folderPath)
    {
        try
        {
            var attributes = File.GetAttributes(folderPath);
            if (!attributes.HasFlag(FileAttributes.Directory))
                return DateTime.MinValue;

            // 资源管理器只在文件夹带「只读」或「系统」属性时才应用 desktop.ini 里的图标。
            // 这两个属性都不在的话，即使 desktop.ini 写了 IconResource 也不会生效 ——
            // 我们保持一致，免得给用户显示一个资源管理器里根本看不到的图标。
            if ((attributes & (FileAttributes.ReadOnly | FileAttributes.System)) == 0)
                return DateTime.MinValue;

            var iniPath = Path.Combine(folderPath, DesktopIniName);
            var ini = new FileInfo(iniPath);
            if (!ini.Exists)
                return DateTime.MinValue;

            return DeclaresCustomIcon(iniPath) ? ini.LastWriteTimeUtc : DateTime.MinValue;
        }
        catch (Exception)
        {
            // 无权限、路径失效、网络盘掉线等，一律当作「没有自定义图标」。
            return DateTime.MinValue;
        }
    }

    private static bool DeclaresCustomIcon(string iniPath)
    {
        try
        {
            // desktop.ini 常见是 UTF-16LE + BOM，也可能是 ANSI / UTF-8。
            // 让 StreamReader 按 BOM 自动嗅探；即使编码猜错也没关系 ——
            // 我们只找 IconResource / IconFile 这两个纯 ASCII 键名，字节序列依然能匹配。
            using var reader = new StreamReader(iniPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();

            return text.Contains("IconResource", StringComparison.OrdinalIgnoreCase)
                || text.Contains("IconFile", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    #endregion Private Methods

    #region Nested Types

    private readonly record struct CacheEntry(DateTime StampUtc, IconInfo? Icon);

    #endregion Nested Types
}
