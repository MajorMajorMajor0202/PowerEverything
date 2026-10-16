namespace QuickJumpCmdPal.Internal;

/// <summary>
/// 命令 / 提供程序的标识符。
///
/// 注意：<see cref="JumpPage"/> 是宿主用来标识这条命令的身份，
/// 用户在命令面板里给它配的「别名 / 快捷键」都绑在这个 Id 上
/// （宿主自带的功能，扩展侧不参与），一旦改动，用户此前绑定的别名/快捷键就会失效。
/// </summary>
internal static class CommandIds
{
    /// <summary>提供程序 Id（仅用于标识自身）。</summary>
    internal const string ProviderId = "goucey.cmdPal.quickJump";

    /// <summary>主页面（也就是顶层命令）的 Id。</summary>
    internal const string JumpPage = "goucey.cmdPal.quickJump.jump";

    /// <summary>命令面板里显示的扩展名。纯英文，不掺「中文 (English)」式注释。</summary>
    internal const string DisplayName = "QuickJump";

    /// <summary>顶层命令标题（功能入口）。与全局搜索入口的「Everything 全局搜索」保持同一命名风格。</summary>
    internal const string JumpTitle = "Everything 快速跳转";

    /// <summary>功能入口下面那行说明。</summary>
    internal const string JumpSubtitle = "搜索任意文件夹 / 文件，回车送进「打开 / 另存为」窗口";

    /// <summary>设置页标题。</summary>
    internal const string SettingsTitle = "QuickJump Settings";
}
