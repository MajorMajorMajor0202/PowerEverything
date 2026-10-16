using System;
using System.IO;
using System.Threading.Tasks;

using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace EverythingExtension.Helpers;

/// <summary>
/// 给列表项挂「可拖出」的数据包：<c>ListItem.DataPackage</c>。
/// 宿主（命令面板）拿到 DataPackage 后自己发起 OLE 拖拽，
/// 拖到资源管理器 / 桌面 / 别的地方落下来的就是真实文件 / 文件夹。
///
/// 这是 CmdPal SDK 原生支持的拖拽出口（ECP 也是这么做的），不需要辅助进程。
/// StorageItems 用延迟提供：只有接收方真要文件数据时才去取 StorageFile，
/// 避免为列表里每个结果都提前做一遍 IO。
/// </summary>
internal static class DataPackageHelper
{
    /// <summary>为路径构造 DataPackage；路径为空 / 不存在时返回 null。</summary>
    public static DataPackage? ForPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        var capturedPath = fullPath;
        var trimmed = capturedPath.TrimEnd('\\', '/');
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy,
            Properties =
            {
                Title = Path.GetFileName(trimmed) is { Length: > 0 } name ? name : trimmed,
                Description = capturedPath,
            },
        };

        // StorageItems 用延迟提供：只有接收方真要文件数据时才去取 StorageFile，
        // 避免每次列目录都为所有结果做一遍异步 IO。
        // （async lambda 赋给 void 委托即 async void，与 PowerToys Indexer 扩展同一写法。）
        DataProviderHandler handler = async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var items = await TryGetStorageItemAsync(capturedPath).ConfigureAwait(false);
                if (items is not null)
                {
                    request.SetData(items);
                }

                // 取不到就只提供标题 / 描述文本，拖到文本框之类的场景仍可用。
            }
            catch
            {
                // 同上：部分数据好过整个拖拽失败。
            }
            finally
            {
                deferral.Complete();
            }
        };

        package.SetDataProvider(StandardDataFormats.StorageItems, handler);

        return package;
    }

    private static async Task<IStorageItem[]?> TryGetStorageItemAsync(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var file = await StorageFile.GetFileFromPathAsync(filePath);
                return [file];
            }

            if (Directory.Exists(filePath))
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(filePath);
                return [folder];
            }

            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
