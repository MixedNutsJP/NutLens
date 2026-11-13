using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NutLens.Core.Media;

/// <summary>
/// 撮影と同時に動く録音（F-11 / ADR-0016）。
/// 実装は WASAPI に触るので NutLens.Capture 側。
/// </summary>
public interface IAudioRecorder : IAsyncDisposable
{
    /// <summary>録音を始める。始められなければ false。<b>撮影は止めない。</b></summary>
    bool TryStart(string destinationWavPath);

    /// <summary>録音を止め、書けたバイト数を返す。始まっていなければ 0。</summary>
    Task<long> StopAsync();

    /// <summary>始められなかった理由。利用者に出す。</summary>
    string? FailureReason { get; }
}

/// <summary>録音を作る役。撮影ごとに作り直す（状態を持つため）。</summary>
public interface IAudioRecorderFactory
{
    IAudioRecorder Create();
}

/// <summary>文字起こしの 1 区間。</summary>
public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

/// <summary>文字起こしの結果。失敗しても例外にせず、理由を添えて返す。</summary>
public sealed record TranscriptionResult(
    bool Succeeded,
    string? Language,
    IReadOnlyList<TranscriptSegment> Segments,
    string? SkipReason)
{
    public static TranscriptionResult Skipped(string reason) =>
        new(false, null, [], reason);

    public bool HasText => this.Segments.Count > 0;
}

/// <summary>音声を文字にする役（F-11 / ADR-0017）。</summary>
public interface ITranscriber
{
    /// <summary>0.0〜1.0 の進捗を報告する。例外は外へ出さない。</summary>
    Task<TranscriptionResult> TranscribeAsync(
        string wavPath,
        string? modelPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

/// <summary>OCR で読んだ 1 行。</summary>
public sealed record OcrLine(TimeSpan Elapsed, string Text);

/// <summary>OCR の結果。</summary>
public sealed record OcrResult(
    bool Succeeded,
    string? Language,
    int FramesWithText,
    int FrameCount,
    IReadOnlyList<OcrLine> Lines,
    string? SkipReason)
{
    public static OcrResult Skipped(string reason) =>
        new(false, null, 0, 0, [], reason);

    public bool HasText => this.Lines.Count > 0;
}

/// <summary>OCR をかける対象のフレーム。</summary>
public sealed record OcrTarget(string Path, TimeSpan Elapsed);

/// <summary>テロップ・字幕を読む役（F-12 / ADR-0018）。</summary>
public interface IOcrEngine
{
    /// <summary>この環境で使えるか。使えないなら理由を返す。</summary>
    bool IsAvailable(out string? reason);

    Task<OcrResult> RecognizeAsync(
        IReadOnlyList<OcrTarget> frames,
        string? preferredLanguage,
        CancellationToken cancellationToken);
}

/// <summary>後処理の進み具合。</summary>
public sealed record MediaProcessingProgress(string Message, double Ratio);

/// <summary>後処理の結果。</summary>
public sealed record MediaProcessingResult(
    TranscriptionResult Transcription,
    OcrResult Ocr,
    IReadOnlyList<string> Warnings);
