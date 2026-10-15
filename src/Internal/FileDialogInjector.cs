using System;
using System.Text;
using System.Threading;

namespace QuickJumpCmdPal.Internal;

/// <summary>
/// 把某个文件夹路径「写进」文件选择窗口，并按需要回车确认，
/// 效果等同于用户在「文件名」框里手打路径再回车。
///
/// 三级回退（每一级都验证「文件名框里的文本确实变了」才算成功）：
///   1. keybd_event 发一个真实的回车（前提是目标窗口已在前台）；
///   2. PostMessage 把 WM_KEYDOWN/WM_KEYUP 直接投给输入框
///      —— 对话框的消息循环会把它当默认按钮处理；
///   3. PostMessage WM_COMMAND + IDOK 给对话框本体，等于点了「打开/保存」。
///
/// 为什么要验证「文本变了」：路径跳转成功后，对话框通常会清空文件名框，
/// 所以「框里的文本不再等于刚填进去的路径」是当前能拿到的最好的成功信号。
/// </summary>
internal static class FileDialogInjector
{
    /// <summary>每级回退之后等待对话框反应的时长。</summary>
    private static readonly TimeSpan ReactionTimeout = TimeSpan.FromMilliseconds(800);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 在目标对话框里跳到指定文件夹。
    /// </summary>
    /// <param name="dialog">目标文件选择窗口。</param>
    /// <param name="path">要跳转到的文件夹绝对路径。</param>
    /// <param name="autoEnter">是否自动回车（关掉的话只把路径填进输入框）。</param>
    /// <param name="detail">人类可读的执行细节，用于日志。</param>
    /// <returns>路径是否确实被写进了输入框。</returns>
    internal static bool TryInjectPath(FileDialogWindow dialog, string path, bool autoEnter, out string detail)
    {
        if (!NativeMethods.IsWindow(dialog.Hwnd))
        {
            detail = "目标窗口已经关闭";
            return false;
        }

        var edit = FileDialogLocator.FindFileNameEdit(dialog.Hwnd);
        if (edit == IntPtr.Zero)
        {
            detail = "找不到「文件名」输入框";
            return false;
        }

        // 1) 填路径
        NativeMethods.SendMessageTimeout(
            edit,
            NativeMethods.WM_SETTEXT,
            IntPtr.Zero,
            path,
            NativeMethods.SMTO_ABORTIFHUNG,
            2000,
            out _);

        var written = ReadEditText(edit);
        if (!string.Equals(written, path, StringComparison.Ordinal))
        {
            detail = $"写入路径失败，输入框内容是「{written}」";
            return false;
        }

        if (!autoEnter)
        {
            detail = "已把路径填进文件名框（按设置未回车）";
            return true;
        }

        // 2) 真实键盘回车
        NativeMethods.TryBringToForeground(dialog.Hwnd);
        NativeMethods.keybd_event(NativeMethods.VK_RETURN, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_RETURN, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (WaitForEditToChange(edit, path))
        {
            detail = "已跳转（键盘回车）";
            return true;
        }

        // 3) 把按键消息直接投给输入框
        NativeMethods.PostMessage(edit, NativeMethods.WM_KEYDOWN, NativeMethods.VK_RETURN, IntPtr.Zero);
        NativeMethods.PostMessage(edit, NativeMethods.WM_KEYUP, NativeMethods.VK_RETURN, IntPtr.Zero);
        if (WaitForEditToChange(edit, path))
        {
            detail = "已跳转（按键消息）";
            return true;
        }

        // 4) 直接通知对话框按下默认按钮
        NativeMethods.PostMessage(dialog.Hwnd, NativeMethods.WM_COMMAND, NativeMethods.IDOK, edit);
        if (WaitForEditToChange(edit, path))
        {
            detail = "已跳转（WM_COMMAND IDOK）";
            return true;
        }

        // 路径确实填进去了，只是没能自动确认 —— 用户手动按一下回车即可，不算彻底失败。
        detail = "路径已填进文件名框，但自动回车没被接受（请手动按回车）";
        return true;
    }

    private static string ReadEditText(IntPtr edit)
    {
        try
        {
            var buffer = new StringBuilder(4096);
            NativeMethods.SendMessage(edit, NativeMethods.WM_GETTEXT, buffer.Capacity, buffer);
            return buffer.ToString();
        }
        catch (Exception ex)
        {
            Log.Warn($"读取输入框文本失败：{ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// 等待输入框内容不再等于刚填进去的路径（说明对话框接受并跳转了）。
    /// </summary>
    private static bool WaitForEditToChange(IntPtr edit, string path)
    {
        var deadline = DateTime.UtcNow + ReactionTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!NativeMethods.IsWindow(edit))
            {
                // 窗口没了（例如「打开」对话框确认后关闭），也算生效了。
                return true;
            }

            if (!string.Equals(ReadEditText(edit), path, StringComparison.Ordinal))
            {
                return true;
            }

            Thread.Sleep(PollInterval);
        }

        return false;
    }
}
