using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using QuickJumpCmdPal.Commands;
using QuickJumpCmdPal.Internal;
using QuickJumpCmdPal.Settings;

using EverythingExtension;
using EverythingExtension.SDK;
using EverythingExtension.Settings;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Windows.Storage.Streams;
using Windows.System;

namespace QuickJumpCmdPal.Pages;

/// <summary>
/// 「快速跳转」主页面 —— 整个扩展只有这一页，它按当前情况切换两种形态：
///
///   A. 文件夹列表（常态）
///      输入框搜索文件夹，Enter 直接把路径送进文件窗口。
///      目标窗口是谁，写在搜索框的说明文字里，不占列表行。
///
///   B. 目标窗口选择（少数情况）
///      只有在「同时开着多个文件窗口」而且用户还没指定过目标时才会出现，
///      而且是在**选完文件夹之后**才出现 —— 先选文件夹，再多选一就选窗口。
///      一旦选过，本次会话里就记住它，之后的文件夹直接跳，不再多问。
///
/// 列表内容，三层，顺序就是列表顺序：
    ///   1. 此刻真的开着窗口的资源管理器文件夹；
    ///   2. 用户固定到首页的文件夹（已经开着的以第 1 层的身份出现，不重复）。
    /// 输入关键字还会用 Everything 搜「任意文件夹 / 文件」并补到列表后面，
    /// 其中正开着窗口的路径会被提到搜索结果最前。
    /// 固定的行不做任何视觉标记，只是排得更靠前。
///
/// 键盘操作：
///   - 输入关键字 → 按名称/路径模糊筛选（文件夹名命中的整层排在只有路径命中的前面）；
///     输入的内容如果本身就是一个存在的目录路径，会直接给出「跳转到这里」一项；
///   - Enter      → 跳转到选中项；
///   - Ctrl+R     → 重新检测文件窗口 + 重扫文件夹列表；
///   - Ctrl+K     → 选中项的命令：刷新、固定到首页 / 从首页移除、重新选择目标窗口。
/// </summary>
internal sealed partial class QuickJumpPage : DynamicListPage
{
    #region Constants

    /// <summary>扩展自己的图标（入口 / 空状态用）。</summary>
    private const string EntryIconPath = "Assets\\Images\\jump-96.png";

    /// <summary>「跳转到的文件夹」用的图标。</summary>
    private const string FolderIconPath = "Assets\\Images\\folder-48.png";

    /// <summary>「文件窗口」用的图标。</summary>
    private const string WindowIconPath = "Assets\\Images\\window-48.png";

    /// <summary>「文件」用的图标（Everything 搜索结果里的文件项）。</summary>
    private const string FileIconPath = "Assets\\Images\\file.png";

    /// <summary>Segoe Fluent 字体里的「刷新」字形。</summary>
    private const string RefreshGlyph = "\uE72C";

    /// <summary>Segoe Fluent 字体里的「返回」字形。</summary>
    private const string BackGlyph = "\uE72B";

    /// <summary>Segoe Fluent 字体里的「图钉」字形（固定）。</summary>
    private const string PinGlyph = "\uE718";

    /// <summary>Segoe Fluent 字体里的「拔掉图钉」字形（取消固定）。</summary>
    private const string UnpinGlyph = "\uE77A";

    /// <summary>刷新那一行在列表里的标题。</summary>
    private const string RefreshName = "刷新列表";

    /// <summary>
    /// 刷新命令的名字。命令名才是宿主画在面板底部那个按钮上的文字
    /// （底部按钮显示的是选中项第一条上下文命令的 Name，不是 Title），
    /// 所以这里必须写人话 —— 否则用户在那个位置看到的只有无意义的内部标识符。
    ///
    /// 名字里<b>不再</b>带「（Ctrl+R）」：那个位置旁边的热键提示是宿主写死的 Ctrl+Enter，
    /// 扩展改不了，把 Ctrl+R 写在名字里只会跟它并排出现、互相打架。
    /// Ctrl+R 本身照样生效（见 <see cref="CreateRefreshContextCommand"/>）。
    /// </summary>
    private const string RefreshCommandName = "刷新列表";

    /// <summary>刷新到底刷新了什么，写在副标题里。</summary>
    private const string RefreshSubtitle = "重新检测「打开 / 另存为」窗口和已打开的文件夹";

    private const string PinName = "固定到首页";

    private const string UnpinName = "从首页移除";

