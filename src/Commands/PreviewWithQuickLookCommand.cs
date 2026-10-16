using QuickJumpCmdPal.Internal;

using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EverythingExtension.Commands
{
    /// <summary>
    /// 预览文件 / 文件夹（通过 QuickLook 显示）。
    ///
    /// 两条通路，按优先级：
    ///   1. 命名管道 <c>QuickLook.App.Pipe.&lt;用户 SID&gt;</c>：QuickLook 常驻托盘时监听这个管道，
    ///      写入一行 <c>QuickLook.App.PipeMessages.Toggle|&lt;路径&gt;|</c> 即可切换预览。
    ///      走管道<b>不新起进程</b>。
    ///   2. 启动 QuickLook：管道不在说明它没在运行。GitHub 安装版直接跑 exe；
    ///      微软商店版是 AppX（exe 在 WindowsApps 里不能直接启动），从注册表拿到包全名后
    ///      用 <c>shell:AppsFolder&lt;包全名&gt;!App</c> 唤起，再等管道就绪补发切换消息。
    ///
    /// QuickLook 的预览窗口按设计不抢焦点，所以经常被当前前台窗口（命令面板自己）盖住。
    /// 发完消息后会把预览窗口在 topmost 层里置顶（<c>SWP_NOACTIVATE</c>，不抢焦点），
    /// 命令面板保持打开、预览也露得出来。
    /// </summary>
    internal sealed partial class PreviewWithQuickLookCommand : InvokableCommand
    {
        #region Public Constructors

        public PreviewWithQuickLookCommand(string path)
        {
            _path = path;
            Name = "预览";
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
            // 正常路径（QuickLook 在运行）：写完管道就返回，不收起命令面板。
            if (QuickLookLauncher.TryPipe(_path))
            {
                // 预览窗口不抢焦点，会被命令面板盖住；后台把它置顶到面板前面。
                _ = Task.Run(() => QuickLookLauncher.BringToFront());
                return CommandResult.KeepOpen();
            }

            // 兜底：QuickLook 没在运行。后台拉起来再补发，不卡界面；
            // 成败都不打扰用户（失败只记日志）。
            _ = Task.Run(() =>
            {
                if (!QuickLookLauncher.StartAndPreview(_path))
                {
                    Log.Warn($"QuickLook 预览失败：{_path}（QuickLook 未安装或启动失败）");
                }
            });

            return CommandResult.KeepOpen();
        }

        #endregion Public Methods
    }

    /// <summary>QuickLook 的外部调用入口（命名管道优先，启动兜底）。</summary>
    internal static class QuickLookLauncher
    {
        #region Public Methods

        /// <summary>给运行中的 QuickLook 发一条「切换预览到 path」的管道消息。</summary>
        public static bool TryPipe(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(1000);
                if (!pipe.IsConnected)
                {
                    return false;
                }

                // 消息格式见 QuickLook 的 PipeServerManager："{消息}|{路径}|{选项}"，一行一条。
                using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine($"{ToggleMessage}|{path}|");
                writer.Flush();
                return true;
            }
            catch
            {
                // QuickLook 没在运行 / 管道不可用。
                return false;
            }
        }

        /// <summary>先把 QuickLook 拉起来，再等管道就绪后补发切换消息。</summary>
        public static bool StartAndPreview(string path)
        {
            if (string.IsNullOrEmpty(path) || !TryStart())
            {
                return false;
            }

            // 冷启动 QuickLook 到管道就绪有几秒延迟，最多等 ~4 秒。
            for (var i = 0; i < 20; i++)
            {
                Thread.Sleep(200);
                if (TryPipe(path))
                {
                    // 这条路径本来就在后台线程里，直接同步把预览窗口置顶。
                    BringToFront();
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 把 QuickLook 的预览窗口置顶到命令面板前面。
        /// 预览窗口是收到管道消息后才出现的，所以轮询等一小会儿（最多 ~2 秒）。
        /// 用 <c>SWP_NOACTIVATE</c>：只改 z 序、不激活，命令面板仍是前台窗口、不会收起。
        /// </summary>
        public static void BringToFront()
        {
            for (var i = 0; i < 20; i++)
            {
                var hwnd = FindPreviewWindow();
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(
                        hwnd,
                        NativeMethods.HWND_TOPMOST,
                        0, 0, 0, 0,
                        NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
                    return;
                }

                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// 找 QuickLook 的预览窗口：可见的顶层窗口、非工具窗口、属于 QuickLook 进程。
        /// （预览窗口是 WPF 窗口，类名形如 <c>HwndWrapper[QuickLook.exe;;...]</c>，
        /// 按进程名认最稳——GitHub 安装版和微软商店版的进程都叫 QuickLook。）
        /// </summary>
        private static IntPtr FindPreviewWindow()
        {
            var found = IntPtr.Zero;

            NativeMethods.EnumWindows((hwnd, _l) =>
            {
                if (!NativeMethods.IsWindowVisible(hwnd))
                {
                    return true;
                }

                // 跳过工具窗口（托盘弹出、隐藏辅助窗口之类）。
                if ((NativeMethods.GetWindowLongW(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                {
                    return true;
                }

                _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
                if (pid == 0)
                {
                    return true;
                }

                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    if (process.ProcessName.Contains("QuickLook", StringComparison.OrdinalIgnoreCase))
                    {
                        found = hwnd;
                        return false;
                    }
                }
                catch
                {
                    // 进程可能刚退出，跳过这个窗口。
                }

                return true;
            }, IntPtr.Zero);

            return found;
        }

        #endregion Public Methods

        #region Private Methods

        private static bool TryStart()
        {
            // 1) GitHub release 安装版 / 解压版的常见位置。
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
                        UseShellExecute = true,
                    });
                    return true;
                }
                catch
                {
                    // 换个候选路径再试。
                }
            }

            // 2) 微软商店版：AppX 包的 exe 位于 WindowsApps，普通进程无法直接启动，
            //    从注册表找到包全名后用 shell:AppsFolder 唤起（AppId 固定为 App）。
            var packageFamilyName = FindStorePackageFamilyName();
            if (packageFamilyName is not null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"shell:AppsFolder{packageFamilyName}!App",
                        UseShellExecute = true,
                    });
                    return true;
                }
                catch
                {
                    // 唤起失败就静默返回 false。
                }
            }

            return false;
        }

        /// <summary>
        /// 在当前用户的已安装 AppX 包里找 QuickLook（微软商店版），返回包全名。
        /// 注册表里每个包一个子键，键名就是 PackageFullName。
        /// </summary>
        private static string? FindStorePackageFamilyName()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
                if (key is null)
                {
                    return null;
                }

                foreach (var name in key.GetSubKeyNames())
                {
                    if (name.Contains("QuickLook", StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
            catch
            {
                // 注册表读不到就算了。
            }

            return null;
        }

        #endregion Private Methods

        #region Fields

        /// <summary>QuickLook 监听的命名管道名（带当前用户 SID，见 QuickLook 的 PipeServerManager）。</summary>
        private static readonly string PipeName = "QuickLook.App.Pipe." + WindowsIdentity.GetCurrent().User?.Value;

        /// <summary>切换预览的消息（常量全名，见 QuickLook 的 PipeMessages.Toggle）。</summary>
        private const string ToggleMessage = "QuickLook.App.PipeMessages.Toggle";

        /// <summary>QuickLook 的常见安装位置（GitHub release 解压版 / 安装版）。</summary>
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
