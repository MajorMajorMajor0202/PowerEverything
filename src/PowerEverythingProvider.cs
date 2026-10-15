using EverythingExtension;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickJumpCmdPal;

using Serilog;

using System;

namespace PowerEverything;

/// <summary>
/// 聚合 Provider：把 QuickJump 与 Everything 两个原有 Provider 的顶层命令合并到同一个
/// 命令面板扩展下。这样主页会同时出现「QuickJump 跳转」+「Everything 文件夹搜索」+
/// 「Everything 全局搜索」等多个入口 —— 即用户要的「一个插件、多个入口」形态。
///
/// 容错：任一个子 Provider 在构造 / 初始化时抛异常，只丢弃它自己的入口，
/// 不会让整个扩展激活失败（否则 CmdPal 里整条扩展都会消失，难以排查）。
/// </summary>
public sealed partial class PowerEverythingProvider : CommandProvider
{
    private readonly QuickJumpCommandsProvider? _quickJump;
    private readonly EverythingExtensionCommandsProvider? _everything;
    private readonly ICommandItem[] _commands;

    public PowerEverythingProvider()
    {
        DisplayName = "PowerEverything";
        // 整个插件沿用 Everything 原版图标
        Icon = IconHelpers.FromRelativePath("Assets\\Everything.png");

        _quickJump = TryCreate(() => new QuickJumpCommandsProvider(), "QuickJump");
        _everything = TryCreate(() => new EverythingExtensionCommandsProvider(), "Everything");

        var commands = new System.Collections.Generic.List<ICommandItem>();
        if (_quickJump is not null) commands.AddRange(_quickJump.TopLevelCommands() ?? []);
        if (_everything is not null) commands.AddRange(_everything.TopLevelCommands() ?? []);
        _commands = [.. commands];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override IFallbackCommandItem[]? FallbackCommands() => _everything?.FallbackCommands();

    public override ICommandItem[]? GetDockBands() => _everything?.GetDockBands();

    public override void InitializeWithHost(IExtensionHost host)
    {
        TryRun(() => _quickJump?.InitializeWithHost(host), "QuickJump.InitializeWithHost");
        TryRun(() => _everything?.InitializeWithHost(host), "Everything.InitializeWithHost");
    }

    private static T? TryCreate<T>(Func<T> factory, string name)
    {
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PowerEverything] 子 Provider {Name} 构造失败，已跳过其入口。", name);
            return default;
        }
    }

    private static void TryRun(Action action, string name)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PowerEverything] {Name} 执行失败。", name);
        }
    }
}
