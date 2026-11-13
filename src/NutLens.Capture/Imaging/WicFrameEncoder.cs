using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NutLens.Core.Imaging;

namespace NutLens.Capture.Imaging;

/// <summary>
/// 縮小して JPEG に符号化する（F-06 / ADR-0008）。
///
/// 中身は Windows Imaging Component（WPF の Imaging 経由）。
/// System.Drawing.Common も SkiaSharp も入れずに済ませるための選択。
/// </summary>
public sealed class WicFrameEncoder : IFrameEncoder
{
    public const string FileExtension = ".jpg";

    public EncodedFrame Encode(CapturedFrame frame, FrameEncodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(options);

        BitmapSource source = BitmapSource.Create(
            frame.Width,
            frame.Height,
            dpiX: 96,
            dpiY: 96,
            PixelFormats.Bgra32,
            palette: null,
            frame.Bgra,
            frame.Stride);

        BitmapSource scaled = Scale(source, options.MaxLongEdgePixels);

        JpegBitmapEncoder encoder = new() { QualityLevel = options.JpegQuality };
        encoder.Frames.Add(BitmapFrame.Create(scaled));

        using MemoryStream stream = new();
        encoder.Save(stream);

        return new EncodedFrame(
            stream.ToArray(),
            scaled.PixelWidth,
            scaled.PixelHeight,
            frame.Elapsed,
            FileExtension);
    }

    private static BitmapSource Scale(BitmapSource source, int maxLongEdge)
    {
        int longEdge = Math.Max(source.PixelWidth, source.PixelHeight);

        // 元が既に小さいときは引き伸ばさない。
        if (longEdge <= maxLongEdge)
        {
            return Freeze(source);
        }

        double ratio = (double)maxLongEdge / longEdge;
        return Freeze(new TransformedBitmap(Freeze(source), new ScaleTransform(ratio, ratio)));
    }

    /// <summary>
    /// WPF の画像オブジェクトはスレッド親和性を持つ。
    /// 符号化はバックグラウンドスレッドで走るので、凍結しないと
    /// UI スレッド以外から触った時点で InvalidOperationException になる（ADR-0008 決定 4）。
    /// </summary>
    private static BitmapSource Freeze(BitmapSource source)
    {
        if (source.CanFreeze && !source.IsFrozen)
        {
            source.Freeze();
        }

        return source;
    }
}
