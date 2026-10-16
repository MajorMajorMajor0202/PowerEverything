using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

using System;

using Windows.System;

namespace EverythingExtension.Internal;

/// <summary>
/// 把「F1」「Ctrl+Shift+P」「Alt+D」这类文本解析成宿主认的 <see cref="KeyChord"/>，
/// 供设置项里的「预览快捷键」使用。解析不了返回 null（调用方就不绑快捷键，菜单项本身不受影响）。
/// </summary>
internal static class HotkeyParser
{
    /// <summary>
    /// 解析快捷键文本。格式：若干修饰键（Ctrl / Alt / Shift / Win，用 + 连接，大小写无所谓）
    /// 加一个按键名（F1-F24、A-Z、Left 等 <see cref="VirtualKey"/> 枚举名）。
    /// </summary>
    public static KeyChord? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var ctrl = false;
        var alt = false;
        var shift = false;
        var win = false;

        // 最后一段是按键，前面全是修饰键。
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                case "win":
                case "windows":
                    win = true;
                    break;
                default:
                    return null;
            }
        }

        if (!Enum.TryParse(parts[^1], ignoreCase: true, out VirtualKey vkey))
        {
            return null;
        }

        // 参数顺序（见 KeyChordHelpers.FromModifiers）：ctrl, alt, shift, win, vkey, scanCode。
        return KeyChordHelpers.FromModifiers(ctrl, alt, shift, win, vkey, 0);
    }
}
