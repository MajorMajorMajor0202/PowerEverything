// Copyright (c) Microsoft Corporation The Microsoft Corporation licenses this file to you under the
// MIT license. See the LICENSE file in the project root for more information.

using EverythingExtension.Commands;
using EverythingExtension.Internal;
using EverythingExtension.Pages;
using EverythingExtension.SDK;

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

        public static IContextItem[] LoadContextMenus(IEverythingClient client, SearchResult searchResult, bool isFirstLevelFolder = false)
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
            contextMenus.Add(new CommandContextItem(new DeleteCommand(searchResult)));

            // QuickLook 预览：只对文件有意义（文件夹预览只是个信息框）。
            if (searchResult.Type == ResultType.File)
            {
                contextMenus.Add(new CommandContextItem(new Commands.PreviewWithQuickLookCommand(searchResult.FullPath)));
            }

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