    /// <summary>输入框在选择目标窗口形态下的说明文字。</summary>
    private const string SearchWindowHint = "搜索窗口...";

    /// <summary>输入框里「目标窗口」前缀。没有确定目标时只显示前面的搜索提示。</summary>
    private const string SearchPathHint = "搜索路径...";

    /// <summary>窗口名太长时截断到这个长度，免得把搜索框的说明文字挤没。</summary>
    private const int WindowLabelMaxLength = 32;

    /// <summary>
    /// 名称命中层的基准分。取得足够大，保证「名字命中」的任何结果都排在
    /// 「只有路径命中」的结果之前 —— 这是硬分层，不是加权。
    /// </summary>
    private const int NameTierBase = 1_000_000;

    #endregion Constants

    #region Fields

    /// <summary>候选列表（固定 + 打开的文件夹 + 常用位置）的缓存时长。</summary>
    private static readonly TimeSpan CandidateCacheLifetime = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// 命令返回后命令面板才开始收起，等一小会儿再往文件窗口里塞按键，
    /// 否则回车会落到正在消失的面板上。
    /// </summary>
    private static readonly TimeSpan PaletteHideDelay = TimeSpan.FromMilliseconds(350);

    private readonly QuickJumpSettings _settings;

    private readonly object _candidateGate = new();

    private string _query = string.Empty;

    /// <summary>已经定下来的目标文件窗口（<see cref="IntPtr.Zero"/> = 还没定）。</summary>
    private IntPtr _targetDialog = IntPtr.Zero;

    private string _targetDialogName = string.Empty;

    /// <summary>
    /// 非空 = 正在「选目标窗口」形态，内容是等在外面的那个文件夹路径。
    /// 只有当同时开着多个文件窗口、用户还没定过目标时才会被设置。
    /// </summary>
    private string _pendingPath = string.Empty;

        private bool _forceRefresh;

        private List<FolderCandidate> _cachedCandidates = [];

        private DateTime _cachedCandidatesStamp = DateTime.MinValue;

        /// <summary>懒初始化的 Everything 客户端（仅用于「搜索任意文件夹 / 文件」）。</summary>
        private IEverythingClient? _everythingClient;

        /// <summary>是否已经尝试过初始化 Everything 客户端（只试一次，失败就禁用搜索）。</summary>
        private bool _everythingTriedInit;

        /// <summary>Everything 搜索结果与查询的缓存。</summary>
        private readonly object _searchGate = new();

        private string _searchCacheQuery = string.Empty;
        private List<IListItem> _searchCache = [];
        private DateTime _searchCacheStamp = DateTime.MinValue;

        /// <summary>Everything 查询最短长度。用户要求输入一个字符就触发搜索。</summary>
        private const int MinSearchLength = 1;

        /// <summary>Everything 搜索每次最多返回的条目数（文件夹 + 文件合计）。</summary>
        private const int MaxSearchResults = 40;

        /// <summary>Everything 搜索结果缓存时长。</summary>
        private static readonly TimeSpan SearchCacheLifetime = TimeSpan.FromMilliseconds(500);

    #endregion Fields

    #region Public Constructors

    internal QuickJumpPage(QuickJumpSettings settings)
    {
        _settings = settings;

        // Id 必须显式设置：宿主按 CommandId 识别命令，别名/快捷键也绑在它上面。
        Id = CommandIds.JumpPage;
        Icon = IconHelpers.FromRelativePath(EntryIconPath);
        Title = CommandIds.JumpTitle;
        Name = CommandIds.JumpTitle;

        // 具体的目标窗口要等列表刷新时才知道，所以这里只给最朴素的说明；
        // 每次 GetItems 都会把它改写成带目标窗口信息的版本。
        PlaceholderText = SearchPathHint;

        EmptyContent = new CommandItem()
        {
            Title = "没有匹配的文件夹 / 文件",
            Icon = IconHelpers.FromRelativePath(EntryIconPath),
        };
    }

    #endregion Public Constructors

    #region Public Methods

    /// <summary>
    /// DynamicListPage 把筛选交给扩展：搜索框每次变化都会回调这里。
    /// 这里只记下查询词并让宿主重取列表，不做任何耗时操作。
    /// </summary>
    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        var query = newSearch ?? string.Empty;
        if (string.Equals(query, _query, StringComparison.Ordinal))
        {
            return;
        }

