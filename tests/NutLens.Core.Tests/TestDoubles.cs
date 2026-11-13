using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Capture;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;

namespace NutLens.Core.Tests;

/// <summary>
/// テスト用のフレーム生成。モックライブラリは足さない（依存を増やさないため）。
///
/// 要点：<see cref="PatternFromHash"/> は「dHash がちょうど指定の 64 bit になる画像」を作る。
/// dHash は 9 × 8 の区画の平均輝度しか見ないので、区画を単色で塗れば平均が確定し、
/// 横隣との大小関係＝ハッシュのビットをこちらで決められる。
/// これにより「距離がちょうど 5 のフレーム」のような境界値テストが書ける。
/// </summary>
internal static class FrameFactory
{
    /// <summary>
    /// 区画どうしの明るさの差。
    /// 1 行あたり 24 段ぶん動くので、±8 では 0〜255 に収まらない。
    /// 歩いた結果が収まるよう、後から全体をずらす（<see cref="BuildRow"/>）。
    /// </summary>
    private const int Step = 8;

    /// <summary>ずらした後に収める範囲。±10 程度の増減で飽和しない幅を残す。</summary>
    private const int MinLuminance = 16;
    private const int MaxLuminance = 240;

    public static CapturedFrame Solid(byte b, byte g, byte r, double elapsedSeconds, int width = 200, int height = 192)
    {
        int stride = width * 4;
        byte[] bgra = new byte[stride * height];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = 255;
        }

        return new CapturedFrame(bgra, width, height, stride, TimeSpan.FromSeconds(elapsedSeconds));
    }

    /// <summary>seed ごとに十分に離れたハッシュを持つフレーム。</summary>
    public static CapturedFrame Pattern(int seed, double elapsedSeconds, int width = 200, int height = 192) =>
        PatternFromHash(ExpectedWords(seed), elapsedSeconds, width, height);

    /// <summary>seed に対応するビット列。画像を作らずに期待値だけ知りたいときに使う。</summary>
    public static ulong[] ExpectedWords(int seed)
    {
        ulong[] words = new ulong[DHash.WordCount];
        ulong state = (ulong)(uint)seed;

        for (int i = 0; i < words.Length; i++)
        {
            state = Mix(state + (ulong)i + 1);
            words[i] = state;
        }

        return words;
    }

    public static PerceptualHash ExpectedHash(int seed) => DHash.FromWords(ExpectedWords(seed));

    /// <summary>下位から <paramref name="count"/> ビットを反転させる。距離がちょうど count になる。</summary>
    public static ulong[] FlipBits(ReadOnlySpan<ulong> words, int count)
    {
        ulong[] flipped = words.ToArray();
        for (int i = 0; i < count; i++)
        {
            flipped[i / 64] ^= 1UL << (i % 64);
        }

        return flipped;
    }

    /// <summary>dHash がちょうど <paramref name="bits"/> になる灰色の市松画像を作る。</summary>
    public static CapturedFrame PatternFromHash(
        ReadOnlySpan<ulong> bits,
        double elapsedSeconds,
        int width = 200,
        int height = 192)
    {
        int stride = width * 4;
        byte[] bgra = new byte[stride * height];
        Span<int> row = stackalloc int[DHash.GridWidth];

        for (int gy = 0; gy < DHash.GridHeight; gy++)
        {
            (int y0, int y1) = SliceRange(height, gy, DHash.GridHeight);
            BuildRow(bits, gy, row);

            for (int gx = 0; gx < DHash.GridWidth; gx++)
            {
                (int x0, int x1) = SliceRange(width, gx, DHash.GridWidth);
                FillCell(bgra, stride, x0, x1, y0, y1, (byte)row[gx]);
            }
        }

        return new CapturedFrame(bgra, width, height, stride, TimeSpan.FromSeconds(elapsedSeconds));
    }

    /// <summary>
    /// 1 行ぶんの明るさを作る。
    /// まず 0 から始めて「bit が立っていれば下げる、立っていなければ上げる」と歩き、
    /// 最後に全体をずらして 16〜240 に収める。
    /// <b>歩いてからずらす</b>のは、隣との大小だけが意味を持つため。
    /// </summary>
    private static void BuildRow(ReadOnlySpan<ulong> bits, int gy, Span<int> row)
    {
        int value = 0;
        int min = 0;
        int max = 0;

        row[0] = 0;
        for (int gx = 0; gx < DHash.GridWidth - 1; gx++)
        {
            int index = (gy * (DHash.GridWidth - 1)) + gx;
            bool bit = ((bits[index / 64] >> (index % 64)) & 1UL) != 0UL;

            value += bit ? -Step : Step;
            row[gx + 1] = value;

            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        // 歩いた幅は最大 24 × 8 = 192。許される幅 224 に必ず収まる。
        int span = max - min;
        int offset = MinLuminance - min + ((MaxLuminance - MinLuminance - span) / 2);

        for (int gx = 0; gx < DHash.GridWidth; gx++)
        {
            row[gx] += offset;
        }
    }

    /// <summary>画素に微小なノイズを足す。動画の圧縮ノイズを模す。</summary>
    public static CapturedFrame WithNoise(CapturedFrame source, int amplitude, double elapsedSeconds)
    {
        byte[] bgra = (byte[])source.Bgra.Clone();
        for (int i = 0; i < bgra.Length; i += 4)
        {
            int delta = ((i / 4) % ((amplitude * 2) + 1)) - amplitude;
            bgra[i] = ClampByte(bgra[i] + delta);
            bgra[i + 1] = ClampByte(bgra[i + 1] + delta);
            bgra[i + 2] = ClampByte(bgra[i + 2] + delta);
        }

        return source with { Bgra = bgra, Elapsed = TimeSpan.FromSeconds(elapsedSeconds) };
    }

    private static void FillCell(byte[] bgra, int stride, int x0, int x1, int y0, int y1, byte luminance)
    {
        // 灰色（B = G = R）にすると、輝度の計算式がそのまま値になる。
        for (int y = y0; y < y1; y++)
        {
            int rowOffset = y * stride;
            for (int x = x0; x < x1; x++)
            {
                int p = rowOffset + (x * 4);
                bgra[p] = luminance;
                bgra[p + 1] = luminance;
                bgra[p + 2] = luminance;
                bgra[p + 3] = 255;
            }
        }
    }

    /// <summary><see cref="DHash"/> の区切り方と同じ。区画の平均が狙った値になるように揃える。</summary>
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

    /// <summary>splitmix64。seed が近くてもハッシュが十分に散るようにする。</summary>
    private static ulong Mix(ulong value)
    {
        ulong x = value + 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);
}

