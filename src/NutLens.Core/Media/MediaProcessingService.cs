using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Analysis;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.Core.Media;

/// <summary>撮影の後に文字起こしと OCR を行う役（詳細設計 04 §6）。</summary>
public interface IMediaProcessingService
{
    Task<MediaProcessingResult> ProcessAsync(
        SessionLayout layout,
        IReadOnlyList<string> frameFiles,
        NutLensSettings settings,
        IProgress<MediaProcessingProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// <inheritdoc cref="IMediaProcessingService" />
///
/// <b>どちらが失敗しても、もう一方は続ける。</b>両方失敗しても解析は続く
/// （プロンプトには「ありません」と入る。ADR-0014）。
/// 補助情報が取れなかっただけで解析を止めない（概要設計 §10.1）。
/// </summary>
public sealed class MediaProcessingService : IMediaProcessingService
{
    /// <summary>利用者が開いて読むファイルは UTF-8（BOM つき）で書く。</summary>
    private static readonly Encoding TextEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private readonly ITranscriber transcriber;
    private readonly IOcrEngine ocrEngine;
    private readonly IFrameFileLister frameLister;
    private readonly MediaTextFormatter formatter = new();

    public MediaProcessingService(
        ITranscriber transcriber,
        IOcrEngine ocrEngine,
        IFrameFileLister frameLister)
    {
        ArgumentNullException.ThrowIfNull(transcriber);
        ArgumentNullException.ThrowIfNull(ocrEngine);
        ArgumentNullException.ThrowIfNull(frameLister);

        this.transcriber = transcriber;
        this.ocrEngine = ocrEngine;
        this.frameLister = frameLister;
    }

    public async Task<MediaProcessingResult> ProcessAsync(
        SessionLayout layout,
        IReadOnlyList<string> frameFiles,
        NutLensSettings settings,
        IProgress<MediaProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(frameFiles);
        ArgumentNullException.ThrowIfNull(settings);

        List<string> warnings = [];

        TranscriptionResult transcription = await this
            .TranscribeAsync(layout, settings, warnings, progress, cancellationToken)
            .ConfigureAwait(false);

        OcrResult ocr = await this
            .RecognizeAsync(layout, settings, warnings, progress, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProcessingProgress("後処理が終わりました。", 1.0));
        return new MediaProcessingResult(transcription, ocr, warnings);
    }

    private async Task<TranscriptionResult> TranscribeAsync(
        SessionLayout layout,
        NutLensSettings settings,
        List<string> warnings,
        IProgress<MediaProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!settings.Whisper.Enabled)
        {
            return TranscriptionResult.Skipped("設定で文字起こしが無効になっています。");
        }

        if (!File.Exists(layout.AudioFile))
        {
            // 録音しなかった／できなかった。警告は録音側が既に出しているので重ねない。
            return TranscriptionResult.Skipped("音声が記録されていません。");
        }

        progress?.Report(new MediaProcessingProgress("音声を文字に起こしています…", 0.0));

        TranscriptionResult result = await this.transcriber
            .TranscribeAsync(
                layout.AudioFile,
                settings.Whisper.ModelPath,
                new Progress<double>(ratio =>
                    progress?.Report(new MediaProcessingProgress("音声を文字に起こしています…", ratio * 0.7))),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            warnings.Add($"文字起こしを飛ばしました（{result.SkipReason}）。");
            return result;
        }

        string modelName = string.IsNullOrWhiteSpace(settings.Whisper.ModelPath)
            ? WhisperSettings.DefaultModelFileName
            : Path.GetFileName(settings.Whisper.ModelPath);

        TryWrite(layout.TranscriptFile, this.formatter.FormatTranscript(result, modelName));

        if (!result.HasText)
        {
            warnings.Add("音声から文字を起こせませんでした（無音か、認識できない音声でした）。");
        }

        return result;
    }

    private async Task<OcrResult> RecognizeAsync(
        SessionLayout layout,
        NutLensSettings settings,
        List<string> warnings,
        IProgress<MediaProcessingProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!settings.Ocr.Enabled)
        {
            return OcrResult.Skipped("設定で OCR が無効になっています。");
        }

        if (!this.ocrEngine.IsAvailable(out string? reason))
        {
            warnings.Add($"テロップの読み取りを飛ばしました（{reason}）。");
            return OcrResult.Skipped(reason ?? "OCR が利用できません。");
        }

        IReadOnlyList<FrameReference> frames = this.frameLister.List(layout);
        if (frames.Count == 0)
        {
            return OcrResult.Skipped("フレームがありません。");
        }

        progress?.Report(new MediaProcessingProgress("テロップを読み取っています…", 0.7));

        List<OcrTarget> targets = new(frames.Count);
        foreach (FrameReference frame in frames)
        {
            // FrameReference の相対パスは作業ディレクトリ基準。実体のパスに直す。
            targets.Add(new OcrTarget(
                Path.Combine(layout.Root, frame.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                frame.Elapsed));
        }

        OcrResult result = await this.ocrEngine
            .RecognizeAsync(targets, settings.Ocr.PreferredLanguage, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            warnings.Add($"テロップの読み取りを飛ばしました（{result.SkipReason}）。");
            return result;
        }

        TryWrite(layout.OcrFile, this.formatter.FormatOcr(result));

        if (!result.HasText)
        {
            warnings.Add("テロップ・字幕は読み取れませんでした。");
        }

        return result;
    }

    private static void TryWrite(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content, TextEncoding);
        }
        catch (IOException)
        {
            // 書けなくても解析は続ける。補助情報のために解析を止めない。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
