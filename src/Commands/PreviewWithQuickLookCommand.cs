using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace EverythingExtension.Commands
{
    /// <summary>
    /// 用 QuickLook 预览文件。
    ///
    /// 两条通路，按优先级：
    ///   1. 命名管道 <c>\\.\pipe\QuickLook.App.Pipe</c>：QuickLook 后台常驻进程监听这个管道，
    ///      写入一行 UTF-8 文本（文件路径，以 \r\n 结尾）即可让它显示对应文件的预览。
    ///      走管道**不新起进程**，预览窗口是否抢焦点完全由 QuickLook 自己决定
    ///      （QuickLook 用全局低级键盘钩子处理 Space/Esc/方向键，正常情况下预览窗口
    ///      不抢占焦点，命令面板有机会保持不消失）。
    ///   2. 直接启动 QuickLook.exe 并带上文件路径（QuickLook 没在运行时）。
    ///      注意：QuickLook 的命令行是「再调一次即关闭」的开关式语义，且新进程会抢焦点，
    ///      命令面板通常会随之消失 —— 所以这只是兜底，正常路径是管道。
    /// </summary>
    internal sealed partial class PreviewWithQuickLookCommand : InvokableCommand
    {
        #region Public Constructors

        public PreviewWithQuickLookCommand(string path)
        {
            _path = path;
            Name = "QuickLook 预览";
            Icon = _theIcon;
        }

        #endregion Public Constructors

        #region Fields

        private static readonly IconInfo _theIcon = new("\uE7B3");
        private readonly string _path;

        #endregion Fields

        #region Public Methods

        public override CommandResult Invoke()
        {
            QuickLookLauncher.Preview(_path);
            return CommandResult.Dismiss();
        }

        #endregion Public Methods
    }

    /// <summary>QuickLook 的外部调用入口（命名管道优先，启动 exe 兜底）。</summary>
    internal static class QuickLookLauncher
    {
        #region Public Methods

        public static bool Preview(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return TryPipe(path) || TryLaunchExe(path);
        }

        #endregion Public Methods

        #region Private Methods

        private static bool TryPipe(string path)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(500);
                if (!pipe.IsConnected)
                {
                    return false;
                }

                var payload = Encoding.UTF8.GetBytes(path + "\r\n");
                pipe.Write(payload, 0, payload.Length);
                pipe.Flush();
                pipe.WaitForPipeDrain();
                return true;
            }
            catch
            {
                // QuickLook 没在运行 / 管道不可用，走启动 exe 的兜底。
                return false;
            }
        }

        private static bool TryLaunchExe(string path)
        {
            foreach (var exe in CandidateExePaths)
            {
                try
                {
                    if (!File.Exists(exe))
                    {
                        continue;
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true,
                    });
                    return true;
                }
                catch
                {
                    // 换个候选路径再试。
                }
            }

            return false;
        }

        #endregion Private Methods

        #region Fields

        private const string PipeName = "QuickLook.App.Pipe";

        /// <summary>QuickLook 的常见安装位置（GitHub release 解压版 / 安装版 / scoop）。</summary>
        private static readonly string[] CandidateExePaths =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "QuickLook", "QuickLook.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "QuickLook", "QuickLook.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "QuickLook", "QuickLook.exe"),
        ];

        #endregion Fields
    }
}
