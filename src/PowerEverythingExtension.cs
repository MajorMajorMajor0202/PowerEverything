using Microsoft.CommandPalette.Extensions;

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PowerEverything;

/// <summary>
/// 单包单入口：整个合并扩展只注册这【一个】COM 类 / AppExtension。
/// 主页上的多个入口由 PowerEverythingProvider 聚合 QuickJump 与 Everything 的顶层命令得到。
/// </summary>
[Guid("a1b2c3d4-1234-5678-9abc-def012345678")]
public sealed partial class PowerEverythingExtension(ManualResetEvent extensionDisposedEvent) : IExtension, IDisposable
{
    private readonly PowerEverythingProvider _provider = new();

    public object? GetProvider(ProviderType providerType)
    {
        return providerType switch
        {
            ProviderType.Commands => _provider,
            _ => null,
        };
    }

    public void Dispose() => extensionDisposedEvent.Set();
}
