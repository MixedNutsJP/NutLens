using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NutLens.Core.Imaging;

namespace NutLens.CaptureProbe;

/// <summary>保存した JPEG を BGRA に戻す。確認ツールで dHash を測り直すために使う。</summary>
internal static class ImageFiles
{
    /// <param name="scale">
    /// 読み込んだ後に掛ける倍率。1 以外にすると縮小・拡大する。
    /// 撮影時は原寸で、保存は縮小後。dHash が縮小でどれだけ動くかを測るために使う。
    /// </param>
    public static CapturedFrame Read(string path, double scale = 1.0)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapSource decoded = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        if (Math.Abs(scale - 1.0) > 0.0001)
        {
            decoded = new TransformedBitmap(decoded, new ScaleTransform(scale, scale));
        }

        FormatConvertedBitmap converted = new(decoded, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        return new CapturedFrame(pixels, converted.PixelWidth, converted.PixelHeight, stride, TimeSpan.Zero);
    }
}
