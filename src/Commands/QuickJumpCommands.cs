using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using QuickJumpCmdPal.Internal;
using QuickJumpCmdPal.Pages;

using System;

namespace QuickJumpCmdPal.Commands;

/// <summary>
/// 点某个文件夹 → 把它的路径送进文件窗口。
/// 有多个文件窗口而且还没定过目标时，会先切到「选目标窗口」形态，由用户挑一个。
/// </summary>
internal sealed partial class JumpToFolderCommand : InvokableCommand
{
    private readonly QuickJumpPage _page;
    private readonly string _path;

    internal JumpToFolderCommand(QuickJumpPage page, string path)
    {
        _page = page;
        _path = path;
        Name = "跳转到这个文件夹";
        Icon = new IconInfo("\uE72A"); // 向右的箭头
    }

    public override CommandResult Invoke() => _page.ChooseFolder(_path);
}

/// <summary>在「选目标窗口」形态里挑定了窗口。</summary>
internal sealed partial class SelectTargetCommand : InvokableCommand
{
    private readonly QuickJumpPage _page;
    private readonly FileDialogWindow _dialog;

    internal SelectTargetCommand(QuickJumpPage page, FileDialogWindow dialog)
    {
        _page = page;
        _dialog = dialog;
        Name = "把路径送到这个窗口";
    }

    public override CommandResult Invoke() => _page.SelectTarget(_dialog);
}

/// <summary>放弃「选目标窗口」，回到文件夹列表。</summary>
internal sealed partial class CancelTargetPickCommand : InvokableCommand
{
    private readonly QuickJumpPage _page;

    internal CancelTargetPickCommand(QuickJumpPage page)
    {
        _page = page;
        Name = "返回文件夹列表";
    }

    public override CommandResult Invoke() => _page.CancelTargetPick();
}

/// <summary>忘掉已经定下来的目标窗口，下次选文件夹时重新确定。</summary>
internal sealed partial class ForgetTargetCommand : InvokableCommand
{
    private readonly QuickJumpPage _page;

    internal ForgetTargetCommand(QuickJumpPage page)
    {
        _page = page;
        Name = "重新选择目标窗口";
    }

    public override CommandResult Invoke() => _page.ForgetTarget();
}

/// <summary>重新检测文件窗口并重扫文件夹列表。</summary>
internal sealed partial class RefreshListCommand : InvokableCommand
{
    private readonly QuickJumpPage _page;

    internal RefreshListCommand(QuickJumpPage page)
    {
        _page = page;
        Name = "刷新列表";
    }

    public override CommandResult Invoke() => _page.Refresh();
}

/// <summary>
/// 把当前所有顶层窗口的结构（类名分布、每个 #32770 的标题/进程/文件名框位置）
/// 写进日志。检测不到文件窗口时，这份快照是唯一能看出「对方到底是什么窗口」的东西。
/// </summary>
internal sealed partial class DumpWindowsCommand : InvokableCommand
{
    internal DumpWindowsCommand()
    {
        Name = "诊断：把当前窗口结构写入日志";
        Icon = new IconInfo("\uE9D9"); // Info 字形
    }

    public override CommandResult Invoke()
    {
        try
        {
            Log.Info("窗口结构诊断（由用户触发）：\n" + FileDialogLocator.Census());
            return CommandResult.ShowToast("窗口结构已写入日志（TempState\\Logs）");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "窗口结构诊断失败");
            return CommandResult.ShowToast("诊断失败：" + ex.Message);
        }
    }
}
