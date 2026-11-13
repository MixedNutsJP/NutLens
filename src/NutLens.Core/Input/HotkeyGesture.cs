using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NutLens.Core.Input;

/// <summary>
/// ホットキーの修飾キー（F-04 / ADR-0023）。
///
/// 値は Win32 の <c>MOD_ALT</c> / <c>MOD_CONTROL</c> / <c>MOD_SHIFT</c> / <c>MOD_WIN</c> と同じ。
/// <b>Core は Windows の API を参照できない</b>（<c>net10.0</c>。ADR-0002）ので、
/// 数値だけを合わせて素の整数で持つ。
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// 「Ctrl+Shift+F9」のような 1 つの組み合わせ（ADR-0023 決定 3）。
///
/// <b>設定ファイルには人が読める文字列で書く。</b>
/// 列挙型のビットを数値で書くと利用者が読めず、手で直せない
/// （D-05 の「設定で変えられる」が実質失われる）。
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>既定。Windows と主要アプリで使われていないという<b>推測</b>（ADR-0023 宿題）。</summary>
    public const string DefaultText = "Ctrl+Shift+F9";

    private const uint VkA = 0x41;
    private const uint VkZ = 0x5A;
    private const uint Vk0 = 0x30;
    private const uint Vk9 = 0x39;
    private const uint VkF1 = 0x70;
    private const uint FunctionKeyCount = 24;

    public static HotkeyGesture Default { get; } = Parse(DefaultText);

    /// <summary>
    /// 文字列から組み立てる。読めなければ false。
    /// <b>例外にしない。</b>設定ファイルは利用者が手で書き換えられる。
    /// </summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;

        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            string part = raw.Trim();
            if (part.Length == 0)
            {
                return false;
            }

            if (TryParseModifier(part, out HotkeyModifiers modifier))
            {
                // 同じ修飾を 2 回書かれても足し合わせるだけ。害が無い。
                modifiers |= modifier;
                continue;
            }

            // キー本体は 1 つだけ。2 つ書かれたら読めない組み合わせ。
            if (virtualKey != 0 || !TryParseKey(part, out virtualKey))
            {
                return false;
            }
        }

        if (virtualKey == 0)
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, virtualKey);
        return true;
    }

    public static HotkeyGesture Parse(string text)
    {
        if (!TryParse(text, out HotkeyGesture gesture))
        {
            throw new FormatException($"ホットキーの書式が不正です: {text}");
        }

        return gesture;
    }

    /// <summary>設定ファイルと画面に出す形。<see cref="TryParse"/> で読み戻せる。</summary>
    public override string ToString()
    {
        StringBuilder builder = new();

        // 並び順を固定する。入力の順に関係なく同じ文字列になるようにするため。
        AppendModifier(builder, this.Modifiers, HotkeyModifiers.Control, "Ctrl");
        AppendModifier(builder, this.Modifiers, HotkeyModifiers.Alt, "Alt");
        AppendModifier(builder, this.Modifiers, HotkeyModifiers.Shift, "Shift");
        AppendModifier(builder, this.Modifiers, HotkeyModifiers.Windows, "Win");

        builder.Append(KeyName(this.VirtualKey));
        return builder.ToString();
    }

    private static void AppendModifier(
        StringBuilder builder,
        HotkeyModifiers modifiers,
        HotkeyModifiers flag,
        string name)
    {
        if (modifiers.HasFlag(flag))
        {
            builder.Append(name).Append('+');
        }
    }

    private static string KeyName(uint virtualKey)
    {
        if (virtualKey is (>= VkA and <= VkZ) or (>= Vk0 and <= Vk9))
        {
            return ((char)virtualKey).ToString();
        }

        if (virtualKey is >= VkF1 and < VkF1 + FunctionKeyCount)
        {
            return string.Format(CultureInfo.InvariantCulture, "F{0}", virtualKey - VkF1 + 1);
        }

        // ここに来るのは、設定ファイルではなくコードから作ったときだけ。
        return string.Format(CultureInfo.InvariantCulture, "0x{0:X2}", virtualKey);
    }

    private static bool TryParseModifier(string part, out HotkeyModifiers modifier)
    {
        modifier = part.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => HotkeyModifiers.Control,
            "ALT" => HotkeyModifiers.Alt,
            "SHIFT" => HotkeyModifiers.Shift,
            "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };

        return modifier != HotkeyModifiers.None;
    }

    /// <summary>
    /// 受け付けるのは A〜Z・0〜9・F1〜F24 だけ。
    ///
    /// <b>記号キーを受け付けない。</b>配列によって仮想キーコードが変わるため、
    /// 「設定した通りに効かない」という形で表れる。
    /// </summary>
    private static bool TryParseKey(string part, out uint virtualKey)
    {
        virtualKey = 0;
        string upper = part.ToUpperInvariant();

        if (upper.Length == 1)
        {
            char c = upper[0];
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                virtualKey = c;
                return true;
            }

            return false;
        }

        if (upper[0] == 'F'
            && uint.TryParse(upper.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out uint number)
            && number >= 1
            && number <= FunctionKeyCount)
        {
            virtualKey = VkF1 + number - 1;
            return true;
        }

        return false;
    }
}

/// <summary>
/// グローバルホットキーの登録（F-04 / ADR-0023）。
/// 実装は <c>NutLens.App</c>（<c>HwndSource</c> は WPF のため）。
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// 登録できたら true。<b>できなくても例外にしない。</b>
    /// ほとんどの失敗は「他のアプリが同じ組み合わせを先に取っている」で、
    /// アプリを止める理由にならない（ADR-0023 決定 4）。
    /// </summary>
    bool TryRegister(HotkeyGesture gesture);

    void Unregister();

    /// <summary>いま登録されている組み合わせ。未登録なら null。</summary>
    HotkeyGesture? Current { get; }

    /// <summary>押された。開始と停止の切り替えは購読側が決める（ADR-0023 決定 5）。</summary>
    event EventHandler? Pressed;
}
