// Copyright (c) Microsoft Corporation The Microsoft Corporation licenses this file to you under the
// MIT license. See the LICENSE file in the project root for more information.

using EverythingExtension.Commands;
using EverythingExtension.Exceptions;
using EverythingExtension.Internal;
using EverythingExtension.Properties;
using EverythingExtension.Settings;

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using Serilog;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EverythingExtension.Pages;

internal sealed partial class EverythingExtensionPage : DynamicListPage, IDisposable
{
    #region Constants

    /// <summary>
    /// 范围页（从「文件夹搜索」点进某个已打开的文件夹）的输入框说明文字。
    /// 全局页用资源文件里的插件说明，不受影响。
    /// </summary>
    private const string ScopePlaceholder = "在 文件夹路径 中使用 Everything 搜索...";

    #endregion Constants

    #region Fields

    private readonly HelpPage _helpPage;
    private readonly EverythingSettings _settings;
    private readonly string? _scopePath;
    private readonly List<IListItem> _results = [];

    private readonly CompositeFormat versionCompositeFormat = CompositeFormat.Parse(Resources.everything_version);
    private IEverythingClient? _everythingClient;
    private long _everythingSearchCookie;

    #endregion Fields

    #region Public Constructors

    public EverythingExtensionPage(EverythingSettings settings, string? scopePath = null)
    {
        _scopePath = scopePath;

        // 注意：ID 必须带上 fork 自己的命名空间前缀，且按范围派生。
        // 否则会与上游包的同名命令撞车，导致命令面板里两个插件无法区分、
        // 快捷键互相抢占，甚至新命令不显示。
        Id = CommandIds.ForScope(_scopePath);
        Icon = IconHelpers.FromRelativePath("Assets\\Images\\search.png");
        Title = Resources.everything_subtitle_header;
        Name = CommandIds.DisplayName;

        // 说明文字分两种：从「文件夹搜索」点进某个文件夹进来的是【范围页】，
        // 这里要告诉用户「你是在这个文件夹路径里搜」；从扩展入口直接进来的是全局搜索，
        // 沿用资源文件里原来的插件说明。
        PlaceholderText = string.IsNullOrEmpty(scopePath)
            ? Resources.everything_plugin_description
            : ScopePlaceholder;

        _settings = settings;
        ShowDetails = true;
        _ = EverythingInitializeAsync();
        _helpPage = new HelpPage();
        _helpPage.GoBackHomePage += GoBackHomePageHandler;
        WelcomeEmptyContentInitialize();
    }

    ~EverythingExtensionPage()
    {
        Dispose(false);
    }

    #endregion Public Constructors

    #region Properties

    private int DelayTime { get; set; }

    #endregion Properties

    #region Public Methods

    public override IListItem[] GetItems() => [.. _results];

    public async Task DelayQuery()
    {
        while (DelayTime > 0 && DelayTime-- > 0)
        {
            await Task.Delay(100);
        }
        Query(SearchText);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        if (oldSearch != newSearch)
        {
            _ = Task.Run(() =>
            {
                IsLoading = true;
                try
                {
                    //await DelayQuery();
                    Query(newSearch);
                    FetchItems(30);
                    RaiseItemsChanged(_results.Count);
                }
                catch (Exception ex)
                {
                    // Query() 内部只包住了搜索本身，初始化阶段抛出的异常（例如原生
                    // Everything64.dll 缺失时的 DllNotFoundException）会流到这里。
                    // 必须兜住，否则 IsLoading 永远停在 true —— 表现就是面板一直转圈、
                    // 搜不出任何内容，而且看不到任何错误提示。
                    Log.Error(ex, "搜索失败");
                    _everythingClient?.Cancel();
                    EmptyContent = new CommandContextItem(title: Resources.everything_query_error, subtitle: ex.Message)
                    {
                        Icon = IconHelpers.FromRelativePath("Assets\\Images\\Error.png"),
                        Command = new CopyTextCommand(ex + "\r\n" + ex.StackTrace),
                    };
                    RaiseItemsChanged();
                }
                finally
                {
                    IsLoading = false;
                }
            });
        }
    }

