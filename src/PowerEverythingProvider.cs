using EverythingExtension;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using QuickJumpCmdPal;

using Serilog;

using System;
using System.Collections.Generic;

namespace PowerEverything;

/// <summary>
/// 聚合 Provider：把 QuickJump 与 Everything 两个原有 Provider 的顶层命令合并到同一个
/// 命令面板扩展下。这样主页会同时出现「QuickJump 跳转」+「Everything 文件夹搜索」+
/// 「Everything 全局搜索」等多个入口 —— 即用户要的「一个插件、多个入口」形态。
///
/// 容错与可观测：任一个子 Provider 在构造 / 初始化时抛异常，只丢它自己的入口，并把异常
/// 暴露为一个可见的「⚠ 加载失败」诊断项（而不是整条扩展静默消失，难以排查）。
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

        var qjEx = TryCreate(() => new QuickJumpCommandsProvider(), out _quickJump);
        var evEx = TryCreate(() => new EverythingExtensionCommandsProvider(), out _everything);

        var commands = new List<ICommandItem>();
        if (_quickJump is not null) commands.AddRange(_quickJump.TopLevelCommands() ?? []);
        if (_everything is not null) commands.AddRange(_everything.TopLevelCommands() ?? []);

        // 诊断入口：任一个子 Provider 构造失败，显示一个可见错误项，便于直接定位，
        // 而不是让那一半入口静默消失、只剩另一半（之前就是这种情况）。
        if (qjEx is not null) commands.Add(MakeDiagnostic("QuickJump", qjEx));
        if (evEx is not null) commands.Add(MakeDiagnostic("Everything", evEx));

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

    private static Exception? TryCreate<T>(Func<T> factory, out T? instance) where T : class
    {
        try
        {
            instance = factory();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PowerEverything] 子 Provider 构造失败: {Type}", typeof(T).Name);
            instance = null;
            return ex;
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

    private static CommandItem MakeDiagnostic(string name, Exception ex)
    {
        var inner = ex is TypeInitializationException { InnerException: not null } tie ? tie.InnerException : ex;
        var msg = inner?.Message ?? ex.Message;
        return new CommandItem(new Command())
        {
            Title = $"⚠ {name} 加载失败",
            Subtitle = msg.Length > 220 ? msg[..220] : msg,
            Icon = IconHelpers.FromRelativePath("Assets\\Images\\Error.png"),
        };
    }
}
