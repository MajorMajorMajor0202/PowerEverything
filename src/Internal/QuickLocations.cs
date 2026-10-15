using System;
using System.Collections.Generic;

namespace QuickJumpCmdPal.Internal;

/// <summary>
/// 列表里的一个「可以跳过去」的文件夹。
/// </summary>
/// <param name="Path">绝对路径。</param>
/// <param name="DisplayName">列表标题（通常是文件夹名）。</param>
/// <param name="IsOpen">
/// 是否来自「已经打开的资源管理器窗口」。为 true 的候选排在其它候选前面：
/// 用户多半就是想把文件窗口挪到他刚在资源管理器里看的那个地方。
/// </param>
/// <param name="IsPinned">
/// 是否是用户自己固定到首页的文件夹。这一层排在<b>已打开的文件夹之后、默认位置之前</b>：
/// 相当于把用户常去的地方提上来，但不抢「此刻真的开着窗口」的那些的位置。
/// </param>
/// <param name="IsDefault">
/// 是否内置的默认位置（桌面 / 下载 / 文档 / …/ 各驱动器）。这一层永远垫底，
/// 而且不提供「固定到首页 / 从首页移除」—— 它们本来就一直在这儿，没有「取消」这回事。
/// </param>
internal sealed record FolderCandidate(
    string Path,
    string DisplayName,
    bool IsOpen,
    bool IsPinned = false,
    bool IsDefault = false)
{
    /// <summary>从一条固定记录造候选：路径来自设置文件，可能被用户手改过，所以这里统一清理一次。</summary>
    internal static FolderCandidate FromPinned(string path)
    {
        var normalized = QuickLocations.Normalize(path);
        var name = System.IO.Path.GetFileName(normalized);
        return new FolderCandidate(normalized, name.Length > 0 ? name : normalized, false, true);
    }
}

/// <summary>
/// 常用位置的兜底候选：当用户没开任何资源管理器窗口（或者想去的目录没开窗口）
/// 时，至少还有桌面 / 下载 / 文档 / 图片 / 音乐 / 视频 和各个驱动器可以跳。
/// </summary>
internal static class QuickLocations
{
    /// <summary>
    /// 把一条路径收拾成能存进设置文件、也能直接送进文件窗口的样子：
    /// 去掉首尾空白与用户可能粘进来的引号，再去掉多余的结尾分隔符。
    /// </summary>
    internal static string Normalize(string? path)
    {
        var value = (path ?? string.Empty).Trim().Trim('"');
        while (value.Length > 3 && (value[^1] == '\\' || value[^1] == '/'))
        {
            value = value[..^1];
        }

        return value;
    }

    /// <summary>
    /// 去重用的比较键。<c>C:\</c> 与 <c>C:</c> 指向同一个位置，只有结尾分隔符不同，
    /// 不能让它们在列表里各占一行。
    /// </summary>
    internal static string DedupeKey(string path) => Normalize(path).TrimEnd('\\', '/');

    internal static IReadOnlyList<FolderCandidate> Get()
    {
        var result = new List<FolderCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path, string fallbackName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalized = path.TrimEnd('\\', '/');
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                return;
            }

            var name = System.IO.Path.GetFileName(normalized);
            result.Add(new FolderCandidate(normalized, string.IsNullOrEmpty(name) ? fallbackName : name, false, false, true));
        }

        Add(SafeSpecialFolder(Environment.SpecialFolder.DesktopDirectory), "桌面");
        Add(SafeSpecialFolder(Environment.SpecialFolder.MyDocuments), "文档");
        Add(SafeSpecialFolder(Environment.SpecialFolder.MyPictures), "图片");
        Add(SafeSpecialFolder(Environment.SpecialFolder.MyMusic), "音乐");
        Add(SafeSpecialFolder(Environment.SpecialFolder.MyVideos), "视频");

        // 下载目录不在 SpecialFolder 枚举里，但按约定就是 %USERPROFILE%\Downloads。
        var profile = SafeSpecialFolder(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
        {
            Add(System.IO.Path.Combine(profile, "Downloads"), "下载");
        }

        // 各个驱动器根目录。
        try
        {
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady)
                    {
                        continue;
                    }

                    var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "本地磁盘" : drive.VolumeLabel;
                    var name = $"{label} ({drive.Name.TrimEnd('\\')})";
                    var normalized = drive.RootDirectory.FullName.TrimEnd('\\', '/');
                    if (seen.Add(normalized))
                    {
                        result.Add(new FolderCandidate(normalized, name, false, false, true));
                    }
                }
                catch (Exception)
                {
                    // 光驱没介质、网络盘掉线等，跳过。
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"枚举驱动器失败：{ex.Message}");
        }

        return result;
    }

    private static string? SafeSpecialFolder(Environment.SpecialFolder folder)
    {
        try
        {
            return Environment.GetFolderPath(folder);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