/// <summary>与えられたフレーム列をそのまま流す <see cref="IFrameSource"/>。</summary>
internal sealed class FakeFrameSource : IFrameSource
{
    private readonly IReadOnlyList<CapturedFrame> frames;
    private readonly Exception? throwAfterAll;

    public FakeFrameSource(IReadOnlyList<CapturedFrame> frames, Exception? throwAfterAll = null)
    {
        this.frames = frames;
        this.throwAfterAll = throwAfterAll;
    }

    public bool Disposed { get; private set; }

    public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (CapturedFrame frame in this.frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return frame;
            await Task.Yield();
        }

        if (this.throwAfterAll is not null)
        {
            throw this.throwAfterAll;
        }
    }

    public ValueTask DisposeAsync()
    {
        this.Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeFrameSourceFactory : IFrameSourceFactory
{
    private readonly IReadOnlyList<CapturedFrame> frames;
    private readonly Exception? throwAfterAll;

    public FakeFrameSourceFactory(IReadOnlyList<CapturedFrame> frames, Exception? throwAfterAll = null)
    {
        this.frames = frames;
        this.throwAfterAll = throwAfterAll;
    }

    public FakeFrameSource? Last { get; private set; }

    public TimeSpan? RequestedInterval { get; private set; }

    public IFrameSource Create(CaptureTarget target, TimeSpan interval)
    {
        this.RequestedInterval = interval;
        this.Last = new FakeFrameSource(this.frames, this.throwAfterAll);
        return this.Last;
    }
}

/// <summary>何も録らない録音器。音声デバイスが無い環境での挙動も、これで確かめられる。</summary>
internal sealed class FakeAudioRecorder : NutLens.Core.Media.IAudioRecorder
{
    private readonly bool canStart;

    public FakeAudioRecorder(bool canStart = true)
    {
        this.canStart = canStart;
        this.FailureReason = canStart ? null : "テスト用に録音できない設定です。";
    }

    public string? FailureReason { get; }

    public string? Destination { get; private set; }

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public bool TryStart(string destinationWavPath)
    {
        this.Destination = destinationWavPath;
        this.Started = this.canStart;
        return this.canStart;
    }

    public Task<long> StopAsync()
    {
        this.Stopped = true;
        return Task.FromResult(this.Started ? 1024L : 0L);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeAudioRecorderFactory : NutLens.Core.Media.IAudioRecorderFactory
{
    private readonly bool canStart;

    public FakeAudioRecorderFactory(bool canStart = true) => this.canStart = canStart;

    public FakeAudioRecorder? Last { get; private set; }

    public NutLens.Core.Media.IAudioRecorder Create()
    {
        this.Last = new FakeAudioRecorder(this.canStart);
        return this.Last;
    }
}

/// <summary>BGRA をそのまま詰め替えるだけの符号化器。WIC に触らない。</summary>
internal sealed class FakeFrameEncoder : IFrameEncoder
{
    public int EncodeCount { get; private set; }

    public EncodedFrame Encode(CapturedFrame frame, FrameEncodeOptions options)
    {
        this.EncodeCount++;
        return new EncodedFrame(frame.Bgra, frame.Width, frame.Height, frame.Elapsed, ".bin");
    }
}
