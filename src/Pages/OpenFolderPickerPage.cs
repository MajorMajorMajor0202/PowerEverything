using EverythingExtension.Internal;
using EverythingExtension.Settings;

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Collections.Generic;
using System.Linq;

using Windows.System;

namespace EverythingExtension.Pages;

/// <summary>
/// 列出当前所有已打开的资源管理器（Explorer）文件夹窗口，
/// 选择一个后即可在对应文件夹范围内进行 Everything 搜索。
///
/// 键盘操作：
///   - 输入关键字   → 在「文件夹名」和「完整路径」上做模糊筛选；
///                    文件夹名命中的结果永远排在「只有路径命中」的前面；
///   - Ctrl+R      → 重新扫描当前已打开的文件夹窗口（刷新列表）；
///   - 列表末尾的「刷新文件夹列表」一项，鼠标点选同样可以刷新。
/// </summary>
internal sealed partial class OpenFolderPickerPage : DynamicListPage
{
    #region Constants

    /// <summary>文件夹没有自定义图标时使用的内置图标。</summary>
    private const string DefaultFolderIconPath = "Assets\\Images\\folder-default.png";

    /// <summary>SEGOE Fluent 图标字体里的「刷新」字形。</summary>
    private const string RefreshGlyph = "\uE72C";

    /// <summary>「刷新文件夹列表」的命令名 / 标题。</summary>
    private const string RefreshName = "刷新文件夹列表";

    /// <summary>
    /// 刷新命令的 <c>Name</c>。宿主画在面板底部那个按钮上的文字取的是选中项<b>第一条</b>
    /// 上下文命令的 Name（而不是 Title），所以这里必须写人话，不能是内部标识符。
    ///
    /// 名字里<b>不再</b>带「（Ctrl+R）」：那个位置旁边的热键提示是宿主写死的 Ctrl+Enter，
    /// 扩展改不了，把 Ctrl+R 写在名字里只会跟它并排出现、互相打架。
    /// Ctrl+R 本身照样生效（见 <see cref="CreateRefreshCommand"/>）。
    /// </summary>
    private const string RefreshCommandName = "刷新文件夹列表";

    /// <summary>
    /// 名称命中层的基准分。取得足够大，保证「文件夹名命中」的任何结果
    /// 都排在「只有路径命中」的结果之前 —— 这是硬分层，不是加权。
    /// </summary>
    private const int NameTierBase = 1_000_000;

    #endregion Constants

    #region Fields

    private readonly EverythingSettings _settings;

    /// <summary>当前搜索框里的文字（DynamicListPage 的筛选由扩展自己实现）。</summary>
    private string _query = string.Empty;

    #endregion Fields

    #region Public Constructors

    public OpenFolderPickerPage(EverythingSettings settings)
    {
        _settings = settings;

        // Id 必须显式设置：命令面板按 CommandId 识别命令，
        // 留空会导致该命令无法正常注册 / 显示。
        Id = CommandIds.FolderPicker;
        Icon = IconHelpers.FromRelativePath("Assets\\Images\\folder-search.png");
        Title = CommandIds.FolderPickerTitle;
        Name = CommandIds.FolderPickerTitle;
        PlaceholderText = "搜索已打开的文件夹...";
    }

    #endregion Public Constructors

    #region Public Methods

    /// <summary>
    /// DynamicListPage 把「筛选」交给扩展：搜索框每次变化都会回调这里。
    /// 注意这里<b>不能</b>直接构造列表项（那会做 COM 枚举 + 图标解析），
    /// 只需要记下查询词并让宿主重新调用 <see cref="GetItems"/>。
    /// </summary>
    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var query = newSearch ?? string.Empty;
        if (string.Equals(query, _query, StringComparison.Ordinal))
            return;

