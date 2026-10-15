using EverythingExtension.Properties;

using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace EverythingExtension.Commands
{
    /// <summary>
    /// 针对「只有路径、没有 <see cref="SDK.SearchResult"/>」的场景（例如全局搜索主页列出的
    /// 已打开文件夹），在右键菜单里提供「在终端中打开路径」。
    ///
    /// 优先启动 Windows Terminal（<c>wt.exe -d</c>，按用户默认终端 / 默认配置文件启动），
    /// 没装 WT 时回退到 cmd.exe。与 <see cref="OpenInConsoleCommand"/> 的区别：那个接收完整
    /// <see cref="SDK.SearchResult"/>（还会给 Everything 增加运行计数），这里只接一个文件夹路径。
    /// </summary>
    internal sealed partial class OpenInConsoleForPathCommand : InvokableCommand
    {
        #region Fields

        private static readonly IconInfo _theIcon = new("\ue756");
        private readonly string _path;

        #endregion Fields

        #region Public Constructors

        public OpenInConsoleForPathCommand(string path)
        {
            _path = path;
            Icon = _theIcon;
            Name = Resources.everything_open_in_console;
        }

        #endregion Public Constructors

        #region Public Methods

        public override CommandResult Invoke()
        {
            if (!string.IsNullOrEmpty(_path))
            {
                _ = LaunchTarget(_path).ConfigureAwait(false);
            }

            return CommandResult.Dismiss();
        }

        #endregion Public Methods

        #region Internal Methods

        private static async Task LaunchTarget(string t)
        {
            await Task.Run(() =>
            {
                // 优先用 Windows Terminal：遵循用户的默认终端 / 默认配置文件，而不是落后的 cmd.exe。
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "wt.exe",
                        Arguments = $"-d \"{t}\"",
                        UseShellExecute = true,
                    });
                    return;
                }
                catch
                {
                    // WT 不可用，回退到 cmd
                }

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        WorkingDirectory = t,
                        UseShellExecute = true,
                    });
                }
                catch
                {
                    // 都打不开就静默失败
                }
            });
        }

        #endregion Internal Methods
    }
}
