using System;
using NutLens.Capture.Input;
using NutLens.Core.Input;

namespace NutLens.CaptureProbe;

/// <summary>
/// ホットキーが登録できるかを確かめる（F-04 / ADR-0023 の宿題）。
///
/// <b>ウィンドウは出ない。</b>メッセージ専用ウィンドウを作って登録し、すぐ外す。
/// 単体テストでは守れない（<c>RegisterHotKey</c> は Windows 専用 API で、
/// テストは <c>net10.0</c> なので <c>NutLens.Capture</c> を参照できない）。
///
/// 既定値 <c>Ctrl+Shift+F9</c> が<b>この PC で空いているか</b>も、これで分かる。
/// </summary>
internal static class HotkeySelfTest
{
    public static int Run(string? gestureText)
    {
        string text = string.IsNullOrWhiteSpace(gestureText)
            ? HotkeyGesture.DefaultText
            : gestureText;

        if (!HotkeyGesture.TryParse(text, out HotkeyGesture gesture))
        {
            Console.Error.WriteLine($"書式が読めません: {text}");
            Console.Error.WriteLine("例: Ctrl+Shift+F9（A〜Z・0〜9・F1〜F24 のみ）");
            return 5;
        }

        Console.WriteLine($"入力     : {text}");
        Console.WriteLine($"正規化後 : {gesture}");
        Console.WriteLine($"修飾     : {gesture.Modifiers}（Win32 と同じ値: {(int)gesture.Modifiers}）");
        Console.WriteLine($"仮想キー : 0x{gesture.VirtualKey:X2}");
        Console.WriteLine();

        using HotkeyService service = new();

        bool ok = service.TryRegister(gesture);
        Console.WriteLine(ok
            ? "登録     : できた（この組み合わせは空いている）"
            : "登録     : **できなかった**。他のアプリが使っている可能性が高い");

        if (ok)
        {
            service.Unregister();
            Console.WriteLine("解除     : した");
        }

        Console.WriteLine();
        Console.WriteLine("※ 実際に押したときに WM_HOTKEY が届くかは、アプリを起動して確かめる必要がある。");

        return ok ? 0 : 9;
    }
}
