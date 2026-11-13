using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Imaging;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.Core.Capture;

/// <summary>撮影対象の種類。Windows.Graphics.Capture が扱えるのはこの 2 つだけ（ADR-0003）。</summary>
public enum CaptureTargetKind
{
    Window,
    Monitor,
}

/// <summary>画面上の矩形。モニタ相対の座標で持つ。</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => this.Width <= 0 || this.Height <= 0;

    /// <summary>フレームの寸法に収まるよう切り詰める。解像度が変わっても範囲外にならないように。</summary>
    public PixelRect ClampTo(int frameWidth, int frameHeight)
    {
        int x = Math.Clamp(this.X, 0, Math.Max(0, frameWidth - 1));
        int y = Math.Clamp(this.Y, 0, Math.Max(0, frameHeight - 1));
        int width = Math.Clamp(this.Width, 1, Math.Max(1, frameWidth - x));
        int height = Math.Clamp(this.Height, 1, Math.Max(1, frameHeight - y));
        return new PixelRect(x, y, width, height);
    }
}

/// <summary>
/// 撮影対象。
/// <paramref name="CropRegion"/> は <see cref="CaptureTargetKind.Monitor"/> のときだけ意味を持ち、
/// 「画面の矩形」をモニタ取得＋切り出しで実現するために使う（F-01 / ADR-0003 決定 2）。
/// </summary>
public sealed record CaptureTarget(
    CaptureTargetKind Kind,
    nint Handle,
    string Title,
    PixelRect? CropRegion = null);

/// <summary>撮れる対象を列挙する役。実装は Win32 に触るので NutLens.Capture 側。</summary>
public interface ICaptureTargetProvider
{
    IReadOnlyList<CaptureTarget> GetTargets();
}

/// <summary>
/// フレームを非同期の列として吐き出す役。
/// WGC のイベント駆動（FrameArrived）は実装側で Channel に橋渡しする。
/// </summary>
public interface IFrameSource : IAsyncDisposable
{
    IAsyncEnumerable<CapturedFrame> ReadFramesAsync(CancellationToken cancellationToken);
}

/// <summary>対象と撮影間隔から <see cref="IFrameSource"/> を作る役。</summary>
public interface IFrameSourceFactory
{
    IFrameSource Create(CaptureTarget target, TimeSpan interval);
}

/// <summary>1 回の撮影の指示。</summary>
public sealed record CaptureRequest(CaptureTarget Target, CaptureSettings Settings);

/// <summary>
/// 撮影の進み具合。
/// 受信枚数と採用枚数の両方を出す。重複除去が効くと採用枚数は途中から増えなくなるので、
/// 採用枚数だけを見せると止まったように見える（ADR-0004 の影響）。
/// </summary>
public sealed record CaptureProgress(int ReceivedFrames, int AcceptedFrames, TimeSpan Elapsed);

/// <summary>撮影の結果。</summary>
/// <param name="AudioBytes">録音できたバイト数。0 なら録れていない。</param>
/// <param name="AudioFailureReason">録音を始められなかった理由。null なら問題なし。</param>
public sealed record CaptureResult(
    SessionId SessionId,
    string SessionDirectory,
    string FramesDirectory,
    IReadOnlyList<string> FrameFiles,
    int ReceivedFrames,
    int AcceptedFrames,
    TimeSpan Duration,
    bool StoppedManually,
    long AudioBytes = 0,
    string? AudioFailureReason = null);

/// <summary>1 回の撮影を取り仕切る役。</summary>
public interface ICaptureSessionService
{
    Task<CaptureResult> RunAsync(
        CaptureRequest request,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken);
}
