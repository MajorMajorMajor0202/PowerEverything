using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Diagnostics;

namespace EverythingExtension.Commands
{
    /// <summary>
    /// 打开文件 / 文件夹的属性窗口（等效资源管理器右键「属性」）。
    /// （ECP 同款功能；原生实现，未复制 ECP 代码。）
    /// </summary>
    internal sealed partial class OpenPropertiesCommand : InvokableCommand
    {
        #region Public Constructors

        public OpenPropertiesCommand(string path)
        {
            _path = path;
            Name = "属性";
            Icon = _theIcon;
        }

        #endregion Public Constructors

        #region Fields

        private static readonly IconInfo _theIcon = new("\uE946");
        private readonly string _path;

        #endregion Fields

        #region Public Methods

        public override CommandResult Invoke()
        {
            try
            {
                // ShellExecute 的 "properties" 动词 = 资源管理器右键菜单里的「属性」。
                Process.Start(new ProcessStartInfo(_path)
                {
                    UseShellExecute = true,
                    Verb = "properties",
                });
            }
            catch
            {
                return CommandResult.ShowToast("打开属性窗口失败");
            }

            return CommandResult.Dismiss();
        }

        #endregion Public Methods
    }
}
