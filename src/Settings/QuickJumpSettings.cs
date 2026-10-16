using Microsoft.CommandPalette.Extensions.Toolkit;

using QuickJumpCmdPal.Internal;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QuickJumpCmdPal.Settings;

/// <summary>
/// 本扩展自己的设置：
///   - 填入路径后是否自动回车；
///   - 固定在首页的文件夹清单。
///
/// 「触发文本（别名）」和「全局快捷键」曾经也在这里，但那是重复造轮子 ——
/// 命令面板宿主<b>自带</b>这两项（扩展列表里的每个命令都能配别名与快捷键，
/// 扩展侧根本不需要知道），而且扩展 SDK 里也没有对应 API。
/// 所以本扩展不再掺和这两样，相关的写宿主设置文件的代码已经删掉。
/// </summary>
internal sealed class QuickJumpSettings : JsonSettingsManager
{
    private readonly ToggleSetting _autoEnter = new(
        "quickJump.AutoEnter",
        "填入路径后自动回车",
        "打开：填进路径后立刻回车跳过去。关闭：只填路径，自己按回车确认。",
        true);

    /// <summary>
    /// 「固定到首页」的文件夹清单，一行一个路径。
    ///
    /// 用 <see cref="TextSetting"/> + <c>Multiline</c>（而不是新版的 StringListSetting —— 那个
    /// 本扩展依赖的 SDK 版本 0.9.260303001 里还没有）：值跟着其它设置一起写进 settings.json，
    /// 宿主会把它渲染成设置页里的多行输入框，用户能手改；
    /// 日常操作则是对着列表里的文件夹按 Ctrl+K →「固定到首页」。
    ///
    /// 注意：代码改了 <c>Value</c> 之后必须自己调 <see cref="JsonSettingsManager.SaveSettings"/>，
    /// 宿主只在用户点设置页的「保存」时才写盘。
    /// </summary>
    private readonly TextSetting _pinned = new(
        "quickJump.PinnedFolders",
        "固定文件夹",
        "固定在首页的文件夹，一行一个路径。在列表里对某个文件夹按 Ctrl+K →「固定到首页」也能添加。",
        string.Empty)
    {
        Multiline = true,
        Placeholder = @"C:\Projects",
    };

    public QuickJumpSettings()
    {
        FilePath = SettingsJsonPath();

        // 顺序很重要：JsonSettingsManager.LoadSettings() 内部走的是 Settings.Update()，
        // 它只对【已经 Add 进去】的设置项生效。先 Add 再 Load，上次保存的值才会被读回来。
        Settings.Add(_autoEnter);
        Settings.Add(_pinned);

        LoadSettings();

        Log.Info($"设置文件：{FilePath}；自动回车={AutoEnter}；固定在首页的文件夹 {PinnedFolders.Count} 个");
    }

    internal static readonly QuickJumpSettings Instance = new();

    /// <summary>填入路径后是否自动回车。</summary>
    internal bool AutoEnter => _autoEnter.Value;

    /// <summary>固定在首页的文件夹（按用户固定时的先后顺序，已去重、已规范化）。</summary>
    internal IReadOnlyList<string> PinnedFolders => ParsePinned(_pinned.Value);

    /// <summary>这个文件夹是不是已经固定在首页了。</summary>
    internal bool IsPinned(string path)
        => PinnedFolders.Any(p => SamePath(p, path));

    /// <summary>把一个文件夹固定到首页；已经在清单里就什么都不做，返回 false。</summary>
    internal bool Pin(string path)
    {
        var normalized = QuickLocations.Normalize(path);
        if (normalized.Length == 0 || IsPinned(normalized))
        {
            return false;
        }

        Store([.. PinnedFolders, normalized]);
        Log.Info($"已固定到首页：{normalized}");
        return true;
    }

    /// <summary>把一个文件夹从首页移除；本来就不在清单里则返回 false。</summary>
    internal bool Unpin(string path)
    {
        var remaining = PinnedFolders.Where(p => !SamePath(p, path)).ToList();
        if (remaining.Count == PinnedFolders.Count)
        {
            return false;
        }

        Store(remaining);
        Log.Info($"已从首页移除：{path}");
        return true;
    }

    /// <summary>把清单写回设置文件（顺手立刻存盘，命令面板被关掉也不会丢）。</summary>
    private void Store(IEnumerable<string> paths)
    {
        _pinned.Value = string.Join(Environment.NewLine, paths);
        SaveSettings();
    }

    /// <summary>
    /// 把多行文本拆成路径清单：顺手去掉空行、引号、行尾多余的 <c>\</c>，并去重。
    /// 用户在设置页里手写或粘贴的路径也可能带这些花头，统一在这里收拾。
    /// </summary>
    private static List<string> ParsePinned(string? value)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return list;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = QuickLocations.Normalize(line);
            if (normalized.Length > 0 && seen.Add(QuickLocations.DedupeKey(normalized)))
            {
                list.Add(normalized);
            }
        }

        return list;
    }

    /// <summary>
    /// 两条路径是不是同一个位置。同一个目录的写法可能差在结尾分隔符（<c>C:\</c> / <c>C:</c>）
    /// 或大小写，直接比字符串会把同一个文件夹当成两个。
    /// </summary>
    private static bool SamePath(string left, string right)
        => string.Equals(
            QuickLocations.DedupeKey(left),
            QuickLocations.DedupeKey(right),
            StringComparison.OrdinalIgnoreCase);

    private static string SettingsJsonPath()
    {
        var directory = Utilities.BaseSettingsPath("Microsoft.CmdPal.QuickJump");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "settings.json");
    }

    /// <summary>
    /// 把本管理器的设置项加进目标容器（供聚合 Provider 合并成宿主设置表单用）。
    /// 与 <c>EverythingSettings.AddTo</c> 同一目的，见那里的注释。
    /// </summary>
    internal void AddTo(Microsoft.CommandPalette.Extensions.Toolkit.Settings target)
    {
        target.Add(_autoEnter);
        target.Add(_pinned);
    }
}
