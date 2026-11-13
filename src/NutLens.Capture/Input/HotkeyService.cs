using System;
using System.Windows.Interop;
using NutLens.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace NutLens.Capture.Input;

/// <summary>
/// グローバルホットキー（F-04 / ADR-0023）。
///
/// <b>メッセージ専用ウィンドウに登録する。</b>メインウィンドウには登録しない。
/// メインウィンドウは閉じるとトレイへ畳まれ（ADR-0025）、
/// 破棄されるとハンドルが無効になってホットキーが黙って効かなくなる。
/// 「トレイに居るのに効かない」は、利用者が原因を特定できない壊れ方になる。
///
/// <c>RegisterHotKey</c> は OS に組み合わせを押さえさせるだけで、
/// <b>他のキー入力は見えない</b>（低レベルフックを却下した理由。ADR-0023 却下案）。
/// </summary>
public sealed class HotkeyService : IHotkeyService
{
    /// <summary>メッセージ専用ウィンドウの親。表示もされず、ウィンドウ一覧にも出ない。</summary>
    private const int HwndMessage = -3;

    private const int WmHotkey = 0x0312;

    /// <summary>このプロセス内で一意なら何でもよい。</summary>
    private const int HotkeyId = 0xBEEF;

    private HwndSource? source;
    private bool registered;

    public HotkeyGesture? Current { get; private set; }

    public event EventHandler? Pressed;

    public bool TryRegister(HotkeyGesture gesture)
    {
        this.EnsureWindow();

        if (this.source is not HwndSource window)
        {
            return false;
        }

        // 先に古い登録を外す。同じ ID で二重に登録できないため。
        this.Unregister();

        HWND handle = new(window.Handle);
        bool ok = PInvoke.RegisterHotKey(
            handle,
            HotkeyId,
            (HOT_KEY_MODIFIERS)gesture.Modifiers,
            gesture.VirtualKey);

        if (ok)
        {
            this.registered = true;
            this.Current = gesture;
        }

        // 失敗はほぼ「他のアプリが同じ組み合わせを先に取っている」。
        // 例外にしない。ホットキーが取れないだけでアプリを止めない（ADR-0023 決定 4）。
        return ok;
    }

    public void Unregister()
    {
        if (!this.registered || this.source is not HwndSource window)
        {
            return;
        }

        PInvoke.UnregisterHotKey(new HWND(window.Handle), HotkeyId);
        this.registered = false;
        this.Current = null;
    }

    public void Dispose()
    {
        this.Unregister();

        if (this.source is HwndSource window)
        {
            window.RemoveHook(this.OnMessage);
            window.Dispose();
            this.source = null;
        }
    }

    private void EnsureWindow()
    {
        if (this.source is not null)
        {
            return;
        }

        HwndSourceParameters parameters = new("NutLensHotkey")
        {
            ParentWindow = new IntPtr(HwndMessage),
        };

        this.source = new HwndSource(parameters);
        this.source.AddHook(this.OnMessage);
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmHotkey || wParam.ToInt32() != HotkeyId)
        {
            return IntPtr.Zero;
        }

        handled = true;
        this.Pressed?.Invoke(this, EventArgs.Empty);
        return IntPtr.Zero;
    }
}
