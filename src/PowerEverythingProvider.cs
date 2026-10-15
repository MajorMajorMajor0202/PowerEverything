using EverythingExtension;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickJumpCmdPal;

using System;

namespace PowerEverything;

/// <summary>
/// 聚合 Provider：把 QuickJump 与 Everything 两个原有 Provider 的顶层命令合并到同一个
/// 命令面板扩展下。这样主页会同时出现「QuickJump 跳转」+「Everything 文件夹搜索」+
/// 「Everything 全局搜索」等多个入口 —— 即用户要的「一个插件、多个入口」形态。
/// </summary>
public sealed partial class PowerEverythingProvider : CommandProvider
{
    private readonly QuickJumpCommandsProvider _quickJump = new();
    private readonly EverythingExtensionCommandsProvider _everything = new();
    private readonly ICommandItem[] _commands;

    public PowerEverythingProvider()
    {
        DisplayName = "PowerEverything";
        Icon = IconHelpers.FromRelativePath("Assets\\Images\\search.png");

        var quickJumpCommands = _quickJump.TopLevelCommands();
        var everythingCommands = _everything.TopLevelCommands();
        _commands = [.. quickJumpCommands, .. everythingCommands];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[]? FallbackCommands() => _everything.FallbackCommands();

    public override ICommandItem[]? GetDockBands() => _everything.GetDockBands();

    public override void InitializeWithHost(IExtensionHost host)
    {
        _quickJump.InitializeWithHost(host);
        _everything.InitializeWithHost(host);
    }
}