        _query = query;
        RaiseItemsChanged(-1);
    }

    public override IListItem[] GetItems()
    {
        var folders = OpenFolderEnumerator.GetOpenFolders();

        if (folders.Count == 0)
        {
            EmptyContent = new CommandItem(title: "没有检测到已打开的文件夹窗口")
            {
                Icon = IconHelpers.FromRelativePath("Assets\\Images\\Warning.png"),
                Subtitle = "请先在资源管理器中打开一个文件夹窗口，然后重新进入本页。",
            };
            return [];
        }

        // 自定义图标要问 Shell（COM / WinRT），放到后台 STA 线程上预热，
        // 拿不到 / 超时就回退内置图标 —— 不阻塞命令面板的宿主线程。
        FolderIconProvider.Prefetch(folders.Select(f => f.Path));

        var query = _query.Trim();

        // 评分在 GetItems 里现算，不缓存 —— 文件夹窗口的数量是两位数级别，
        // 这点开销远小于每次枚举 Explorer 窗口本身。
        var visible = query.Length == 0
            ? folders
            : [.. ListHelpers.FilterListWithScores(folders, query, ScoreFolder).Select(s => s.Item)];

        // 注意：这里必须用 ListItem，绝对不能用 CommandItem。
        // 在 Command Palette 的 ABI 里 ICommandItem 与 IListItem 是两个【平行】接口而不是继承关系：
        //   - ListItem      -> IListItem      （列表项）
        //   - CommandItem   -> ICommandItem   （命令项）
        // 写成 (IListItem)new CommandItem(...) 能通过编译（接口之间的显式转换永远是合法的），
        // 但运行时必然抛 InvalidCastException（HRESULT 0x80004002 / E_NOINTERFACE），
        // 表现为「打开该页立刻报 An unexpected error occurred ... Specified cast is not valid」，
        // 且堆栈停在 ABI 的 IListPageMethods.GetItems 上，看不出是自己代码的问题。
        var items = new List<IListItem>(visible.Count + 1);

        foreach (var folder in visible)
        {
            // 范围页的 Id 在 EverythingExtensionPage 构造函数里按路径派生（CommandItem 无法单独设 Id，
            // ListItem 也不承担命令 Id 的职责），这样每个范围页都能被单独绑定别名 / 快捷键。
            var scopePage = new EverythingExtensionPage(_settings, folder.Path);

            items.Add(new ListItem(scopePage)
            {
                Title = folder.DisplayName,
                Subtitle = folder.Path,
                Icon = FolderIconProvider.GetCached(folder.Path)
                    ?? IconHelpers.FromRelativePath(DefaultFolderIconPath),

                // 把「刷新」挂到每个列表项的上下文菜单上：宿主在列表里收到按键时会
                // 交给当前选中项的 keybindings 处理，所以 Ctrl+R 在列表上直接生效，
                // 不必先打开上下文菜单（Tab / Ctrl+K 打开后同样能用）。
                MoreCommands = [CreateRefreshCommand()],
            });
        }

        // 「刷新」这一项只在没有搜索词时出现 —— 搜索时列表里应当只剩命中的文件夹，
        // 塞一条永远匹配不上的操作项进去只会占位置。
        if (query.Length == 0)
        {
            items.Add(new ListItem(CreateRefreshCommand())
            {
                Title = RefreshName,
                Subtitle = $"重新扫描已打开的资源管理器窗口（当前 {visible.Count} 个）",
                Icon = new IconInfo(RefreshGlyph),
            });
        }

        return [.. items];
    }

    #endregion Public Methods

    #region Private Methods

    /// <summary>
    /// 文件夹列表的排序分。两段式硬分层：
    ///   1. <b>文件夹名</b>命中 → <see cref="NameTierBase"/> + 模糊分，整层在前；
    ///   2. 只有<b>路径</b>命中 → 模糊分本身，整层在后；
    ///   3. 都不命中 → 0，会被 <see cref="ListHelpers.FilterListWithScores{T}"/> 过滤掉。
    ///
    /// 用分层而不是加权，是因为加权（上游 ListHelpers.ScoreListItem 的做法：
    /// 副标题分先减 4 再除以 2）在「名字弱匹配、路径强匹配」时仍然会把路径命中排到前面，
    /// 而用户要的是「名字对上的一定在前」。
    /// </summary>
    private static int ScoreFolder(string query, OpenFolderEnumerator.OpenFolder folder)
    {
        var nameScore = FuzzyStringMatcher.ScoreFuzzy(query, folder.DisplayName);
        if (nameScore > 0)
            return NameTierBase + nameScore;

        var pathScore = FuzzyStringMatcher.ScoreFuzzy(query, folder.Path);
        return pathScore > 0 ? pathScore : 0;
    }

    /// <summary>
    /// 造一条「刷新文件夹列表」的上下文菜单命令。每次都新建：
    /// 同一个 CommandContextItem 实例被多个列表项共用不是这层 API 承诺的用法，
    /// 而这里的条数是两位数，没必要为省这点分配去冒险。
    ///
    /// 关于 <c>name</c>：宿主画在面板底部那个按钮上的文字就是选中项<b>第一条</b>上下文命令的
    /// Name（不是 Title），并且它旁边的快捷键提示是宿主写死的 Ctrl+Enter。
    /// 所以这里把 Ctrl+R 写进名字里 —— 否则用户在这个位置只会看到一个内部标识符，
    /// 而且会误以为刷新只能按 Ctrl+Enter。
    /// </summary>
    private CommandContextItem CreateRefreshCommand()
        => new(
            title: RefreshName,
            name: RefreshCommandName,
            action: Refresh,
            result: CommandResult.KeepOpen())
        {
            Icon = new IconInfo(RefreshGlyph),

            // Ctrl+R。ShellPage 的 PreviewKeyDown 会把未被占用的按键发给
            // 当前选中项的 keybindings，所以列表上直接按即可生效。
            RequestedShortcut = KeyChordHelpers.FromModifiers(true, false, false, false, VirtualKey.R, 0),
        };

    /// <summary>
    /// 重新枚举已打开的文件夹。<see cref="OpenFolderEnumerator"/> 没有缓存，
    /// 通知宿主重取列表即可拿到最新结果。
    /// </summary>
    private void Refresh() => RaiseItemsChanged(-1);

    #endregion Private Methods
}
