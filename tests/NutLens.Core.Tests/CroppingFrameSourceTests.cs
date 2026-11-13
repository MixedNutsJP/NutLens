using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Capture;
using NutLens.Core.Imaging;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class CroppingFrameSourceTests
{
    [Fact]
    public void 指定した矩形の寸法になる()
    {
        CapturedFrame source = FrameFactory.Pattern(seed: 1, elapsedSeconds: 0, width: 72, height: 64);

        CapturedFrame cropped = CroppingFrameSource.Crop(source, new PixelRect(8, 8, 32, 16));

        Assert.Equal(32, cropped.Width);
        Assert.Equal(16, cropped.Height);
        Assert.Equal(32 * 4, cropped.Stride);
        Assert.Equal(32 * 4 * 16, cropped.Bgra.Length);
    }

    [Fact]
    public void 切り出した画素が元の位置と一致する()
    {
        CapturedFrame source = FrameFactory.Pattern(seed: 5, elapsedSeconds: 0, width: 72, height: 64);
        PixelRect region = new(9, 8, 18, 16);

        CapturedFrame cropped = CroppingFrameSource.Crop(source, region);

        for (int y = 0; y < region.Height; y++)
        {
            for (int x = 0; x < region.Width; x++)
            {
                int sourceIndex = ((region.Y + y) * source.Stride) + ((region.X + x) * 4);
                int croppedIndex = (y * cropped.Stride) + (x * 4);

                Assert.Equal(source.Bgra[sourceIndex], cropped.Bgra[croppedIndex]);
                Assert.Equal(source.Bgra[sourceIndex + 1], cropped.Bgra[croppedIndex + 1]);
                Assert.Equal(source.Bgra[sourceIndex + 2], cropped.Bgra[croppedIndex + 2]);
            }
        }
    }

    [Fact]
    public void 経過時間は引き継がれる()
    {
        CapturedFrame source = FrameFactory.Pattern(seed: 1, elapsedSeconds: 12.5);

        CapturedFrame cropped = CroppingFrameSource.Crop(source, new PixelRect(4, 4, 16, 16));

        Assert.Equal(TimeSpan.FromSeconds(12.5), cropped.Elapsed);
    }

    [Fact]
    public void フレーム全体と同じ矩形なら元のフレームをそのまま返す()
    {
        // 無駄なコピーをしないこと。
        CapturedFrame source = FrameFactory.Pattern(seed: 2, elapsedSeconds: 0, width: 72, height: 64);

        CapturedFrame cropped = CroppingFrameSource.Crop(source, new PixelRect(0, 0, 72, 64));

        Assert.Same(source, cropped);
    }

    [Fact]
    public void 画面より大きい矩形はフレームに収まるよう切り詰められる()
    {
        // モニタの解像度が撮影中に変わると起きうる。範囲外アクセスで落ちてはいけない。
        CapturedFrame source = FrameFactory.Pattern(seed: 3, elapsedSeconds: 0, width: 72, height: 64);

        CapturedFrame cropped = CroppingFrameSource.Crop(source, new PixelRect(40, 40, 9999, 9999));

        Assert.Equal(32, cropped.Width);
        Assert.Equal(24, cropped.Height);
    }

    [Fact]
    public void 負の座標でも落ちない()
    {
        CapturedFrame source = FrameFactory.Pattern(seed: 4, elapsedSeconds: 0, width: 72, height: 64);

        CapturedFrame cropped = CroppingFrameSource.Crop(source, new PixelRect(-50, -50, 32, 32));

        Assert.Equal(32, cropped.Width);
        Assert.Equal(32, cropped.Height);
    }

    [Fact]
    public async Task 包んだフレーム源の全フレームが切り出される()
    {
        List<CapturedFrame> frames =
        [
            .. Enumerable.Range(0, 3).Select(i =>
                FrameFactory.Pattern(seed: i + 1, elapsedSeconds: i, width: 72, height: 64)),
        ];

        await using FakeFrameSource inner = new(frames);
        await using CroppingFrameSource cropping = new(inner, new PixelRect(4, 4, 24, 24));

        List<CapturedFrame> received = [];
        await foreach (CapturedFrame frame in cropping.ReadFramesAsync(CancellationToken.None))
        {
            received.Add(frame);
        }

        Assert.Equal(3, received.Count);
        Assert.All(received, frame =>
        {
            Assert.Equal(24, frame.Width);
            Assert.Equal(24, frame.Height);
        });
    }

    [Fact]
    public async Task 破棄すると包んだフレーム源も破棄される()
    {
        FakeFrameSource inner = new([]);
        CroppingFrameSource cropping = new(inner, new PixelRect(0, 0, 10, 10));

        await cropping.DisposeAsync();

        Assert.True(inner.Disposed);
    }

    [Theory]
    [InlineData(0, 0, 10, 10, 0, 0, 10, 10)]
    [InlineData(-5, -5, 10, 10, 0, 0, 10, 10)]
    [InlineData(60, 60, 50, 50, 60, 60, 12, 4)]
    [InlineData(0, 0, 0, 0, 0, 0, 1, 1)]
    public void 矩形のクランプ(
        int x,
        int y,
        int width,
        int height,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        PixelRect clamped = new PixelRect(x, y, width, height).ClampTo(72, 64);

        Assert.Equal(new PixelRect(expectedX, expectedY, expectedWidth, expectedHeight), clamped);
    }
}
