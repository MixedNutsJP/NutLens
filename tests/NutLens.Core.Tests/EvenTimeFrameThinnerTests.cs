using System;
using System.Collections.Generic;
using System.Linq;
using NutLens.Core.Imaging;
using NutLens.Core.Sampling;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class EvenTimeFrameThinnerTests
{
    private readonly EvenTimeFrameThinner thinner = new();

    /// <summary>間引きのテスト用。経過時間だけを持つ最小の型。</summary>
    private sealed record Stamp(TimeSpan Elapsed) : ITimestamped;

    private static List<Stamp> Series(params double[] seconds) =>
        [.. seconds.Select(s => new Stamp(TimeSpan.FromSeconds(s)))];

    private static List<Stamp> Uniform(int count, double step) =>
        [.. Enumerable.Range(0, count).Select(i => new Stamp(TimeSpan.FromSeconds(i * step)))];

    [Fact]
    public void 上限以下ならそのまま返す()
    {
        List<Stamp> frames = Uniform(5, 2.0);

        IReadOnlyList<Stamp> result = this.thinner.Thin(frames, maxCount: 20);

        Assert.Equal(5, result.Count);
        Assert.Same(frames, result);
    }

    [Fact]
    public void 上限を超えたら上限ちょうどまで減る()
    {
        List<Stamp> frames = Uniform(100, 0.5);

        IReadOnlyList<Stamp> result = this.thinner.Thin(frames, maxCount: 20);

        Assert.Equal(20, result.Count);
    }

    [Fact]
    public void 最初と最後は必ず残る()
    {
        List<Stamp> frames = Uniform(37, 1.5);

        IReadOnlyList<Stamp> result = this.thinner.Thin(frames, maxCount: 8);

        Assert.Equal(frames[0], result[0]);
        Assert.Equal(frames[^1], result[^1]);
    }

    [Fact]
    public void 結果は時間の昇順になる()
    {
        List<Stamp> frames = Uniform(50, 0.4);

        IReadOnlyList<Stamp> result = this.thinner.Thin(frames, maxCount: 11);

        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i].Elapsed > result[i - 1].Elapsed);
        }
    }

    [Fact]
    public void 等間隔のフレームなら間隔もほぼ均等になる()
    {
        // 100 枚を 0.5 秒刻み（0〜49.5 秒）から 11 枚選ぶと、理想の間隔は 4.95 秒。
        List<Stamp> frames = Uniform(100, 0.5);

        IReadOnlyList<Stamp> result = this.thinner.Thin(frames, maxCount: 11);

        double ideal = frames[^1].Elapsed.TotalSeconds / (result.Count - 1);
        for (int i = 1; i < result.Count; i++)
        {
            double gap = (result[i].Elapsed - result[i - 1].Elapsed).TotalSeconds;
            Assert.InRange(gap, ideal - 0.5, ideal + 0.5);
        }
    }

    [Fact]
    public void 時間が偏っていても端は保たれる()
    {
        // 前半に密集し、後半がまばらな列。手動停止や重複除去の偏りで起きうる。
        List<Stamp> frames = Series(0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 20, 40, 60);

        IReadOnlyList<Stamp> result = this.thinner.Thin(frames, maxCount: 4);

        Assert.Equal(4, result.Count);
        Assert.Equal(TimeSpan.Zero, result[0].Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(60), result[^1].Elapsed);
    }

    [Fact]
    public void 上限が１なら最初の一枚だけ返す()
    {
        List<Stamp> frames = Uniform(10, 1.0);

        IReadOnlyList<Stamp> result = this.thinner.Thin(frames, maxCount: 1);

        Assert.Single(result);
        Assert.Equal(frames[0], result[0]);
    }

    [Fact]
    public void 全フレームが同時刻でも上限ちょうど返す()
    {
        // 目標時刻がすべて同じになり、最近傍も同じ 1 件に潰れる。補充の経路が効いているか。
        List<Stamp> frames = [.. Enumerable.Repeat(new Stamp(TimeSpan.FromSeconds(3)), 30)];

        IReadOnlyList<Stamp> result = this.thinner.Thin(frames, maxCount: 7);

        Assert.Equal(7, result.Count);
    }

    [Fact]
    public void 上限が０以下なら例外になる()
    {
        List<Stamp> frames = Uniform(5, 1.0);

        Assert.Throws<ArgumentOutOfRangeException>(() => this.thinner.Thin(frames, maxCount: 0));
    }

    [Fact]
    public void 符号化済みフレームにも使える()
    {
        // 実運用では EncodedFrame に対して呼ぶ。
        List<EncodedFrame> frames =
        [
            .. Enumerable.Range(0, 30).Select(i =>
                new EncodedFrame([1, 2, 3], 100, 80, TimeSpan.FromSeconds(i), ".jpg")),
        ];

        IReadOnlyList<EncodedFrame> result = this.thinner.Thin(frames, maxCount: 5);

        Assert.Equal(5, result.Count);
        Assert.Equal(TimeSpan.Zero, result[0].Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(29), result[^1].Elapsed);
    }
}
