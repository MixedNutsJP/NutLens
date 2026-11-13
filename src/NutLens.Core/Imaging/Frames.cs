using System;

namespace NutLens.Core.Imaging;

/// <summary>
/// 間引き（<see cref="Sampling.IFrameThinner"/>）の対象になれることを表す。
/// Elapsed は常に「撮影開始からの経過時間」で、動画の再生位置ではない（要件 2 章）。
/// </summary>
public interface ITimestamped
{
    TimeSpan Elapsed { get; }
}

/// <summary>
/// 撮影直後の生フレーム。画素は BGRA32（[0]=B [1]=G [2]=R [3]=A）。
/// </summary>
/// <param name="Bgra">画素データ。長さは少なくとも (Height - 1) * Stride + Width * 4。</param>
/// <param name="Stride">1 行あたりのバイト数。Width * 4 より大きいことがある。</param>
public sealed record CapturedFrame(
    byte[] Bgra,
    int Width,
    int Height,
    int Stride,
    TimeSpan Elapsed) : ITimestamped
{
    /// <summary>画素データとして辻褄が合っているか。</summary>
    public bool IsValid =>
        this.Width > 0
        && this.Height > 0
        && this.Stride >= this.Width * 4
        && this.Bgra.Length >= ((this.Height - 1) * this.Stride) + (this.Width * 4);
}

/// <summary>縮小・符号化が済んだフレーム。ディスクに書くのはこの形（ADR-0005）。</summary>
public sealed record EncodedFrame(
    byte[] Bytes,
    int Width,
    int Height,
    TimeSpan Elapsed,
    string FileExtension) : ITimestamped;

/// <summary>符号化の条件（F-06）。</summary>
/// <param name="MaxLongEdgePixels">縮小後の長辺の上限。元が既に小さいときは拡大しない。</param>
/// <param name="JpegQuality">JPEG の品質（30〜100）。</param>
public sealed record FrameEncodeOptions(int MaxLongEdgePixels, int JpegQuality);

/// <summary>
/// 縮小して符号化する役。実装は外界（WIC）に触るので NutLens.Capture 側に置く（ADR-0002）。
/// </summary>
public interface IFrameEncoder
{
    EncodedFrame Encode(CapturedFrame frame, FrameEncodeOptions options);
}
