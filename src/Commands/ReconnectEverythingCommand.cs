using EverythingExtension.Internal;
using EverythingExtension.Pages;
using EverythingExtension.Properties;

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Windows.ApplicationModel;

namespace EverythingExtension.Commands
{
    internal sealed partial class ReconnectEverythingCommand : InvokableCommand
    {
        #region Public Constructors

        public ReconnectEverythingCommand(EverythingExtensionPage page)
        {
            _page = page;
            Name = Resources.everything_reconnect;
            Icon = new IconInfo("\ue72c");
        }

        #endregion Public Constructors

        #region Fields

        private readonly EverythingExtensionPage _page;
        private readonly StatusMessage _installBanner = new();

        #endregion Fields

        #region Public Methods

        public override ICommandResult Invoke(object? sender)
        {
            if (_page.EverythingInitialize())
            {
                _installBanner.Message = Resources.everything_reconnect_success_message;
                _installBanner.State = MessageState.Success;
                EverythingExtensionHost.Instance.ShowStatusWithAutoHide(_installBanner, StatusContext.Extension);
                // 用页面自身的 Id 而不是硬编码值：范围页的 Id 按路径派生，
                // 硬编码会把用户从范围页踢回全局搜索页。
                var pageId = string.IsNullOrEmpty(_page.Id) ? CommandIds.SearchPage : _page.Id;
                return CommandResult.GoToPage(new GoToPageArgs { PageId = pageId });
            }
            else
            {
                _installBanner.State = MessageState.Error;
                _installBanner.Message = Resources.everything_reconnect_fail_message;
                EverythingExtensionHost.Instance.ShowStatusWithAutoHide(_installBanner, StatusContext.Extension);
                return CommandResult.KeepOpen();
            }
        }

        #endregion Public Methods
    }
}