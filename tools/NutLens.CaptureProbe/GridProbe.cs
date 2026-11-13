using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using NutLens.Core.Imaging;

namespace NutLens.CaptureProbe;

/// <summary>
/// 格子の細かさを変えたときの分離具合を測る実験。
///
/// 実際のフリップ動画をブラウザの窓ごと撮ると、画面の大半が動かない部分（タブ・サイドバー・
/// 動画自体の固定テロップ）で占められる。9 × 8 の格子では、動く部分に割り当たるマス目が
/// 少なすぎて、別の場面でも距離が 2〜7 しか出ない。
///
/// 格子を細かくすれば、動く部分に割り当たるビットが増える。
/// <b>本当に効くのか、ノイズも同じだけ増えるだけなのかを、推測せずに測る。</b>
/// </summary>
internal static class GridProbe
{
    private static readonly (int Width, int Height)[] Grids =
    [
        (9, 8),     // 現行（64 bit）
        (13, 12),   // 144 bit
        (17, 16),   // 256 bit
        (25, 24),   // 576 bit
    ];

    public static int Run(string framesDirectory)
    {
        if (!Directory.Exists(framesDirectory))
        {
            Console.Error.WriteLine($"フォルダが見つかりません: {framesDirectory}");
            return 5;
        }

        string[] files = Directory.GetFiles(framesDirectory, "*.jpg");
        Array.Sort(files, StringComparer.Ordinal);

        if (files.Length < 2)
        {
            Console.Error.WriteLine("フレームが足りません。");
            return 5;
        }

        List<CapturedFrame> frames = [];
        List<CapturedFrame> half = [];
        foreach (string file in files)
        {
            frames.Add(ImageFiles.Read(file));
            half.Add(ImageFiles.Read(file, 0.5));
        }

        Console.WriteLine($"フレーム {frames.Count} 枚");
        Console.WriteLine();
        Console.WriteLine("{0,-10} {1,6} {2,10} {3,10} {4,10} {5,12}", "格子", "bit数", "最小距離", "中央値", "縮小ノイズ", "分離比");
        Console.WriteLine(new string('-', 64));

        foreach ((int gridWidth, int gridHeight) in Grids)
        {
            int bits = (gridWidth - 1) * gridHeight;

            List<ulong[]> hashes = [];
            foreach (CapturedFrame frame in frames)
            {
                hashes.Add(Compute(frame, gridWidth, gridHeight));
            }

            // 別の場面どうしの距離（小さいほど区別しにくい）
            List<int> nearest = [];
            for (int i = 1; i < hashes.Count; i++)
            {
                int best = int.MaxValue;
                for (int j = 0; j < i; j++)
                {
                    best = Math.Min(best, Hamming(hashes[i], hashes[j]));
                }

                nearest.Add(best);
            }

            // 同じ画像を縮小したときのずれ（＝ノイズの目安）
            List<int> noise = [];
            for (int i = 0; i < frames.Count; i++)
            {
                noise.Add(Hamming(hashes[i], Compute(half[i], gridWidth, gridHeight)));
            }

            nearest.Sort();
            noise.Sort();

            int minSignal = nearest[0];
            int medianSignal = nearest[nearest.Count / 2];
            int maxNoise = noise[^1];

            Console.WriteLine(
                "{0,-10} {1,6} {2,10} {3,10} {4,10} {5,12}",
                $"{gridWidth}x{gridHeight}",
                bits,
                minSignal,
                medianSignal,
                maxNoise,
                maxNoise == 0 ? "∞" : $"{(double)minSignal / maxNoise:0.0}");
        }

        Console.WriteLine();
        Console.WriteLine("最小距離 : 別の場面どうしで最も近かった組（これを下回るしきい値なら全場面が残る）");
        Console.WriteLine("縮小ノイズ: 同じ画像を半分に縮めたときのずれの最大（しきい値はこれを上回る必要がある）");
        Console.WriteLine("分離比   : 最小距離 ÷ 縮小ノイズ。大きいほど、しきい値を置ける幅が広い");

        return 0;
    }

    /// <summary>任意の格子の dHash。NutLens.Core の DHash を一般化したもの（実験用）。</summary>
    private static ulong[] Compute(CapturedFrame frame, int gridWidth, int gridHeight)
    {
        int[] cells = new int[gridWidth * gridHeight];

        for (int gy = 0; gy < gridHeight; gy++)
        {
            (int y0, int y1) = Slice(frame.Height, gy, gridHeight);
            for (int gx = 0; gx < gridWidth; gx++)
            {
                (int x0, int x1) = Slice(frame.Width, gx, gridWidth);

                long sum = 0;
                long count = 0;
                for (int y = y0; y < y1; y++)
                {
                    int rowOffset = y * frame.Stride;
                    for (int x = x0; x < x1; x++)
                    {
                        int p = rowOffset + (x * 4);
                        sum += ((frame.Bgra[p + 2] * 299) + (frame.Bgra[p + 1] * 587) + (frame.Bgra[p] * 114)) / 1000;
                        count++;
                    }
                }

                cells[(gy * gridWidth) + gx] = (int)(sum / count);
            }
        }

        int bits = (gridWidth - 1) * gridHeight;
        ulong[] hash = new ulong[(bits + 63) / 64];
        int index = 0;

        for (int gy = 0; gy < gridHeight; gy++)
        {
            for (int gx = 0; gx < gridWidth - 1; gx++)
            {
                if (cells[(gy * gridWidth) + gx] > cells[(gy * gridWidth) + gx + 1])
                {
                    hash[index / 64] |= 1UL << (index % 64);
                }

                index++;
            }
        }

        return hash;
    }

    private static int Hamming(ulong[] left, ulong[] right)
    {
        int total = 0;
        for (int i = 0; i < left.Length; i++)
        {
            total += BitOperations.PopCount(left[i] ^ right[i]);
        }

        return total;
    }

    private static (int Start, int End) Slice(int length, int index, int divisions)
    {
        int start = (int)((long)length * index / divisions);
        int end = (int)((long)length * (index + 1) / divisions);

        if (start >= length)
        {
            start = length - 1;
        }

        if (end <= start)
        {
            end = start + 1;
        }

        return (start, Math.Min(end, length));
    }
}