    public override void LoadMore()
    {
        IsLoading = true;
        try
        {
            FetchItems(30);
            RaiseItemsChanged(_results.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载更多失败");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal bool EverythingInitialize()
    {
        if (_everythingClient != null && _everythingClient.Version != Version.Parse("0.0.0.0"))
            return true;

        _everythingClient = EverythingClientProvider.GetClient(_settings);
        if (!string.IsNullOrEmpty(_scopePath))
            _everythingClient.Scope = _scopePath;

        if (_everythingClient.Initialize())
        {
            EmptyContent = null;
            WelcomeEmptyContentInitialize();
            var baseTitle = Resources.everything_subtitle_header + string.Format(CultureInfo.InvariantCulture, versionCompositeFormat, _everythingClient.Version);
            Title = string.IsNullOrEmpty(_scopePath) ? baseTitle : baseTitle + " — " + _scopePath;
            return true;
        }
        else
        {
            NotRunningEmptyContentInitialize();
            return false;
        }
    }

    private async Task EverythingInitializeAsync()
    {
        using CancellationTokenSource _initCts = new();
        _initCts.CancelAfter(TimeSpan.FromSeconds(60));
        CancellationToken token = _initCts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (EverythingInitialize())
                    return;

                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        catch (TaskCanceledException)
        {
            Log.Warning("��ʼ����������ֹ��30�볬ʱ��");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "��ʼ�������쳣");
        }
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _everythingClient?.Dispose();
            _everythingClient = null;
        }
    }

    private void Query(string query)
    {
        if (_everythingClient == null)
        {
            EverythingInitialize();
        }
        _everythingSearchCookie = DateTime.Now.ToFileTime();
        _results.Clear();
        _everythingClient?.SearchResults.Clear();
        _everythingClient?.Cancel();

        //WelcomeEmptyContentInitialize();

        if (string.IsNullOrEmpty(query))
        {
            WelcomeEmptyContentInitialize();
            return;
        }
        else
            EmptyContent = null;

        if (_settings.MaxSearchCount <= 0)
            _settings.MaxSearchCount = EverythingSettings.DefaultMaxSearchCount;

        try
        {
            _everythingClient?.EnsureEverythingAvailable();
            _everythingClient?.Execute(query, _everythingSearchCookie);
            //_everythingSearch?.Execute(query, _everythingSearchCookie);
        }
        catch (IpcErrorException)
        {
            _everythingClient?.Cancel();
            NotRunningEmptyContentInitialize();
        }
        catch (OperationCanceledException ex)
        {
            Log.Error(ex, "��������ȡ��");
        }
        catch (Exception e)
        {
            _everythingClient?.Cancel();
            EmptyContent = new CommandContextItem(title: Resources.everything_query_error, subtitle: e.Message)
            {
                Icon = IconHelpers.FromRelativePath("Assets\\Images\\Error.png"),
                Command = new CopyTextCommand(e.Message + "\r\n" + e.StackTrace)
            };
        }
    }

    private void WelcomeEmptyContentInitialize()
    {
        EmptyContent = new CommandContextItem(_helpPage)
        {
            Title = Resources.everything_welcome,
            Subtitle = Resources.everything_welcome_subtitle,
        };
    }

    private void NotRunningEmptyContentInitialize()
    {
        EmptyContent = new CommandContextItem(title: Resources.everything_is_not_running)
        {
            Icon = IconHelpers.FromRelativePath("Assets\\Images\\Warning.png"),
            Command = new ReconnectEverythingCommand(this)
        };
    }

    private void FetchItems(int limit)
    {
        if (_everythingClient == null)
            return;

        var cookie = _everythingClient.Cookie;
        if (cookie != _everythingSearchCookie)
            return;

        var index = 0;
        while (!_everythingClient.SearchResults.IsEmpty && _everythingClient.SearchResults.TryDequeue(out var result) && ++index <= limit)
        {
            //IconInfo icon = null;
            //try
            //{
            //    var stream = ThumbnailHelper.GetThumbnail(result.FullPath).Result;
            //    if (stream != null)
            //    {
            //        var data = new IconData(RandomAccessStreamReference.CreateFromStream(stream));
            //        icon = new IconInfo(data, data);
            //    }
            //}
            //catch (Exception ex)
            //{
            //    Debug.Write(ex);
            //    //Logger.LogError("Failed to get the icon.", ex);
            //}
            var item = new EverythingListItem(result, _everythingClient);

            //_item.Icon = icon;

            result.Deleted = () => DeletedListItemHandler(item);

            _results.Add(item);
        }

        HasMoreItems = !_everythingClient.SearchResults.IsEmpty;
    }

    private void DeletedListItemHandler(EverythingListItem item)
    {
        _results.Remove(item);
        RaiseItemsChanged(_results.Count);
    }

    private void GoBackHomePageHandler(object? sender, EventArgs e)
    {
        _results.Clear();
        _everythingClient?.SearchResults.Clear();
        _everythingClient?.Cancel();
        RaiseItemsChanged();
    }

    #endregion Public Methods
}