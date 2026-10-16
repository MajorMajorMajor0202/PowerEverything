using EverythingExtension.Settings;

using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Diagnostics;

namespace EverythingExtension.Commands
{
    /// <summary>
    /// 用设置里指定的程序打开文件（ECP 的 "Send to specified" 同款功能；原生实现）。
    /// 设置项为空时给个提示，不静默失败。
    /// </summary>
    internal sealed partial class SendToProgramCommand : InvokableCommand
    {
        #region Public Constructors

        public SendToProgramCommand(string path)
        {
            _path = path;
            Name = "发送到指定程序";
            Icon = _theIcon;
        }

        #endregion Public Constructors

        #region Fields

        private static readonly IconInfo _theIcon = new("\uE898");
        private readonly string _path;

        #endregion Fields

        #region Public Methods

        public override CommandResult Invoke()
        {
            var program = EverythingSettings.Instance.SendToProgram;
            if (string.IsNullOrWhiteSpace(program))
            {
                return CommandResult.ShowToast("请先在扩展设置里配置「发送到指定程序」的程序路径");
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = program,
                    Arguments = $"\"{_path}\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
                return CommandResult.ShowToast($"启动 {program} 失败");
            }

            return CommandResult.Dismiss();
        }

        #endregion Public Methods
    }
}
