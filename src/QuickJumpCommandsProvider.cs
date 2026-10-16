using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using EverythingExtension.Settings;

using QuickJumpCmdPal.Commands;
using QuickJumpCmdPal.Internal;
using QuickJumpCmdPal.Pages;
using QuickJumpCmdPal.Settings;

using System;

namespace QuickJumpCmdPal;

public sealed partial class QuickJumpCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public QuickJumpCommandsProvider()
    {
        DisplayName = CommandIds.DisplayName;
        Icon = IconHelpers.FromRelativePath("Assets\\Images\\jump-96.png");

        var page = new QuickJumpPage(QuickJumpSettings.Instance);

        // 交给宿主：这样「设置 → 扩展」里会出现本扩展的设置表单（目前只有「自动回车」一项）。
        // 「触发文本 / 全局快捷键」宿主自带，扩展侧不提供。
        Settings = QuickJumpSettings.Instance.Settings;

        _commands =
        [
            new CommandItem(page)
            {
                Title = CommandIds.JumpTitle,
                Subtitle = CommandIds.JumpSubtitle,
                Icon = IconHelpers.FromRelativePath("Assets\\Images\\jump-96.png"),
                MoreCommands =
                [
                    new CommandContextItem(GetSettingsPage()),
                    new CommandContextItem(GetSearchSettingsPage()),
                    new CommandContextItem(new ForgetTargetCommand(page)),
                    new CommandContextItem(new DumpWindowsCommand()),
                ],
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override void InitializeWithHost(IExtensionHost host)
    {
        QuickJumpExtensionHost.Instance.Initialize(host);
        Log.Info("已连接到命令面板宿主。");
    }

    /// <summary>设置页。</summary>
    private static ICommand GetSettingsPage()
    {
        var settingsPage = QuickJumpSettings.Instance.Settings.SettingsPage;
        if (settingsPage is Command command)
        {
            command.Name = CommandIds.SettingsTitle;
            return command;
        }

        return (ICommand)settingsPage;
    }

    /// <summary>
    /// Everything 搜索 / 预览的设置页（与全局搜索入口共用同一份设置：
    /// 搜索前缀、正则、排序、发送到指定程序、预览快捷键等）。
    /// </summary>
    private static ICommand GetSearchSettingsPage()
    {
        var settingsPage = EverythingSettings.Instance.Settings.SettingsPage;
        if (settingsPage is Command command)
        {
            command.Name = "搜索与预览设置";
            return command;
        }

        return (ICommand)settingsPage;
    }
}
