using System;
using NutLens.Core.Imaging;

namespace NutLens.Core.Hashing;

/// <summary>
/// 知覚ハッシュ（dHash）。詳細設計 §4 / ADR-0004 / ADR-0015。
///
/// 25 × 24 の格子に分けて各区画の平均輝度を求め、横に隣り合う区画の大小を 576 bit に詰める。
///
/// 格子をこの細かさにしたのは、<b>実物のフリップ動画では画面の大半が動かない</b>ため
/// （ブラウザのタブやサイドバー、動画自体の固定テロップ）。
/// 9 × 8 では動く部分に割り当たるマス目が数個しかなく、
/// 別々の場面でも距離が 2〜7 しか出なかった（ADR-0015 背景）。
///
/// 縮小にボックス平均（区画内の全画素の平均）を使うのが要点で、
/// 最近傍の間引きだと動画の圧縮ノイズを拾った 1 画素でビットが反転し、
/// 同じ場面でもハッシュが安定しない。
/// </summary>
public static class DHash
{
    /// <summary>格子の横の数。隣との比較を 24 回行うため 25。</summary>
    public const int GridWidth = 25;

    /// <summary>格子の縦の数。</summary>
    public const int GridHeight = 24;

    /// <summary>ハッシュのビット数。(25 - 1) × 24 = 576。</summary>
    public const int BitCount = (GridWidth - 1) * GridHeight;

    /// <summary>ビット列を納める <see cref="ulong"/> の数。</summary>
    public const int WordCount = (BitCount + 63) / 64;

    public static PerceptualHash Compute(CapturedFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Compute(frame.Bgra, frame.Width, frame.Height, frame.Stride);
    }

    public static PerceptualHash Compute(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * 4);

        int required = ((height - 1) * stride) + (width * 4);
        if (bgra.Length < required)
        {
            throw new ArgumentException(
                $"画素データが足りません。必要 {required} バイト、実際 {bgra.Length} バイト。",
                nameof(bgra));
        }

        Span<int> cells = stackalloc int[GridWidth * GridHeight];

        for (int gy = 0; gy < GridHeight; gy++)
        {
            (int y0, int y1) = SliceRange(height, gy, GridHeight);

            for (int gx = 0; gx < GridWidth; gx++)
            {
                (int x0, int x1) = SliceRange(width, gx, GridWidth);

                long sum = 0;
                long count = 0;

                for (int y = y0; y < y1; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = x0; x < x1; x++)
                    {
                        int p = rowOffset + (x * 4);
                        int b = bgra[p];
                        int g = bgra[p + 1];
                        int r = bgra[p + 2];
                        sum += ((r * 299) + (g * 587) + (b * 114)) / 1000;
                        count++;
                    }
                }

                cells[(gy * GridWidth) + gx] = (int)(sum / count);
            }
        }

        ulong[] words = new ulong[WordCount];
        int index = 0;

        for (int gy = 0; gy < GridHeight; gy++)
        {
            for (int gx = 0; gx < GridWidth - 1; gx++)
            {
                if (cells[(gy * GridWidth) + gx] > cells[(gy * GridWidth) + gx + 1])
                {
                    words[index / 64] |= 1UL << (index % 64);
                }

                index++;
            }
        }

        return new PerceptualHash(words);
    }

    /// <summary>ビット列からハッシュを組み立てる。テストと確認ツールのための入口。</summary>
    public static PerceptualHash FromWords(ReadOnlySpan<ulong> words)
    {
        if (words.Length != WordCount)
        {
            throw new ArgumentException(
                $"ビット列の長さが違います。必要 {WordCount} 語、実際 {words.Length} 語。",
                nameof(words));
        }

        return new PerceptualHash(words.ToArray());
    }

    /// <summary>
    /// length を divisions 等分したときの index 番目の範囲 [start, end) を返す。
    /// length が divisions より小さくても空の範囲にならないようにしている
    /// （幅 25px 未満・高さ 24px 未満の切り出し矩形でも破綻させないため）。
    /// </summary>
    private static (int Start, int End) SliceRange(int length, int index, int divisions)
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

        if (end > length)
        {
            end = length;
        }

        return (start, end);
    }
}
