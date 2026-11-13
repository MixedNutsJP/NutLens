using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Imaging;

namespace NutLens.Core.Capture;

/// <summary>
/// 「画面の矩形」を実現するデコレータ（ADR-0003 決定 2）。
///
/// Windows.Graphics.Capture が取れる単位はウィンドウかモニタだけで、任意の矩形を指定する入口が無い。
/// そこでモニタ全体を取ってから、ここで切り出す。
/// 上位層（<see cref="CaptureSessionService"/>）は包まれていることを知らない。
///
/// 画素の並べ替えだけで Windows に依存しないので、実装は Core に置く（ADR-0002 決定 2）。
/// </summary>
public sealed class CroppingFrameSource : IFrameSource
{
    private readonly IFrameSource inner;
    private readonly PixelRect region;

    public CroppingFrameSource(IFrameSource inner, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(inner);

        this.inner = inner;
        this.region = region;
    }

    public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (CapturedFrame frame in this.inner
            .ReadFramesAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return Crop(frame, this.region);
        }
    }

    public ValueTask DisposeAsync() => this.inner.DisposeAsync();

    /// <summary>行ごとにコピーして切り出す。矩形はフレームの寸法に収まるよう切り詰める。</summary>
    public static CapturedFrame Crop(CapturedFrame frame, PixelRect region)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // モニタの解像度が撮影中に変わっても範囲外にならないよう、毎回クランプする。
        PixelRect clamped = region.ClampTo(frame.Width, frame.Height);

        if (clamped.X == 0
            && clamped.Y == 0
            && clamped.Width == frame.Width
            && clamped.Height == frame.Height)
        {
            return frame;
        }

        int destinationStride = clamped.Width * 4;
        byte[] destination = new byte[destinationStride * clamped.Height];

        for (int y = 0; y < clamped.Height; y++)
        {
            int sourceOffset = ((clamped.Y + y) * frame.Stride) + (clamped.X * 4);
            Array.Copy(frame.Bgra, sourceOffset, destination, y * destinationStride, destinationStride);
        }

        return new CapturedFrame(destination, clamped.Width, clamped.Height, destinationStride, frame.Elapsed);
    }
}
