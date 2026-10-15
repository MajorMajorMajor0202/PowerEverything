using System.Globalization;

namespace EverythingExtension.Internal;

/// <summary>
/// 本 fork 使用独立的命令 ID 命名空间，避免与上游
/// <c>Goucey.EverythingExtensionforCommandPalette</c> 撞车。
///
/// 命令面板（Command Palette）的别名 alias 与快捷键 hotkey 都是<b>按 CommandId 绑定</b>的，
/// 两个包若共用同一个 CommandId，会出现：
///   1. 命令列表里出现两个完全同名、同图标的条目，分不清谁是谁；
///   2. 快捷键 / 别名互相抢占，按下去永远落到先注册的那个；
///   3. 新注册的命令（尤其是没设置 Id 的）无法正常显示。
/// 因此 fork 内所有命令 ID 都必须走这里，保持前缀唯一。
/// </summary>
internal static class CommandIds
{
    /// <summary>本 fork 的 ID 命名空间前缀。</summary>
    public const string Prefix = "goucey.cmdPal.folderScope";

    /// <summary>搜索页。不带范围时即全局搜索；带范围时为「文件夹范围内搜索」。</summary>
    public const string SearchPage = Prefix + ".everything";

    /// <summary>全局搜索的 fallback 项。</summary>
    public const string Fallback = Prefix + ".fallback";

    /// <summary>「在打开的文件夹中搜索」的文件夹选择页。</summary>
    public const string FolderPicker = Prefix + ".folderPicker";

    /// <summary>扩展在命令面板中的显示名（与上游的「Everything 搜索」区分开）。</summary>
    public const string DisplayName = "Everything 文件夹搜索";

    /// <summary>fork 自己的全局搜索入口标题。</summary>
    public const string GlobalSearchTitle = "Everything 全局搜索";

    /// <summary>新功能入口标题。</summary>
    public const string FolderPickerTitle = "在打开的文件夹中搜索";

    /// <summary>
    /// 为某个搜索范围生成稳定的页面 ID：同一路径始终得到同一个 ID，
    /// 这样「按范围搜索」也能各自被赋予别名 / 快捷键。
    /// </summary>
    public static string ForScope(string? scopePath)
    {
        if (string.IsNullOrWhiteSpace(scopePath))
            return SearchPage;

        // FNV-1a 32bit。仅用于生成稳定、无歧义的后缀，不用于任何安全用途。
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in scopePath.Trim().ToLowerInvariant())
            {
                hash ^= c;
                hash *= 16777619u;
            }

            return SearchPage + "." + hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
