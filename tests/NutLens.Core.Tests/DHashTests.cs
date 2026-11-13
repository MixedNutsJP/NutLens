using System;
using System.Linq;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class DHashTests
{
    [Fact]
    public void ビット数は５７６()
    {
        // ADR-0015：9 × 8（64 bit）では、実物のフリップ動画で別々の場面が distance 2 までしか離れなかった。
        Assert.Equal(576, DHash.BitCount);
        Assert.Equal(9, DHash.WordCount);
        Assert.Equal(576, DHash.Compute(FrameFactory.Pattern(1, 0)).Words.Length * 64);
    }

    public static TheoryData<ulong[]> BitPatterns()
    {
        ulong[] allZero = new ulong[DHash.WordCount];
        ulong[] allOne = Enumerable.Repeat(ulong.MaxValue, DHash.WordCount).ToArray();
        ulong[] mixed = Enumerable
            .Range(0, DHash.WordCount)
            .Select(static i => 0x0123456789ABCDEFUL + (ulong)i)
            .ToArray();

        return new TheoryData<ulong[]>
        {
            allZero,
            allOne,
            mixed,
            FrameFactory.ExpectedWords(12345),
        };
    }

    [Theory]
    [MemberData(nameof(BitPatterns))]
    public void 指定したビット列どおりのハッシュになる(ulong[] bits)
    {
        // 区画を単色で塗ればボックス平均の結果が確定するので、
        // 「どの 576 bit が出るか」まで決め打ちで確かめられる。
        CapturedFrame frame = FrameFactory.PatternFromHash(bits, elapsedSeconds: 0);

        Assert.Equal(DHash.FromWords(bits), DHash.Compute(frame));
    }

    [Fact]
    public void 同じ画像なら同じハッシュになる()
    {
        CapturedFrame first = FrameFactory.Pattern(seed: 3, elapsedSeconds: 0);
        CapturedFrame second = FrameFactory.Pattern(seed: 3, elapsedSeconds: 10);

        Assert.Equal(DHash.Compute(first), DHash.Compute(second));
    }

    [Fact]
    public void 全体の明るさを一律に上げてもハッシュは変わらない()
    {
        // dHash は「隣との大小」だけを見るので、全画素に同じ値を足しても順序は変わらない。
        // フェードイン・フェードアウトの多いフリップ動画で、同じ場面が別物として残らないために重要。
        CapturedFrame original = FrameFactory.Pattern(seed: 5, elapsedSeconds: 0);
        byte[] brighter = new byte[original.Bgra.Length];
        for (int i = 0; i < original.Bgra.Length; i++)
        {
            brighter[i] = (byte)Math.Min(255, original.Bgra[i] + 10);
        }

        CapturedFrame lifted = original with { Bgra = brighter };

        Assert.Equal(DHash.Compute(original), DHash.Compute(lifted));
    }

    [Fact]
    public void 別の画像なら距離が大きい()
    {
        PerceptualHash a = DHash.Compute(FrameFactory.Pattern(seed: 1, elapsedSeconds: 0));
        PerceptualHash b = DHash.Compute(FrameFactory.Pattern(seed: 42, elapsedSeconds: 0));

        Assert.True(a.DistanceTo(b) > 150, $"距離が小さすぎる: {a.DistanceTo(b)}");
    }

    [Fact]
    public void 微小なノイズではハッシュが動かない()
    {
        // ボックス平均で縮小しているので、画素単位のノイズでビットが反転しない（ADR-0004 決定 1）。
        CapturedFrame original = FrameFactory.Pattern(seed: 7, elapsedSeconds: 0, width: 400, height: 384);
        CapturedFrame noisy = FrameFactory.WithNoise(original, amplitude: 2, elapsedSeconds: 0);

        Assert.True(
            DHash.Compute(original).DistanceTo(DHash.Compute(noisy)) <= 4,
            "ノイズでハッシュが動きすぎている");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 3)]
    [InlineData(25, 24)]
    [InlineData(24, 25)]
    public void 格子より小さい画像でも例外にならない(int width, int height)
    {
        // 「画面の矩形」で極端に小さい領域を選ばれても落ちないこと。
        CapturedFrame frame = FrameFactory.Pattern(seed: 2, elapsedSeconds: 0, width: width, height: height);

        PerceptualHash hash = DHash.Compute(frame);

        Assert.Equal(DHash.WordCount, hash.Words.Length);
    }

    [Fact]
    public void ハミング距離は対称で自分自身とは０()
    {
        PerceptualHash a = DHash.Compute(FrameFactory.Pattern(seed: 11, elapsedSeconds: 0));
        PerceptualHash b = DHash.Compute(FrameFactory.Pattern(seed: 12, elapsedSeconds: 0));

        Assert.Equal(0, a.DistanceTo(a));
        Assert.Equal(a.DistanceTo(b), b.DistanceTo(a));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(65)]
    [InlineData(200)]
    public void 指定した数だけビットを反転すると距離もその数になる(int count)
    {
        // 語をまたぐ反転（65 ビット以上）でも数が合うこと。
        ulong[] bits = FrameFactory.ExpectedWords(seed: 20);

        CapturedFrame original = FrameFactory.PatternFromHash(bits, elapsedSeconds: 0);
        CapturedFrame flipped = FrameFactory.PatternFromHash(FrameFactory.FlipBits(bits, count), elapsedSeconds: 0);

        Assert.Equal(count, DHash.Compute(original).DistanceTo(DHash.Compute(flipped)));
    }

    [Fact]
    public void 単色の画像はすべてのビットが０になる()
    {
        // 隣との大小が常に「等しい」ので、どのビットも立たない。
        CapturedFrame frame = FrameFactory.Solid(10, 20, 30, elapsedSeconds: 0);

        Assert.Equal(DHash.FromWords(new ulong[DHash.WordCount]), DHash.Compute(frame));
    }

    [Fact]
    public void 画素データが足りなければ例外になる()
    {
        byte[] tooSmall = new byte[16];

        Assert.Throws<ArgumentException>(() => DHash.Compute(tooSmall, width: 64, height: 48, stride: 256));
    }

    [Fact]
    public void ビット数の違うハッシュは比較できない()
    {
        PerceptualHash valid = DHash.Compute(FrameFactory.Pattern(1, 0));

        Assert.Throws<ArgumentException>(() => valid.DistanceTo(default));
    }

    [Fact]
    public void 十六進の文字列になる()
    {
        // ログと確認ツールで読むためのもの。
        PerceptualHash hash = DHash.FromWords(new ulong[DHash.WordCount]);

        Assert.Equal(new string('0', DHash.WordCount * 16), hash.ToString());
    }
}
