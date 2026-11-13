using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;
using NutLens.Core.Media;
using NutLens.Core.Sampling;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.Core.Capture;

/// <summary>
/// 1 回の撮影の流れ全体（詳細設計 §8）。
///
/// このクラスは抽象にしか依存しないので、偽の IFrameSource / IFrameEncoder を差し込めば
/// グラフィックスデバイス無しで流れ全体をテストできる（ADR-0002 決定 3）。
/// </summary>
public sealed class CaptureSessionService : ICaptureSessionService
{
    /// <summary>
    /// 撮影時間を過ぎてもフレームが来ないときに打ち切るまでの猶予。
    /// これが無いと、更新されないウィンドウを対象にしたとき永久に待つことになる。
    /// </summary>
    private static readonly TimeSpan WatchdogGrace = TimeSpan.FromSeconds(5);

    private readonly IFrameSourceFactory frameSourceFactory;
    private readonly IFrameEncoder encoder;
    private readonly IFrameThinner thinner;
    private readonly ISessionWorkspace workspace;
    private readonly IDuplicateFrameFilterFactory filterFactory;
    private readonly IAudioRecorderFactory audioRecorderFactory;
    private readonly TimeProvider timeProvider;

    public CaptureSessionService(
        IFrameSourceFactory frameSourceFactory,
        IFrameEncoder encoder,
        IFrameThinner thinner,
        ISessionWorkspace workspace,
        IDuplicateFrameFilterFactory filterFactory,
        IAudioRecorderFactory audioRecorderFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(frameSourceFactory);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(thinner);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(filterFactory);
        ArgumentNullException.ThrowIfNull(audioRecorderFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.frameSourceFactory = frameSourceFactory;
        this.encoder = encoder;
        this.thinner = thinner;
        this.workspace = workspace;
        this.filterFactory = filterFactory;
        this.audioRecorderFactory = audioRecorderFactory;
        this.timeProvider = timeProvider;
    }

    public async Task<CaptureResult> RunAsync(
        CaptureRequest request,
        IProgress<CaptureProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        CaptureSettings settings = request.Settings.Normalize();

        // 次の撮影を始めたときに前回の一時データを消す（5.5）。
        this.workspace.DeleteAll();

        SessionId sessionId = SessionId.New();
        SessionLayout layout = this.workspace.Create(sessionId);

        IDuplicateFrameFilter filter = this.filterFactory.Create(settings.DuplicateThreshold);
        FrameEncodeOptions encodeOptions = new(settings.LongEdgePixels, settings.JpegQuality);

        // 要件 4 章：画面のフレームと、システム音声を同時に記録する（ADR-0016 決定 2）。
        // 録音は audio.wav へ直接書く（ADR-0005 の「メモリに持つ」対象外）。
        IAudioRecorder? recorder = null;
        string? audioFailure = null;
        if (settings.RecordAudio)
        {
            recorder = this.audioRecorderFactory.Create();
            if (!recorder.TryStart(layout.AudioFile))
            {
                audioFailure = recorder.FailureReason;
                await recorder.DisposeAsync().ConfigureAwait(false);
                recorder = null;
            }
        }

        List<EncodedFrame> accepted = [];
        int received = 0;
        TimeSpan lastElapsed = TimeSpan.Zero;
        bool stoppedManually = false;

        using CancellationTokenSource watchdog = new(settings.Duration + WatchdogGrace, this.timeProvider);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, watchdog.Token);

        IFrameSource source = this.frameSourceFactory.Create(request.Target, settings.Interval);
        await using (source.ConfigureAwait(false))
        {
            try
            {
                await foreach (CapturedFrame frame in source
                    .ReadFramesAsync(linked.Token)
                    .ConfigureAwait(false))
                {
                    received++;
                    lastElapsed = frame.Elapsed;

                    // 撮影時間による終了（F-03）。壁時計ではなくフレーム自身の経過時間で判定するので、
                    // テストが時刻に依存せず決定的に動く。
                    if (frame.Elapsed >= settings.Duration)
                    {
                        break;
                    }

                    if (!filter.TryAccept(frame))
                    {
                        progress?.Report(new CaptureProgress(received, accepted.Count, lastElapsed));
                        continue;
                    }

                    accepted.Add(this.encoder.Encode(frame, encodeOptions));

                    if (accepted.Count > settings.MaxFramesInMemoryLimit)
                    {
                        // ADR-0005 決定 3：その場で半分に落とす。
                        // 均等性は少し崩れるが、最後の間引きで再度均等化される。
                        IReadOnlyList<EncodedFrame> reduced =
                            this.thinner.Thin(accepted, settings.MaxFramesInMemoryLimit / 2);
                        accepted = [.. reduced];
                    }

                    progress?.Report(new CaptureProgress(received, accepted.Count, lastElapsed));
                }
            }
            catch (OperationCanceledException)
            {
                // 手動停止（F-03）も、安全網の発火も、ここまでに採れた分で結果を返す。
                stoppedManually = cancellationToken.IsCancellationRequested;
            }
        }

        long audioBytes = 0;
        if (recorder is not null)
        {
            audioBytes = await recorder.StopAsync().ConfigureAwait(false);
            await recorder.DisposeAsync().ConfigureAwait(false);
        }

        IReadOnlyList<EncodedFrame> kept = this.thinner.Thin(accepted, settings.MaxFrames);

        // ここで初めてディスクに書く（ADR-0005）。
        IReadOnlyList<string> files = this.workspace.WriteFrames(layout, kept);

        return new CaptureResult(
            sessionId,
            layout.Root,
            layout.FramesDirectory,
            files,
            received,
            kept.Count,
            lastElapsed,
            stoppedManually,
            audioBytes,
            audioFailure);
    }
}
