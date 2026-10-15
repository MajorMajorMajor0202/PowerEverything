using EverythingExtension.Internal;
using EverythingExtension.Properties;
using EverythingExtension.SDK;

using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace EverythingExtension.Commands
{
    internal sealed partial class OpenInConsoleCommand : InvokableCommand
    {
        #region Public Constructors

        public OpenInConsoleCommand(SearchResult searchResult)
        {
            _searchResult = searchResult;

            Icon = _theIcon;
            Name = Resources.everything_open_in_console;
        }

        #endregion Public Constructors

        #region Fields

        private static readonly IconInfo _theIcon = new("\ue756");
        private readonly SearchResult _searchResult;

        #endregion Fields

        #region Public Methods

        public override CommandResult Invoke()
        {
            string? path = _searchResult.GetDirectoryPath();

            if (!string.IsNullOrEmpty(path))
            {
                _ = LaunchTarget(path).ConfigureAwait(false);

                _ = EverythingSdk.Everything_IncRunCountFromFileNameW(_searchResult.FullPath);
            }

            return CommandResult.Dismiss();
        }

        #endregion Public Methods

        #region Internal Methods

        private static async Task LaunchTarget(string t)
        {
            await Task.Run(() =>
            {
                // 优先用 Windows Terminal（wt.exe）：它会按用户「默认终端 / 默认配置文件」
                // 启动，而不是落后的 cmd.exe。系统没装 WT 时再回退到 cmd。
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