using System;
using System.Collections.Generic;
using System.IO;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;

namespace NutLens.CaptureProbe;

/// <summary>
/// 縮小すると dHash がどれだけ動くかを測る。
///
/// 撮影時の重複判定は<b>原寸の BGRA</b> に対して行い、保存するのは縮小後の JPEG。
/// 両者でハッシュが食い違うなら、「保存されたフレームで測った距離」は
/// 「撮影時に効いていた距離」と別物ということになる。確かめずに結論を出さない。
/// </summary>
internal static class ScaleProbe
{
    private static readonly double[] Scales = [1.0, 0.5, 0.25, 0.125];

    public static int Run(string framesDirectory)
    {
        if (!Directory.Exists(framesDirectory))
        {
            Console.Error.WriteLine($"フォルダが見つかりません: {framesDirectory}");
            return 5;
        }

        string[] files = Directory.GetFiles(framesDirectory, "*.jpg");
        Array.Sort(files, StringComparer.Ordinal);

        if (files.Length == 0)
        {
            Console.Error.WriteLine("JPEG がありません。");
            return 5;
        }

        Console.WriteLine("--- 同じ画像を縮小したときの、原寸ハッシュとの距離 ---");
        Console.Write("{0,-28}", "ファイル");
        foreach (double scale in Scales)
        {
            Console.Write("{0,8}", $"x{scale}");
        }

        Console.WriteLine();

        List<int> worst = [];
        foreach (string file in files)
        {
            Console.Write("{0,-28}", Path.GetFileName(file));

            PerceptualHash baseline = default;
            for (int i = 0; i < Scales.Length; i++)
            {
                CapturedFrame frame = ImageFiles.Read(file, Scales[i]);
                PerceptualHash hash = DHash.Compute(frame);

                if (i == 0)
                {
                    baseline = hash;
                    Console.Write("{0,8}", 0);
                    continue;
                }

                int distance = baseline.DistanceTo(hash);
                Console.Write("{0,8}", distance);

                if (i == 1)
                {
                    worst.Add(distance);
                }
            }

            Console.WriteLine();
        }

        worst.Sort();
        Console.WriteLine();
        Console.WriteLine($"半分に縮小したときのずれ: 中央値 {worst[worst.Count / 2]} / 最大 {worst[^1]}");
        Console.WriteLine();
        Console.WriteLine("ずれが重複しきい値（既定 5）と同じ桁なら、");
        Console.WriteLine("「保存後のフレームで測った距離」は「撮影時に効いた距離」と別物として読むこと。");

        return 0;
    }
}
