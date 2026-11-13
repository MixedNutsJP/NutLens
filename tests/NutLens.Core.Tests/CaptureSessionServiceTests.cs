using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Capture;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;
using NutLens.Core.Sampling;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using Xunit;

namespace NutLens.Core.Tests;

/// <summary>
/// 撮影の流れ全体のテスト。
/// 偽の IFrameSource / IFrameEncoder を差し込むので、グラフィックスデバイスが要らない（ADR-0002 決定 3）。
/// </summary>
public sealed class CaptureSessionServiceTests : IDisposable
{
    private static readonly CaptureTarget DummyTarget =
        new(CaptureTargetKind.Window, 1234, "テスト対象");

    private readonly string root;
    private readonly AppDataPaths paths;
    private readonly FileSystemSessionWorkspace workspace;
    private readonly FakeFrameEncoder encoder = new();

    public CaptureSessionServiceTests()
    {
        this.root = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.root);
        this.paths = new AppDataPaths(this.root);
        this.workspace = new FileSystemSessionWorkspace(this.paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private FakeAudioRecorderFactory audioFactory = new();

    private CaptureSessionService CreateService(IReadOnlyList<CapturedFrame> frames, out FakeFrameSourceFactory factory)
    {
        factory = new FakeFrameSourceFactory(frames);
        return new CaptureSessionService(
            factory,
            this.encoder,
            new EvenTimeFrameThinner(),
            this.workspace,
            new DHashDuplicateFrameFilterFactory(),
            this.audioFactory,
            TimeProvider.System);
    }

    private static CaptureSettings Settings(
        double duration = 60,
        int maxFrames = 20,
        int threshold = 5,
        int memoryLimit = 200) => new()
        {
            IntervalSeconds = 2.0,
            DurationSeconds = duration,
            DuplicateThreshold = threshold,
            MaxFrames = maxFrames,
            MaxFramesInMemoryLimit = memoryLimit,
        };

    [Fact]
    public async Task 重複を除いた枚数だけが保存される()
    {
        // 10 枚届くが、場面は 3 種類しかない（ループ再生を模した並び）。
        List<CapturedFrame> frames =
        [
            .. Enumerable.Range(0, 9).Select(i => FrameFactory.Pattern(seed: (i % 3) + 1, elapsedSeconds: i * 2.0)),
        ];

        CaptureSessionService service = this.CreateService(frames, out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings()),
            progress: null,
            CancellationToken.None);

        Assert.Equal(9, result.ReceivedFrames);
        Assert.Equal(3, result.AcceptedFrames);
        Assert.Equal(3, result.FrameFiles.Count);
        Assert.All(result.FrameFiles, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task 上限枚数を超えたら間引かれる()
    {
        List<CapturedFrame> frames =
        [
            .. Enumerable.Range(0, 50).Select(i => FrameFactory.Pattern(seed: i + 1, elapsedSeconds: i * 1.0)),
        ];

        CaptureSessionService service = this.CreateService(frames, out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings(duration: 300, maxFrames: 20)),
            progress: null,
            CancellationToken.None);

        Assert.Equal(20, result.AcceptedFrames);
        Assert.Equal(20, result.FrameFiles.Count);
    }

    [Fact]
    public async Task 撮影時間を過ぎたフレームは取り込まれない()
    {
        // 0,1,2,…,29 秒のフレームが届くが、撮影時間は 10 秒。
        List<CapturedFrame> frames =
        [
            .. Enumerable.Range(0, 30).Select(i => FrameFactory.Pattern(seed: i + 1, elapsedSeconds: i * 1.0)),
        ];

        CaptureSessionService service = this.CreateService(frames, out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings(duration: 10, maxFrames: 100)),
            progress: null,
            CancellationToken.None);

        // 0〜9 秒の 10 枚を採り、10 秒のフレームで打ち切る（受信は 11 枚）。
        Assert.Equal(11, result.ReceivedFrames);
        Assert.Equal(10, result.AcceptedFrames);
    }

    [Fact]
    public async Task 手動停止でもそれまでの分が保存される()
    {
        List<CapturedFrame> frames =
        [
            .. Enumerable.Range(0, 100).Select(i => FrameFactory.Pattern(seed: i + 1, elapsedSeconds: i * 0.5)),
        ];

        using CancellationTokenSource cts = new();
        FrameCountingProgress progress = new(cts, stopAfter: 5);

        CaptureSessionService service = this.CreateService(frames, out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings(duration: 300, maxFrames: 100)),
            progress,
            cts.Token);

        Assert.True(result.StoppedManually);
        Assert.InRange(result.AcceptedFrames, 1, 10);
        Assert.All(result.FrameFiles, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task メモリ上限を超えたらその場で間引かれる()
    {
        // 上限 20 枚（最小値）に対して 60 枚届く。途中で半分に落とす経路を通る。
        List<CapturedFrame> frames =
        [
            .. Enumerable.Range(0, 60).Select(i => FrameFactory.Pattern(seed: i + 1, elapsedSeconds: i * 1.0)),
        ];

        CaptureSessionService service = this.CreateService(frames, out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings(duration: 300, maxFrames: 10, memoryLimit: 20)),
            progress: null,
            CancellationToken.None);

        Assert.Equal(10, result.AcceptedFrames);

        // 撮影中に間引いた分は符号化を済ませてから捨てている。符号化回数は受信枚数と一致する。
        Assert.Equal(60, this.encoder.EncodeCount);
    }

    [Fact]
    public async Task フレームが一枚も来なくても結果を返す()
    {
        CaptureSessionService service = this.CreateService([], out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings()),
            progress: null,
            CancellationToken.None);

