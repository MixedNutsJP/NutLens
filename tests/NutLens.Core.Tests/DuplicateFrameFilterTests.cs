using System;
using System.Collections.Generic;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;
using NutLens.Core.Settings;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class DuplicateFrameFilterTests
{
    [Fact]
    public void ループ再生で同じ場面が戻ってきても採用されない()
    {
        // これがこのクラスの存在理由（ADR-0004）。
        // A,B,C,A,B,C と届いたとき、直前のフレームとだけ比べる実装では 2 周目が全部通ってしまう。
        DHashDuplicateFrameFilter filter = new(threshold: 12);

        List<CapturedFrame> loop =
        [
            FrameFactory.Pattern(seed: 1, elapsedSeconds: 0),
            FrameFactory.Pattern(seed: 2, elapsedSeconds: 2),
            FrameFactory.Pattern(seed: 3, elapsedSeconds: 4),
            FrameFactory.Pattern(seed: 1, elapsedSeconds: 6),
            FrameFactory.Pattern(seed: 2, elapsedSeconds: 8),
            FrameFactory.Pattern(seed: 3, elapsedSeconds: 10),
        ];

        int accepted = 0;
        foreach (CapturedFrame frame in loop)
        {
            if (filter.TryAccept(frame))
            {
                accepted++;
            }
        }

        Assert.Equal(3, accepted);
        Assert.Equal(3, filter.AcceptedCount);
    }

    [Fact]
    public void 連続する同一フレームは最初の一枚だけ採用される()
    {
        DHashDuplicateFrameFilter filter = new(threshold: 12);

        Assert.True(filter.TryAccept(FrameFactory.Pattern(seed: 9, elapsedSeconds: 0)));
        Assert.False(filter.TryAccept(FrameFactory.Pattern(seed: 9, elapsedSeconds: 2)));
        Assert.False(filter.TryAccept(FrameFactory.Pattern(seed: 9, elapsedSeconds: 4)));
        Assert.Equal(1, filter.AcceptedCount);
    }

    [Fact]
    public void しきい値ちょうどの距離は棄却し_一つ離れると採用する()
    {
        // 境界値。「<=」で棄却している（詳細設計 §4.3）ことを固定する。
        ulong[] baseBits = FrameFactory.ExpectedWords(seed: 4);

        DHashDuplicateFrameFilter filter = new(threshold: 12);
        Assert.True(filter.TryAccept(FrameFactory.PatternFromHash(baseBits, 0)));

        Assert.False(filter.TryAccept(FrameFactory.PatternFromHash(FrameFactory.FlipBits(baseBits, 12), 2)));
        Assert.True(filter.TryAccept(FrameFactory.PatternFromHash(FrameFactory.FlipBits(baseBits, 13), 4)));
    }

    [Fact]
    public void しきい値０なら一ビット違うだけで採用される()
    {
        DHashDuplicateFrameFilter filter = new(threshold: 0);
        ulong[] baseBits = FrameFactory.ExpectedWords(seed: 4);

        Assert.True(filter.TryAccept(FrameFactory.PatternFromHash(baseBits, 0)));
        Assert.True(filter.TryAccept(FrameFactory.PatternFromHash(FrameFactory.FlipBits(baseBits, 1), 2)));
        Assert.False(filter.TryAccept(FrameFactory.PatternFromHash(baseBits, 4)));
    }

    [Fact]
    public void 既定のしきい値は１２()
    {
        // ADR-0015 決定 2。縮小によるずれの実測 5 と、別の場面どうしの最小距離 23 のほぼ中間。
        Assert.Equal(12, DHashDuplicateFrameFilter.DefaultThreshold);
        Assert.Equal(12, new CaptureSettings().DuplicateThreshold);
    }

    [Fact]
    public void Reset_で受理済みの記録が消える()
    {
        // フィルタを撮影ごとに作り直さず使い回す場合に備えた経路。
        DHashDuplicateFrameFilter filter = new(threshold: 12);

        Assert.True(filter.TryAccept(FrameFactory.Pattern(seed: 6, elapsedSeconds: 0)));
        Assert.False(filter.TryAccept(FrameFactory.Pattern(seed: 6, elapsedSeconds: 1)));

        filter.Reset();

        Assert.Equal(0, filter.AcceptedCount);
        Assert.True(filter.TryAccept(FrameFactory.Pattern(seed: 6, elapsedSeconds: 2)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65)]
    public void しきい値が範囲外なら例外になる(int threshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DHashDuplicateFrameFilter(threshold));
    }

    [Fact]
    public void ファクトリはしきい値を引き継いだフィルタを作る()
    {
        DHashDuplicateFrameFilterFactory factory = new();

        IDuplicateFrameFilter filter = factory.Create(threshold: 7);

        Assert.Equal(7, filter.Threshold);
        Assert.Equal(0, filter.AcceptedCount);
    }
}
