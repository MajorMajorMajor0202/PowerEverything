using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Diagnostics;
using System.IO;

namespace EverythingExtension.Commands
{
    /// <summary>
    /// 把当前查询词原样交给 Everything 主程序，在它的窗口里看完整结果
    /// （ECP 的 "Show more" 同款功能；原生实现，未复制 ECP 代码）。
    /// </summary>
    internal sealed partial class ShowInEverythingCommand : InvokableCommand
    {
        #region Public Constructors

        public ShowInEverythingCommand(string query)
        {
            _query = query;
            Name = "在 Everything 中显示更多结果";
            Icon = _theIcon;
        }

        #endregion Public Constructors

        #region Fields

        private static readonly IconInfo _theIcon = new("\uE721");
        private readonly string _query;

        #endregion Fields

        #region Public Methods

        public override CommandResult Invoke()
        {
            var exe = FindEverythingExe();
            if (exe is null)
            {
                return CommandResult.ShowToast("没找到 Everything.exe（Everything 没在运行？）");
            }

            try
            {
                // Everything.exe -s "查询词"：用 Everything 自己的窗口做一次同样的搜索。
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-s \"{_query}\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
                return CommandResult.ShowToast("唤起 Everything 失败");
            }

            return CommandResult.Dismiss();
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>找 Everything 主程序的 exe：先在运行中的进程里找，再翻几个常见安装位置。</summary>
        private static string? FindEverythingExe()
        {
            try
            {
                foreach (var process in Process.GetProcessesByName("Everything"))
                {
                    try
                    {
                        var path = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path))
                        {
                            return path;
                        }
                    }
                    catch
                    {
                        // 进程可能刚退出 / 拒绝访问，换下一个。
                    }
                }
            }
            catch
            {
                // 枚举失败就翻常见路径。
            }

            string[] candidates =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "Everything.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", "Everything.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Everything", "Everything.exe"),
            ];

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        #endregion Private Methods
    }
}
