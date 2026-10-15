// Copyright (c) Microsoft Corporation The Microsoft Corporation licenses this file to you under the
// MIT license. See the LICENSE file in the project root for more information.

using EverythingExtension.Internal;
using EverythingExtension.Pages;
using EverythingExtension.Properties;
using EverythingExtension.Settings;

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using System;

namespace EverythingExtension;

public sealed partial class EverythingExtensionCommandsProvider : CommandProvider
{
    #region Fields

    private readonly FallbackEverythingItem _fallback;
    private readonly EverythingExtensionPage _page;

    #endregion Fields

    #region Public Constructors

    public EverythingExtensionCommandsProvider()
    {
        // 显示名必须与上游包（"Everything 搜索"）区分开，否则命令面板里
        // 两个扩展看起来一模一样，无法判断点的是哪一个。
        DisplayName = CommandIds.DisplayName;
        Icon = IconHelpers.FromRelativePath("Assets\\Everything.png");
        _page = new EverythingExtensionPage(EverythingSettings.Instance);
        _fallback = new FallbackEverythingItem(_page);
        var openFolderPicker = new OpenFolderPickerPage(EverythingSettings.Instance);
        _commands = [
            // 本 fork 的主打功能放在最前，避免它被同名条目淹没。
            new CommandItem(openFolderPicker) {
                Title = CommandIds.FolderPickerTitle,
                Subtitle = "列出当前已打开的资源管理器文件夹窗口，选中后只在该文件夹范围内搜索",
                Icon = IconHelpers.FromRelativePath("Assets\\Images\\folder-search.png"),
            },
            new CommandItem(_page) {
                Title = CommandIds.GlobalSearchTitle,
                Subtitle = Resources.everything_plugin_description,
                Icon = IconHelpers.FromRelativePath("Assets\\Images\\folder-search.png"),
                MoreCommands = [new CommandContextItem(GetSettingsPage())],
            },
        ];
        _bands = [
            new WrappedDockItem(_page,_page.Name)
            ];
    }

  
    private ICommand GetSettingsPage()
    {
        Settings = EverythingSettings.Instance.Settings;

        if (Settings.SettingsPage is Command settingPage)
        {
            settingPage.Name = Resources.everything_plugin_settings;
            return settingPage;
        }
        else
            return Settings.SettingsPage;
    }

    #endregion Public Constructors



    #region Fields

    private readonly ICommandItem[] _commands;
    readonly ICommandItem[] _bands ;
    #endregion Fields

    #region Public Methods

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[]? FallbackCommands() => [_fallback];

    public override ICommandItem[]? GetDockBands() => _bands;


    public override void InitializeWithHost(IExtensionHost host)
    {
        EverythingExtensionHost.Instance.Initialize(host);
    }

    #endregion Public Methods
}