        Assert.Equal(0, result.ReceivedFrames);
        Assert.Empty(result.FrameFiles);
        Assert.True(Directory.Exists(result.FramesDirectory));
    }

    [Fact]
    public async Task 撮影を始めると前回の一時データが消える()
    {
        // 5.5「次の撮影を始めたとき」の削除。
        SessionLayout stale = this.workspace.Create(SessionId.New());
        Assert.True(Directory.Exists(stale.Root));

        CaptureSessionService service = this.CreateService(
            [FrameFactory.Pattern(seed: 1, elapsedSeconds: 0)],
            out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings()),
            progress: null,
            CancellationToken.None);

        Assert.False(Directory.Exists(stale.Root));
        Assert.Single(Directory.GetDirectories(this.paths.WorkRoot));
        Assert.Equal(result.SessionDirectory, Directory.GetDirectories(this.paths.WorkRoot)[0]);
    }

    [Fact]
    public async Task フレーム源は必ず破棄される()
    {
        CaptureSessionService service = this.CreateService(
            [FrameFactory.Pattern(seed: 1, elapsedSeconds: 0)],
            out FakeFrameSourceFactory factory);

        await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings()),
            progress: null,
            CancellationToken.None);

        Assert.NotNull(factory.Last);
        Assert.True(factory.Last.Disposed);
    }

    [Fact]
    public async Task 撮影間隔がフレーム源に渡される()
    {
        CaptureSessionService service = this.CreateService(
            [FrameFactory.Pattern(seed: 1, elapsedSeconds: 0)],
            out FakeFrameSourceFactory factory);

        await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings()),
            progress: null,
            CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(2), factory.RequestedInterval);
    }

    [Fact]
    public async Task 録音は撮影と同時に始まり同時に止まる()
    {
        // 要件 4 章：画面のフレームと、システム音声を同時に記録する（ADR-0016 決定 2）。
        CaptureSessionService service = this.CreateService(
            [FrameFactory.Pattern(seed: 1, elapsedSeconds: 0)],
            out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings()),
            progress: null,
            CancellationToken.None);

        FakeAudioRecorder recorder = Assert.IsType<FakeAudioRecorder>(this.audioFactory.Last);
        Assert.True(recorder.Started);
        Assert.True(recorder.Stopped);
        Assert.Equal(new SessionLayout(this.paths.WorkRoot, result.SessionId).AudioFile, recorder.Destination);
        Assert.Equal(1024L, result.AudioBytes);
        Assert.Null(result.AudioFailureReason);
    }

    [Fact]
    public async Task 録音を始められなくても撮影は続く()
    {
        // 補助情報が取れなかっただけで解析を止めない（概要設計 §10.1 / ADR-0016 決定 3）。
        this.audioFactory = new FakeAudioRecorderFactory(canStart: false);
        CaptureSessionService service = this.CreateService(
            [FrameFactory.Pattern(seed: 1, elapsedSeconds: 0)],
            out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings()),
            progress: null,
            CancellationToken.None);

        Assert.Single(result.FrameFiles);
        Assert.Equal(0L, result.AudioBytes);
        Assert.NotNull(result.AudioFailureReason);
    }

    [Fact]
    public async Task 録音を切っていれば録音器を作らない()
    {
        CaptureSessionService service = this.CreateService(
            [FrameFactory.Pattern(seed: 1, elapsedSeconds: 0)],
            out _);

        CaptureResult result = await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings() with { RecordAudio = false }),
            progress: null,
            CancellationToken.None);

        Assert.Null(this.audioFactory.Last);
        Assert.Equal(0L, result.AudioBytes);
        Assert.Null(result.AudioFailureReason);
    }

    [Fact]
    public async Task 進捗には受信枚数と採用枚数の両方が入る()
    {
        List<CapturedFrame> frames =
        [
            FrameFactory.Pattern(seed: 1, elapsedSeconds: 0),
            FrameFactory.Pattern(seed: 1, elapsedSeconds: 2),   // 重複
            FrameFactory.Pattern(seed: 2, elapsedSeconds: 4),
        ];

        CollectingProgress progress = new();
        CaptureSessionService service = this.CreateService(frames, out _);

        await service.RunAsync(
            new CaptureRequest(DummyTarget, Settings()),
            progress,
            CancellationToken.None);

        Assert.Equal(3, progress.Reports.Count);
        Assert.Equal(new CaptureProgress(1, 1, TimeSpan.Zero), progress.Reports[0]);
        Assert.Equal(new CaptureProgress(2, 1, TimeSpan.FromSeconds(2)), progress.Reports[1]);
        Assert.Equal(new CaptureProgress(3, 2, TimeSpan.FromSeconds(4)), progress.Reports[2]);
    }

    private sealed class CollectingProgress : IProgress<CaptureProgress>
    {
        public List<CaptureProgress> Reports { get; } = [];

        public void Report(CaptureProgress value) => this.Reports.Add(value);
    }

    /// <summary>n 件の進捗を受けたら撮影を止める。手動停止を模す。</summary>
    private sealed class FrameCountingProgress : IProgress<CaptureProgress>
    {
        private readonly CancellationTokenSource cts;
        private readonly int stopAfter;
        private int count;

        public FrameCountingProgress(CancellationTokenSource cts, int stopAfter)
        {
            this.cts = cts;
            this.stopAfter = stopAfter;
        }

        public void Report(CaptureProgress value)
        {
            this.count++;
            if (this.count >= this.stopAfter)
            {
                this.cts.Cancel();
            }
        }
    }
}