        _query = query;
        RaiseItemsChanged(-1);
    }

    public override IListItem[] GetItems()
    {
        var dialogs = FileDialogLocator.FindAll(_forceRefresh);
        _forceRefresh = false;

        // 之前定下的窗口已经关了 → 回到「还没定」。
        if (_targetDialog != IntPtr.Zero && !dialogs.Any(d => d.Hwnd == _targetDialog))
        {
            Log.Info($"选定的文件窗口「{_targetDialogName}」已关闭，需要重新确定目标。");
            _targetDialog = IntPtr.Zero;
            _targetDialogName = string.Empty;
        }

        if (_pendingPath.Length > 0)
        {
            if (dialogs.Count > 0)
            {
                return BuildWindowPicker(dialogs);
            }

            // 窗口全没了，退回文件夹列表（点文件夹时会提示先开窗口）。
            Log.Info("选窗口期间文件窗口都关了，退回文件夹列表。");
            _pendingPath = string.Empty;
        }

        return BuildFolderList(dialogs);
    }

    /// <summary>
    /// 用户选中了一个文件夹。绝大多数情况在这里就完成了跳转；
    /// 只有在「多个文件窗口 + 还没定过目标」时，才会切到「选目标窗口」形态，
    /// 等用户挑完窗口再跳（<see cref="SelectTarget"/>）。
    /// </summary>
    internal CommandResult ChooseFolder(string path)
    {
        var dialogs = FileDialogLocator.FindAll(force: true);

        if (dialogs.Count == 0)
        {
            Log.Warn($"想跳转到「{path}」，但没有检测到文件选择窗口。");
            return CommandResult.ShowToast("没有检测到「打开 / 另存为」窗口，先把文件窗口打开再试");
        }

        if (_targetDialog != IntPtr.Zero)
        {
            var latched = dialogs.FirstOrDefault(d => d.Hwnd == _targetDialog);
            if (latched is not null)
            {
                return JumpTo(path, latched);
            }

            _targetDialog = IntPtr.Zero;
            _targetDialogName = string.Empty;
        }

        if (dialogs.Count == 1)
        {
            return JumpTo(path, dialogs[0]);
        }

        // 多个文件窗口：先记住文件夹，让用户在列表里挑一个窗口收下它。
        Log.Info($"有 {dialogs.Count} 个文件窗口，先选文件夹再选窗口：「{path}」。");
        _pendingPath = path;
        RaiseItemsChanged(-1);
        return CommandResult.KeepOpen();
    }

    /// <summary>在「选目标窗口」形态里挑定了窗口 → 立刻把之前选中的文件夹送过去。</summary>
    internal CommandResult SelectTarget(FileDialogWindow dialog)
    {
        _targetDialog = dialog.Hwnd;
        _targetDialogName = WindowLabel(dialog);

        var path = _pendingPath;
        _pendingPath = string.Empty;

        Log.Info($"目标文件窗口：{_targetDialogName}（hwnd=0x{dialog.Hwnd.ToInt64():X}）");

        if (path.Length == 0)
        {
            RaiseItemsChanged(-1);
            return CommandResult.KeepOpen();
        }

        return JumpTo(path, dialog);
    }

    /// <summary>放弃「选目标窗口」，回到文件夹列表。</summary>
    internal CommandResult CancelTargetPick()
    {
        _pendingPath = string.Empty;
        RaiseItemsChanged(-1);
        return CommandResult.KeepOpen();
    }

    /// <summary>忘掉已定的目标窗口，下次选文件夹时重新确定。</summary>
    internal CommandResult ForgetTarget()
    {
        if (_targetDialog == IntPtr.Zero)
        {
            return CommandResult.KeepOpen();
        }

        Log.Info($"用户手动清除了目标文件窗口「{_targetDialogName}」。");
        _targetDialog = IntPtr.Zero;
        _targetDialogName = string.Empty;
        RaiseItemsChanged(-1);
        return CommandResult.KeepOpen();
    }

    /// <summary>重新检测文件窗口、重扫文件夹列表。</summary>
    internal CommandResult Refresh()
    {
        _forceRefresh = true;
        RefreshCandidateCache();
        RaiseItemsChanged(-1);
        return CommandResult.KeepOpen();
    }

    /// <summary>
    /// 把一个文件夹固定到首页。固定记录写在设置文件里，退出命令面板也不会丢，
    /// 之后它就和「桌面 / 下载 / 文档」一样常驻在列表里 ——
    /// 位置在「已打开的文件夹」下面、「默认位置」上面。
    /// </summary>
    internal CommandResult Pin(string path)
    {
        _settings.Pin(path);
        RefreshCandidateCache();
        RaiseItemsChanged(-1);
        return CommandResult.KeepOpen();
    }

    /// <summary>把固定记录删掉（只删记录，磁盘上的文件夹不受影响）。</summary>
    internal CommandResult Unpin(string path)
    {
        _settings.Unpin(path);
        RefreshCandidateCache();
        RaiseItemsChanged(-1);
        return CommandResult.KeepOpen();
    }

    #endregion Public Methods

    #region Private Methods

    /// <summary>形态 A：文件夹列表。</summary>
    private IListItem[] BuildFolderList(IReadOnlyList<FileDialogWindow> dialogs)
    {
        UpdatePlaceholder(dialogs);

        var query = _query.Trim();
        var items = new List<IListItem>();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 直接把一个存在的目录路径粘进输入框 → 第一项就是它。
        var direct = ResolveDirectPath(query);
        if (direct is not null)
        {
            items.Add(CreateFolderItem(direct));
            addedPaths.Add(QuickLocations.DedupeKey(direct.Path));
        }

        var candidates = GetCandidates();
        var visible = query.Length == 0
            ? candidates
            : [.. ListHelpers.FilterList(candidates, query, ScoreCandidate)];

        foreach (var candidate in visible)
        {
            if (direct is not null && AreSameFolder(candidate.Path, direct.Path))
            {
                continue;
            }

            items.Add(CreateFolderItem(candidate));
            addedPaths.Add(QuickLocations.DedupeKey(candidate.Path));
        }

        // 直接在输入框搜索：把 Everything 索引里的文件夹 / 文件也拉进来。
        if (query.Length >= MinSearchLength)
        {
            foreach (var item in GetSearchItems(query))
            {
                var path = item.Command switch
                {
                    JumpToFolderCommand folder => folder.Path,
                    JumpToFileCommand file => file.Path,
                    _ => string.Empty,
                };

                if (path.Length > 0 && !addedPaths.Add(QuickLocations.DedupeKey(path)))
                {
                    continue;
                }

                items.Add(item);
            }
        }

        items.Add(CreateRefreshItem());
        return [.. items];
    }

    /// <summary>形态 B：选目标窗口。</summary>
    private IListItem[] BuildWindowPicker(IReadOnlyList<FileDialogWindow> dialogs)
    {
        UpdatePlaceholder(dialogs);

        var query = _query.Trim();
        var items = new List<IListItem>
        {
            new ListItem(new CancelTargetPickCommand(this))
            {
                Title = "← 返回文件夹列表",
                Icon = new IconInfo(BackGlyph),
            },
        };

        // 搜索框里可能还留着刚才筛文件夹用的关键字（宿主那边不会自己清空），
        // 那个词多半匹配不到任何窗口 —— 这时就把窗口全部列出来，别把用户卡在空列表里。
        var visible = dialogs;
        if (query.Length > 0)
        {
            var filtered = ListHelpers.FilterList(dialogs, query, ScoreDialog).ToList();
            if (filtered.Count > 0)
            {
                visible = filtered;
            }
        }

        var topMost = dialogs[0].Hwnd;
        foreach (var dialog in visible)
        {
            items.Add(CreateDialogItem(dialog, isTopMost: dialog.Hwnd == topMost));
        }

        items.Add(CreateRefreshItem());
        return [.. items];
    }

    /// <summary>
    /// 把「目标窗口是谁」写进搜索框的说明文字 —— 这信息以前占一整行列表，
    /// 现在放到输入框里，列表干净了、也不会随搜索消失。
    ///
    /// 文案很短，就三段可能：
    ///   - 选目标窗口形态          → 「搜索窗口...」（这时列表里全是窗口，搜的自然是窗口）；
    ///   - 已经确定 / 只有一个窗口 → 「搜索路径... 目标窗口：窗口名」；
    ///   - 其余（没窗口、或好几个窗口还没挑）→ 「搜索路径...」。
    /// </summary>
    private void UpdatePlaceholder(IReadOnlyList<FileDialogWindow> dialogs)
    {
        string text;
        if (_pendingPath.Length > 0)
        {
            text = SearchWindowHint;
        }
        else
        {
            var target = _targetDialogName.Length > 0
                ? _targetDialogName
                : dialogs.Count == 1 ? WindowLabel(dialogs[0]) : string.Empty;

            text = target.Length > 0 ? $"{SearchPathHint} 目标窗口：{target}" : SearchPathHint;
        }

        PlaceholderText = text;
    }

    #region Everything 搜索

    /// <summary>
    /// 懒初始化 Everything 客户端（只试一次）。失败就禁用「搜索任意文件夹 / 文件」，
    /// 退回只显示已打开 / 已固定的文件夹，不影响基本跳转。
    /// </summary>
    private bool EnsureEverythingClient()
    {
        if (_everythingTriedInit)
        {
            return _everythingClient is not null;
        }

        _everythingTriedInit = true;
        try
        {
            _everythingClient = EverythingClientProvider.GetClient(EverythingSettings.Instance);
            if (_everythingClient.Initialize())
            {
                Log.Info("Everything 客户端已初始化，搜索功能可用。");
            }
            else
            {
                Log.Warn("Everything 未运行，搜索功能已禁用（基本跳转不受影响）。");
                _everythingClient = null;
            }
        }
            catch (Exception ex)
            {
                Log.Warn($"初始化 Everything 客户端失败，搜索功能已禁用：{ex.Message}");
                _everythingClient = null;
            }

        return _everythingClient is not null;
    }

    /// <summary>
    /// 用 Everything 按关键字搜文件夹和文件，返回要插进列表的项。
    /// 结果里凡是「正打开着窗口的路径」（资源管理器已打开的文件夹、或文件所在的文件夹），
    /// 都会被提到本段结果的最前面 —— 你多半就是想跳到当前正在看的那儿。
    /// 结果按 query 缓存一小段时间，避免每次按键都重新查询。
    /// </summary>
    private List<IListItem> GetSearchItems(string query)
    {
        lock (_searchGate)
        {
            if (DateTime.UtcNow - _searchCacheStamp < SearchCacheLifetime &&
                string.Equals(query, _searchCacheQuery, StringComparison.Ordinal))
            {
                return _searchCache;
            }
        }

        var items = new List<IListItem>();

        if (EnsureEverythingClient() && _everythingClient is not null)
        {
            try
            {
                _everythingClient.Cancel();
                _everythingClient.SearchResults.Clear();
                _everythingClient.Execute(query, DateTime.UtcNow.ToFileTimeUtc());

                var folders = new List<EverythingExtension.Internal.SearchResult>();
                var files = new List<EverythingExtension.Internal.SearchResult>();
                while (folders.Count + files.Count < MaxSearchResults &&
                       _everythingClient.SearchResults.TryDequeue(out var result))
                {
                    if (result.Type == ResultType.Folder)
                    {
                        folders.Add(result);
                    }
                    else if (result.Type == ResultType.File)
                    {
                        files.Add(result);
                    }
                }

                // 当前正打开着窗口的文件夹路径，用于把命中项提到最前。
                var openPaths = OpenFolderEnumerator.GetOpenFolders()
                    .Select(f => QuickLocations.DedupeKey(f.Path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                bool IsOpen(EverythingExtension.Internal.SearchResult r) =>
                    r.Type == ResultType.Folder
                        ? openPaths.Contains(QuickLocations.DedupeKey(r.FullPath))
                        : r.ParentPath is not null && openPaths.Contains(QuickLocations.DedupeKey(r.ParentPath));

                var boostedFolders = folders.Where(IsOpen).ToList();
                var boostedFiles = files.Where(IsOpen).ToList();

                foreach (var r in boostedFolders) items.Add(CreateSearchFolderItem(r));
                foreach (var r in boostedFiles) items.Add(CreateSearchFileItem(r));
                foreach (var r in folders.Except(boostedFolders)) items.Add(CreateSearchFolderItem(r));
                foreach (var r in files.Except(boostedFiles)) items.Add(CreateSearchFileItem(r));
            }
            catch (Exception ex)
            {
                Log.Warn($"Everything 搜索失败，仅显示已打开 / 已固定的文件夹：{ex.Message}");
            }
        }

        lock (_searchGate)
        {
            _searchCacheQuery = query;
            _searchCache = items;
            _searchCacheStamp = DateTime.UtcNow;
        }

        return items;
    }

    private ListItem CreateSearchFolderItem(EverythingExtension.Internal.SearchResult result)
    {
        var candidate = new FolderCandidate(
            result.FullPath,
            string.IsNullOrEmpty(result.FileName) ? result.FullPath : result.FileName,
            false,
            _settings.IsPinned(result.FullPath));

        // 搜索出来的文件夹走「普通文件夹图标」，再异步补上缩略图（和普通搜索一致的观感）。
        var item = CreateFolderItem(candidate);
        AttachThumbnail(item, result.FullPath);
        return item;
    }

        private ListItem CreateSearchFileItem(EverythingExtension.Internal.SearchResult result)
        {
            var parent = result.ParentPath ?? Path.GetDirectoryName(result.FullPath) ?? result.FullPath;

            // 文件项先给一个回退图标，再异步换成真实缩略图（和普通搜索一样的缩略图观感）。
            var item = new ListItem(new JumpToFileCommand(this, result.FullPath))
            {
                Title = string.IsNullOrEmpty(result.FileName) ? result.FullPath : result.FileName,
                Subtitle = parent,
                Icon = IconHelpers.FromRelativePath(FileIconPath),
                MoreCommands =
                [
                    // 复制路径：绑 Ctrl+Shift+C（和 Windows 资源管理器「复制为路径」默认快捷键一致）。
                    new CommandContextItem(new EverythingExtension.Commands.CopyPathCommand(result))
                    {
                        RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
                    },
                    // QuickLook 预览（装了就生效，没装则静默无操作）。
                    new CommandContextItem(new EverythingExtension.Commands.PreviewWithQuickLookCommand(result.FullPath)),
                ],
            };
            AttachThumbnail(item, result.FullPath);
            return item;
        }

    /// <summary>
    /// 异步拿文件 / 文件夹的真实缩略图（与普通搜索 <c>EverythingListItem</c> 同一套
    /// <see cref="ThumbnailHelper"/>），拿到就覆盖到列表项的图标上；拿不到就保留回退图标。
    /// 在后台线程跑，不阻塞列表构建。
    /// </summary>
    private static void AttachThumbnail(ListItem item, string path)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var stream = ThumbnailHelper.GetThumbnail(path).GetAwaiter().GetResult();
                if (stream is null)
                {
                    return;
                }

                var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream));
                item.Icon = new IconInfo(data, data);
            }
            catch
            {
                // 缩略图取不到就忽略，保留回退图标。
            }
        });
    }

    #endregion Everything 搜索

    /// <summary>
    /// 候选文件夹，分两层，顺序就是列表顺序：
    ///   1. <b>此刻真的开着资源管理器窗口的文件夹</b> —— 最贴近用户当前的注意力；
    ///   2. <b>用户固定到首页的文件夹</b>（没开窗口的那些）—— 常去的地方，提上来但排在 1 之后。
    /// 同一层里保持各自原有的顺序；路径重复的只留第一次出现的那一层。
    ///
    /// 枚举 Explorer 窗口要起 STA 线程走 Shell COM，所以带一个很短的缓存：
    /// 同一个字输入过程中宿主要连着取好几次列表，不做缓存会明显卡顿。
    /// </summary>
    private List<FolderCandidate> GetCandidates()
    {
        lock (_candidateGate)
        {
            if (DateTime.UtcNow - _cachedCandidatesStamp < CandidateCacheLifetime)
            {
                return _cachedCandidates;
            }
        }

        var list = new List<FolderCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) 当前打开的资源管理器文件夹。
        foreach (var folder in OpenFolderEnumerator.GetOpenFolders())
        {
            if (seen.Add(DedupeKey(folder.Path)))
            {
                // 已经固定过的照样带上标记：它在第一层是因为「开着窗口」，不是因为它没被固定，
                // 那行按 Ctrl+K 仍然应该能取消固定。
                list.Add(new FolderCandidate(folder.Path, folder.DisplayName, true, _settings.IsPinned(folder.Path)));
            }
        }

        // 2) 用户固定到首页的文件夹（上一轮已经出现过的会被 seen 挡掉）。
        foreach (var pinned in _settings.PinnedFolders)
        {
            var candidate = FolderCandidate.FromPinned(pinned);
            if (candidate.Path.Length > 0 && seen.Add(DedupeKey(candidate.Path)))
            {
                list.Add(candidate);
            }
        }

        // 注：原先内置的「桌面 / 下载 / 文档 / 各驱动器」默认位置已移除 ——
        // 让用户自己用 Ctrl+K 固定，常去的地方更贴合个人习惯。

        lock (_candidateGate)
        {
            _cachedCandidates = list;
            _cachedCandidatesStamp = DateTime.UtcNow;
        }

        return list;
    }

    /// <summary>同一个位置的不同写法（结尾分隔符、大小写）不该在列表里各占一行。</summary>
    private static string DedupeKey(string path) => QuickLocations.DedupeKey(path);

    private static bool AreSameFolder(string left, string right)
        => string.Equals(DedupeKey(left), DedupeKey(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 让候选列表立刻重算。固定 / 取消固定只动了设置，Explorer 窗口那边其实没变化，
    /// 但候选列表带着 600ms 缓存，不戳一下的话列表会看起来「没反应」。
    /// </summary>
    private void RefreshCandidateCache()
    {
        lock (_candidateGate)
        {
            _cachedCandidatesStamp = DateTime.MinValue;
        }
    }

    /// <summary>
    /// 排序分。两段式硬分层：
    ///   1. 文件夹名命中 → <see cref="NameTierBase"/> + 模糊分，整层在前；
    ///   2. 只有路径命中 → 模糊分本身，整层在后；
    ///   3. 都不命中 → 0，会被 <see cref="ListHelpers.FilterList{T}"/> 过滤掉。
    /// </summary>
    private static int ScoreCandidate(string query, FolderCandidate candidate)
    {
        var nameScore = FuzzyStringMatcher.ScoreFuzzy(query, candidate.DisplayName);
        if (nameScore > 0)
        {
            return NameTierBase + nameScore;
        }

        var pathScore = FuzzyStringMatcher.ScoreFuzzy(query, candidate.Path);
        return pathScore > 0 ? pathScore : 0;
    }

    private static int ScoreDialog(string query, FileDialogWindow dialog)
    {
        var score = FuzzyStringMatcher.ScoreFuzzy(query, dialog.ProcessName);
        var byTitle = FuzzyStringMatcher.ScoreFuzzy(query, dialog.Title);
        return Math.Max(score, byTitle);
    }

    /// <summary>
    /// 输入框里如果直接是一个存在的目录路径（含粘贴整条路径的情况），
    /// 就把它当成一个候选，让用户不用先去找窗口开在哪儿。
    /// </summary>
    private FolderCandidate? ResolveDirectPath(string query)
    {
        if (query.Length < 2)
        {
            return null;
        }

        var input = query.Trim().Trim('"');
        if (!input.Contains('\\') && !input.Contains('/'))
        {
            return null;
        }

        try
        {
            // 只要绝对路径：相对路径会相对扩展进程的当前目录解析，得出的东西不是用户想要的。
            if (!Path.IsPathRooted(input) || !Directory.Exists(input))
            {
                return null;
            }

            var full = Path.GetFullPath(input).TrimEnd('\\', '/');
            if (full.Length == 2 && full[1] == ':')
            {
                full += "\\"; // 驱动器根目录
            }

            var name = Path.GetFileName(full);

            // 顺手标出「已经在首页」：这样粘贴一条固定过的路径时，标记和「从首页移除」都在。
            return new FolderCandidate(full, name.Length > 0 ? name : full, false, _settings.IsPinned(full));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private ListItem CreateFolderItem(FolderCandidate candidate)
    {
        return new ListItem(new JumpToFolderCommand(this, candidate.Path))
        {
            Title = candidate.DisplayName,

            // 固定的文件夹不在副标题上做任何标记：它要和「桌面 / 下载」长得一模一样，
            // 只是排在默认位置之前而已。想取消就去 Ctrl+K 里看「从首页移除」。
            Subtitle = candidate.Path,
            // 文件夹行一律用 QuickJump 原来的那个文件夹图标（folder-48），
            // 不再用入口那个 jump 箭头 —— 箭头只留在命令面板的入口上。
            Icon = IconHelpers.FromRelativePath(FolderIconPath),
            MoreCommands = FolderContextCommands(candidate),
        };
    }

    private ListItem CreateDialogItem(FileDialogWindow dialog, bool isTopMost)
    {
        var title = string.IsNullOrWhiteSpace(dialog.Title) ? "文件窗口" : dialog.Title;

        return new ListItem(new SelectTargetCommand(this, dialog))
        {
            Title = isTopMost ? $"{dialog.ProcessName} · {title}（最前）" : $"{dialog.ProcessName} · {title}",
            Icon = IconHelpers.FromRelativePath(WindowIconPath),
            MoreCommands = [CreateRefreshContextCommand()],
        };
    }

    private ListItem CreateRefreshItem()
    {
        return new ListItem(new RefreshListCommand(this))
        {
            Title = RefreshName,
            Subtitle = RefreshSubtitle,
            Icon = new IconInfo(RefreshGlyph),
            MoreCommands = [CreateRefreshContextCommand()],
        };
    }

    /// <summary>
    /// 文件夹行在 Ctrl+K 里的附加命令：刷新、固定 / 取消固定；
    /// 如果已经定过目标窗口，再加一条「重新选择目标窗口」。
    ///
    /// 顺序有意义：宿主把选中项的<b>第一条</b>上下文命令画在面板底部（就是那个带 Ctrl+Enter 的按钮），
    /// 所以刷新留在第一条，固定放它后面。
    /// </summary>
    private CommandContextItem[] FolderContextCommands(FolderCandidate candidate)
    {
        var commands = new List<CommandContextItem> { CreateRefreshContextCommand() };

        // 内置的默认位置（桌面 / 下载 / 文档 / …）本来就一直在列表里，
        // 没有「取消固定」这回事，所以不给它们挂固定 / 取消固定。
        if (!candidate.IsDefault)
        {
            commands.Add(candidate.IsPinned ? CreateUnpinCommand(candidate) : CreatePinCommand(candidate));
        }

        if (_targetDialog != IntPtr.Zero)
        {
            commands.Add(new CommandContextItem(new ForgetTargetCommand(this))
            {
                Icon = IconHelpers.FromRelativePath(WindowIconPath),
            });
        }

        return [.. commands];
    }

    /// <summary>
    /// 造一条「刷新」的上下文菜单命令，顺带把 Ctrl+R 绑上去（宿主会把列表里的按键
    /// 交给当前选中项的 keybindings，所以列表上直接按就生效，不必先打开 Ctrl+K）。
    /// 每次都新建：同一个实例被多个列表项共用不是这层 API 承诺的用法。
    /// </summary>
    private CommandContextItem CreateRefreshContextCommand()
        => new(
            title: RefreshName,
            subtitle: RefreshSubtitle,
            name: RefreshCommandName,
            action: () => Refresh(),
            result: CommandResult.KeepOpen())
        {
            Icon = new IconInfo(RefreshGlyph),
            RequestedShortcut = KeyChordHelpers.FromModifiers(true, false, false, false, VirtualKey.R, 0),
        };

    /// <summary>「固定到首页」：把这个文件夹变成和「桌面 / 下载」一样的常驻候选。</summary>
    private CommandContextItem CreatePinCommand(FolderCandidate candidate)
        => new(
            title: PinName,
            subtitle: candidate.Path,
            name: PinName,
            action: () => Pin(candidate.Path),
            result: CommandResult.KeepOpen())
        {
            Icon = new IconInfo(PinGlyph),
        };

    /// <summary>「从首页移除」：只把固定记录删掉，磁盘上的文件夹不动。</summary>
    private CommandContextItem CreateUnpinCommand(FolderCandidate candidate)
        => new(
            title: UnpinName,
            subtitle: candidate.Path,
            name: UnpinName,
            action: () => Unpin(candidate.Path),
            result: CommandResult.KeepOpen())
        {
            Icon = new IconInfo(UnpinGlyph),
        };

    /// <summary>把路径送进指定的文件窗口。</summary>
    private CommandResult JumpTo(string path, FileDialogWindow dialog)
    {
        var autoEnter = _settings.AutoEnter;
        Log.Info($"跳转「{path}」→ {Describe(dialog)}（自动回车={autoEnter}）");

        _ = Task.Run(() =>
        {
            try
            {
                Thread.Sleep(PaletteHideDelay);
                var ok = FileDialogInjector.TryInjectPath(dialog, path, autoEnter, out var detail);
                if (ok)
                {
                    Log.Info($"跳转成功：{detail}");
                }
                else
                {
                    Log.Warn($"跳转失败：{detail}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "跳转过程中出现异常");
            }
        });

        return CommandResult.Dismiss();
    }

    private static string Describe(FileDialogWindow dialog)
    {
        var title = string.IsNullOrWhiteSpace(dialog.Title) ? "文件窗口" : dialog.Title;
        return $"{title}（{dialog.ProcessName}）";
    }

    /// <summary>
    /// 给用户看的窗口名：优先用窗口标题（就是用户在标题栏上看到的那个名字），
    /// 没有标题就退回进程名；标题特别长的话截断，免得把搜索框的说明文字挤没了。
    /// </summary>
    private static string WindowLabel(FileDialogWindow dialog)
    {
        var title = dialog.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            return dialog.ProcessName;
        }

        return title.Length <= WindowLabelMaxLength ? title : title[..(WindowLabelMaxLength - 1)] + "…";
    }

    #endregion Private Methods
}
