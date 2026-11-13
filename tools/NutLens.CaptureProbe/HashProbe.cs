using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NutLens.Core.Capture;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;

namespace NutLens.CaptureProbe;

/// <summary>
/// 保存済みのフレームについて dHash と距離を測る。
///
/// <b>重複しきい値は推測で決めない。</b>実際のフリップ動画を撮って、ここで測ってから決める
/// （ADR-0004 の宿題）。切り出し範囲を変えたときの効き方も比べられる。
/// </summary>
internal static class HashProbe
{
    public static int Run(string framesDirectory, PixelRect? crop)
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

        if (crop is PixelRect region)
        {
            Console.WriteLine($"切り出し: X={region.X} Y={region.Y} {region.Width} x {region.Height}");
        }
        else
        {
            Console.WriteLine("切り出し: なし（フレーム全体）");
        }

        Console.WriteLine();

        List<PerceptualHash> hashes = [];
        List<string> names = [];

        foreach (string file in files)
        {
            CapturedFrame frame = ImageFiles.Read(file);
            if (crop is PixelRect r)
            {
                frame = CroppingFrameSource.Crop(frame, r);
            }

            PerceptualHash hash = DHash.Compute(frame);

            int nearest = -1;
            int nearestIndex = -1;
            for (int i = 0; i < hashes.Count; i++)
            {
                int distance = hash.DistanceTo(hashes[i]);
                if (nearest < 0 || distance < nearest)
                {
                    nearest = distance;
                    nearestIndex = i;
                }
            }

            Console.WriteLine(
                "{0,-28} {1,4}x{2,-4} 最小距離={3,3} ({4})",
                Path.GetFileName(file),
                frame.Width,
                frame.Height,
                nearest < 0 ? "-" : nearest.ToString(CultureInfo.InvariantCulture),
                nearestIndex < 0 ? "-" : names[nearestIndex]);

            hashes.Add(hash);
            names.Add(Path.GetFileNameWithoutExtension(file));
        }

        PrintClosePairs(files, hashes, maxDistance: 24);
        PrintThresholdTable(hashes);
        return 0;
    }

    /// <summary>
    /// 近い組の一覧。
    /// <b>ループ再生の繰り返しと「別の場面がたまたま近い」組を見分けるため</b>に、
    /// 距離だけでなく撮影時刻の差も出す。
    /// 時刻差が動画の長さの倍数に近ければ、ループで戻ってきた同じ場面。
    /// </summary>
    private static void PrintClosePairs(string[] files, List<PerceptualHash> hashes, int maxDistance)
    {
        Console.WriteLine();
        Console.WriteLine($"--- 距離 {maxDistance} 以下の組（時刻差つき）---");
        Console.WriteLine("{0,5}  {1,-26} {2,-26} {3,8}", "距離", "一方", "他方", "時刻差(秒)");

        List<(int Distance, int Left, int Right)> pairs = [];
        for (int i = 0; i < hashes.Count; i++)
        {
            for (int j = i + 1; j < hashes.Count; j++)
            {
                int distance = hashes[i].DistanceTo(hashes[j]);
                if (distance <= maxDistance)
                {
                    pairs.Add((distance, i, j));
                }
            }
        }

        pairs.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        foreach ((int distance, int left, int right) in pairs)
        {
            double gap = ElapsedOf(files[right]) - ElapsedOf(files[left]);
            Console.WriteLine(
                "{0,5}  {1,-26} {2,-26} {3,8:0.0}",
                distance,
                Path.GetFileNameWithoutExtension(files[left]),
                Path.GetFileNameWithoutExtension(files[right]),
                gap);
        }

        Console.WriteLine($"（{pairs.Count} 組）");
    }

    /// <summary>ファイル名から経過秒を読む。書式は frame_001_t0002.0s.jpg。</summary>
    private static double ElapsedOf(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int start = name.IndexOf("_t", StringComparison.Ordinal);
        if (start < 0)
        {
            return 0;
        }

        start += 2;
        int end = name.IndexOf('s', start);
        return end > start
            && double.TryParse(
                name.AsSpan(start, end - start),
                System.Globalization.NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double seconds)
            ? seconds
            : 0;
    }

    /// <summary>総当たりの距離。どの場面同士が近いかを見るため。</summary>
    private static void PrintMatrix(string[] files, List<PerceptualHash> hashes)
    {
        Console.WriteLine();
        Console.WriteLine("--- 総当たりの距離 ---");
        Console.Write("      ");
        for (int i = 0; i < files.Length; i++)
        {
            Console.Write("{0,4}", i + 1);
        }

        Console.WriteLine();

        for (int i = 0; i < hashes.Count; i++)
        {
            Console.Write("{0,4}: ", i + 1);
            for (int j = 0; j < hashes.Count; j++)
            {
                Console.Write(i == j ? "   ." : $"{hashes[i].DistanceTo(hashes[j]),4}");
            }

            Console.WriteLine();
        }
    }

    /// <summary>しきい値を変えると何枚残るか。既定値を決め直すための材料。</summary>
    private static void PrintThresholdTable(List<PerceptualHash> hashes)
    {
        Console.WriteLine();
        Console.WriteLine("--- しきい値ごとの採用枚数（受理済み全件と比較。ADR-0004 決定 2）---");
        Console.WriteLine("しきい値 : 採用枚数");

        for (int threshold = 0; threshold <= DHashDuplicateFrameFilter.MaxThreshold; threshold += 2)
        {
            List<PerceptualHash> accepted = [];
            foreach (PerceptualHash hash in hashes)
            {
                bool duplicate = false;
                foreach (PerceptualHash other in accepted)
                {
                    if (hash.DistanceTo(other) <= threshold)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    accepted.Add(hash);
                }
            }

            Console.WriteLine("{0,8} : {1,2} / {2}", threshold, accepted.Count, hashes.Count);
        }
    }
}
