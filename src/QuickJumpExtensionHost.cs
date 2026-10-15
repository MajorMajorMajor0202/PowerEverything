using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Threading.Tasks;

namespace QuickJumpCmdPal;

/// <summary>
/// 与命令面板宿主之间的通信通道（日志 / 状态提示）。
/// 宿主在另一个进程里，所以一律"发射后不管"，且必须吞掉异常 ——
/// 宿主一旦消失，扩展不该跟着崩。
/// </summary>
internal sealed partial class ExtensionHostInstance
{
    public IExtensionHost? Host { get; private set; }

    public void Initialize(IExtensionHost host) => Host = host;

    public void LogMessage(string message)
    {
        if (Host is null)
        {
            return;
        }

        var logMessage = new LogMessage() { Message = message };
        _ = Task.Run(async () =>
        {
            try
            {
                await Host.LogMessage(logMessage);
            }
            catch (Exception)
            {
            }
        });
    }

    public void ShowStatus(IStatusMessage message, StatusContext context)
    {
        var host = Host;
        if (host is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await host.ShowStatus(message, context);
            }
            catch (Exception)
            {
            }
        });
    }

    /// <summary>显示一条状态提示，几秒后自动收起。</summary>
    public void ShowStatusWithAutoHide(IStatusMessage message, StatusContext context)
    {
        var host = Host;
        if (host is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await host.ShowStatus(message, context);
                await Task.Delay(3000).ConfigureAwait(false);
                await host.HideStatus(message);
            }
            catch (Exception)
            {
            }
        });
    }
}

internal sealed partial class QuickJumpExtensionHost
{
    internal static ExtensionHostInstance Instance { get; } = new();
}
