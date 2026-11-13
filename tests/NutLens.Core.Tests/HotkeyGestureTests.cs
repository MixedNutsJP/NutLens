using System;
using NutLens.Core.Input;
using NutLens.Core.Settings;
using Xunit;

namespace NutLens.Core.Tests;

/// <summary>ホットキーの書式（F-04 / ADR-0023 決定 3）。</summary>
public sealed class HotkeyGestureTests
{
    [Fact]
    public void 既定は往復する()
    {
        Assert.True(HotkeyGesture.TryParse(HotkeyGesture.DefaultText, out HotkeyGesture gesture));
        Assert.Equal("Ctrl+Shift+F9", gesture.ToString());
    }

    [Theory]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("Control+Shift+F9")]
    [InlineData("ctrl + shift + f9")]
    [InlineData("SHIFT+CTRL+F9")]
    public void 書き方が違っても同じものになる(string text)
    {
        Assert.True(HotkeyGesture.TryParse(text, out HotkeyGesture gesture));
        Assert.Equal(HotkeyGesture.Default, gesture);
    }

    [Fact]
    public void 修飾キーの並び順は出力で揃う()
    {
        // 入力の順に関係なく同じ文字列になる。設定ファイルの差分が揺れないようにするため。
        HotkeyGesture a = HotkeyGesture.Parse("Win+Shift+Alt+Ctrl+S");
        HotkeyGesture b = HotkeyGesture.Parse("Ctrl+Alt+Shift+Win+S");

        Assert.Equal(a.ToString(), b.ToString());
        Assert.Equal("Ctrl+Alt+Shift+Win+S", a.ToString());
    }

    [Theory]
    [InlineData("A", 0x41u)]
    [InlineData("Z", 0x5Au)]
    [InlineData("0", 0x30u)]
    [InlineData("9", 0x39u)]
    [InlineData("F1", 0x70u)]
    [InlineData("F24", 0x87u)]
    public void 受け付けるキー(string text, uint expected)
    {
        Assert.True(HotkeyGesture.TryParse(text, out HotkeyGesture gesture));
        Assert.Equal(expected, gesture.VirtualKey);
        Assert.Equal(HotkeyModifiers.None, gesture.Modifiers);
    }

    [Theory]
    [InlineData("Ctrl+;")]
    [InlineData("Ctrl+-")]
    [InlineData("Ctrl+[")]
    [InlineData("Ctrl+@")]
    public void 記号キーは受け付けない(string text)
    {
        // 配列によって仮想キーコードが変わり、「設定した通りに効かない」という形で表れる。
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Hoge+A")]
    public void 読めない文字列でも例外にしない(string? text)
    {
        // 設定ファイルは利用者が手で書き換えられる。
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }

    [Fact]
    public void 修飾キーの値はWin32と同じ()
    {
        // MOD_ALT=1 / MOD_CONTROL=2 / MOD_SHIFT=4 / MOD_WIN=8。
        // Core は Windows の API を参照できないので、数値だけを合わせている。
        Assert.Equal(1, (int)HotkeyModifiers.Alt);
        Assert.Equal(2, (int)HotkeyModifiers.Control);
        Assert.Equal(4, (int)HotkeyModifiers.Shift);
        Assert.Equal(8, (int)HotkeyModifiers.Windows);
    }

    [Fact]
    public void 設定の既定はCtrlShiftF9()
    {
        HotkeySettings settings = new();

        Assert.True(settings.Enabled);
        Assert.Equal("Ctrl+Shift+F9", settings.Gesture);
    }

    [Fact]
    public void 読めない設定は既定へ戻る()
    {
        // ここで直さないと、起動のたびに「登録できません」が出続ける。
        HotkeySettings settings = new HotkeySettings { Gesture = "Ctrl+なにか" }.Normalize();

        Assert.Equal(HotkeyGesture.DefaultText, settings.Gesture);
    }

    [Fact]
    public void 読める設定は正規化された形になる()
    {
        HotkeySettings settings = new HotkeySettings { Gesture = "control + shift + f9" }.Normalize();

        Assert.Equal("Ctrl+Shift+F9", settings.Gesture);
    }
}
