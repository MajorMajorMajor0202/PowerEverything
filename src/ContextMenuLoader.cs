// Copyright (c) Microsoft Corporation The Microsoft Corporation licenses this file to you under the
// MIT license. See the LICENSE file in the project root for more information.

using EverythingExtension.Commands;
using EverythingExtension.Internal;
using EverythingExtension.Pages;
using EverythingExtension.SDK;
using EverythingExtension.Settings;

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using System;
using System.Collections.Generic;
using System.IO;

using Windows.System;

namespace EverythingExtension
{
    internal static class ContextMenuLoader
    {
        #region Fields

        //private static readonly IFileSystem FileSystem = new FileSystem();
        //private static readonly IPath Path = FileSystem.Path;
        //private static readonly IFile File = FileSystem.File;
        //private static readonly IDirectory Directory = FileSystem.Directory;

        //private readonly PluginInitContext _context;

        // Extensions for adding run as admin context menu item for applications
        private static readonly string[] AppExtensions = [".exe", ".bat", ".appref-ms", ".lnk", ".msi", ".msix"];

        #endregion Fields

        #region Public Methods

        public static IContextItem[] LoadContextMenus(IEverythingClient client, SearchResult searchResult, bool isFirstLevelFolder = false, string? searchText = null)
        {
            List<IContextItem> contextMenus = [];
            // Test to check if File can be Run as admin, if yes, we add a 'run as admin' context
            // menu item

            if (searchResult.IsPreview)
            {
                if (searchResult.IsTextPreview)
                    contextMenus.Add(new CommandContextItem(new TextPreviewPage(searchResult, client)));
                else
                    contextMenus.Add(new CommandContextItem(new ImageViewPage(searchResult)));
            }

            if (CanFileBeRunAsAdmin(searchResult.FullPath))
            {
                contextMenus.Add(new CommandContextItem(new RunAsAdminCommand(searchResult)));
                contextMenus.Add(new CommandContextItem(new RunAsUserCommand(searchResult)));
            }

            if (searchResult.Type == ResultType.File)
            {
                contextMenus.Add(new CommandContextItem(new OpenInShellCommand("explorer.exe", $"/select,\"{searchResult.FullPath}\"")));
            }
            else
            {
                contextMenus.Add(isFirstLevelFolder
                    ? new CommandContextItem(new OpenCommand(searchResult, client))
                    : new CommandContextItem(new DirectoryExplorePage(searchResult.FullPath, client)));
            }

            contextMenus.Add(new CommandContextItem(new CopyFileCommand(searchResult)));

            // 复制路径：绑 Ctrl+Shift+C（和 Windows 资源管理器「复制为路径」默认快捷键一致）。
            contextMenus.Add(new CommandContextItem(new Commands.CopyPathCommand(searchResult))
            {
                RequestedShortcut = KeyChordHelpers.FromModifiers(ctrl: true, shift: true, vkey: VirtualKey.C),
            });
            contextMenus.Add(new CommandContextItem(new Commands.OpenInConsoleCommand(searchResult)));

            // 预览：文件与文件夹都能预览（文件夹显示内容面板）。
            // 快捷键默认 F1，可在扩展设置里改（PreviewShortcut 为 null 时不绑）。
            var previewItem = new CommandContextItem(new Commands.PreviewWithQuickLookCommand(searchResult.FullPath));
            var previewShortcut = EverythingSettings.Instance.PreviewShortcut;
            if (previewShortcut is not null)
            {
                previewItem.RequestedShortcut = previewShortcut.Value;
            }

            contextMenus.Add(previewItem);

            // 打开方式（Windows「打开方式」对话框）—— 只对文件有意义。
            if (searchResult.Type == ResultType.File)
            {
                contextMenus.Add(new CommandContextItem(new Commands.OpenWithCommand(searchResult.FullPath)));
            }

            // 发送到指定程序：设置里配了程序路径才出现。
            if (!string.IsNullOrEmpty(EverythingSettings.Instance.SendToProgram))
            {
                contextMenus.Add(new CommandContextItem(new Commands.SendToProgramCommand(searchResult.FullPath)));
            }

            // 属性窗口。
            contextMenus.Add(new CommandContextItem(new Commands.OpenPropertiesCommand(searchResult.FullPath)));

            // 在 Everything 中显示更多结果：把当前查询词原样交给 Everything 主程序。
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                contextMenus.Add(new CommandContextItem(new Commands.ShowInEverythingCommand(searchText)));
            }

            contextMenus.Add(new CommandContextItem(new DeleteCommand(searchResult)));

            return [.. contextMenus];
        }

        #endregion Public Methods

        #region Private Methods

        // Function to test if the file can be run as admin
        private static bool CanFileBeRunAsAdmin(string path)
        {
            string fileExtension = Path.GetExtension(path);
            foreach (string extension in AppExtensions)
            {
                // Using OrdinalIgnoreCase since this is internal
                if (extension.Equals(fileExtension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion Private Methods
    }
}