using System;

namespace QuickJumpCmdPal.Internal;

/// <summary>
/// 日志门面。日志由 <c>Program.Main</c> 里的 Serilog 写到
/// <c>%LOCALAPPDATA%\Packages\&lt;包族名&gt;\TempState\Logs\</c>。
///
/// 这个扩展做的事（找窗口、跨进程写文本、模拟回车）全都依赖宿主的实现细节，
/// 出问题时几乎只能靠日志定位，所以关键步骤都要落日志。
/// </summary>
internal static class Log
{
    internal static void Info(string message) => Serilog.Log.Information(message);

    internal static void Warn(string message) => Serilog.Log.Warning(message);

    internal static void Error(Exception exception, string message) => Serilog.Log.Error(exception, message);
}
