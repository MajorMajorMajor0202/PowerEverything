using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Diagnostics;

namespace EverythingExtension.Commands
{
    /// <summary>
    /// 弹出 Windows 的「打开方式」对话框，让用户选一个非默认程序打开文件。
    /// （ECP 同款功能；这里是用 rundll32 + OpenAs_RunDLL 的原生实现，未复制 ECP 代码。）
    /// </summary>
    internal sealed partial class OpenWithCommand : InvokableCommand
    {
        #region Public Constructors

        public OpenWithCommand(string path)
        {
            _path = path;
            Name = "选择其他应用打开";
            Icon = _theIcon;
        }

        #endregion Public Constructors

        #region Fields

        private static readonly IconInfo _theIcon = new("\uE7AC");
        private readonly string _path;

        #endregion Fields

        #region Public Methods

        public override CommandResult Invoke()
        {
            try
            {
                // rundll32 + OpenAs_RunDLL 就是资源管理器「打开方式(H)…」菜单的底层实现。
                Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = $"shell32.dll,OpenAs_RunDLL \"{_path}\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
                return CommandResult.ShowToast("打开「打开方式」对话框失败");
            }

            return CommandResult.Dismiss();
        }

        #endregion Public Methods
    }
}
