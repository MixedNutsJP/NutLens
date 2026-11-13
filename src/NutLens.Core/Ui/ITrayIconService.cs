using System;

namespace NutLens.Core.Ui;

/// <summary>
/// タスクトレイの常駐アイコン（6 章 / ADR-0009 / ADR-0025）。
///
/// トレイに触るコードをこのインタフェースの背後に置く。
/// ライブラリを差し替える必要が出たとき、変更が 1 クラスに収まる（ADR-0009 決定 2）。
/// </summary>
public interface ITrayIconService : IDisposable
{
    void Show();

    /// <summary>
    /// 撮影中かどうかでアイコンを変える（ADR-0025 決定 6）。
    ///
    /// ウィンドウを畳んだままホットキーで撮影が始まると、
    /// <b>画面を見ずに撮影が始まる</b>ので、動いていることが分かる必要がある。
    /// </summary>
    void SetCapturing(bool capturing);

    /// <summary>撮影の開始・停止（ホットキーと同じ切り替え）。</summary>
    event EventHandler? ToggleCaptureRequested;

    event EventHandler? OpenWindowRequested;

    event EventHandler? OpenSettingsRequested;

    /// <summary>終了。<b>これが終了の唯一の入口</b>（ADR-0025 決定 5）。</summary>
    event EventHandler? ExitRequested;
}
