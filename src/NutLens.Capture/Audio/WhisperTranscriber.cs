using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Media;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using Whisper.net;

namespace NutLens.Capture.Audio;

/// <summary>
/// Whisper.net でプロセス内に文字起こしする（F-11 / ADR-0017）。
///
/// 外部プロセスを起動しないので「`claude` と同じ、どこにあるか問題」を抱えない。
/// モデルだけは同梱せず（D-04）、パスを設定で持つ。
///
/// <b>例外を外へ出さない。</b>文字起こしの失敗で解析を止めない（概要設計 §10.1）。
/// </summary>
public sealed class WhisperTranscriber : ITranscriber
{
    private readonly IAppDataPaths paths;

    public WhisperTranscriber(IAppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
    }

    /// <summary>設定で指定が無いときのモデルの場所（D-05：固定で書かない）。</summary>
    public string DefaultModelPath =>
        Path.Combine(this.paths.ModelsRoot, WhisperSettings.DefaultModelFileName);

    public async Task<TranscriptionResult> TranscribeAsync(
        string wavPath,
        string? modelPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        string model = string.IsNullOrWhiteSpace(modelPath) ? this.DefaultModelPath : modelPath;

        if (!File.Exists(model))
        {
            return TranscriptionResult.Skipped($"Whisper のモデルが見つかりません: {model}");
        }

        if (!File.Exists(wavPath))
        {
            return TranscriptionResult.Skipped("音声が記録されていません。");
        }

        try
        {
            return await this
                .RunAsync(wavPath, model, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or DllNotFoundException
            or BadImageFormatException
            or InvalidOperationException
            or ApplicationException)
        {
            return TranscriptionResult.Skipped($"文字起こしに失敗しました: {exception.Message}");
        }
    }

    private async Task<TranscriptionResult> RunAsync(
        string wavPath,
        string modelPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using WhisperFactory factory = WhisperFactory.FromPath(modelPath);

        // 言語は自動判定（F-11）。日英が混ざるフリップ動画も多い。
        await using WhisperProcessor processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .WithProgressHandler(value => progress?.Report(Math.Clamp(value / 100.0, 0.0, 1.0)))
            .Build();

        await using FileStream stream = File.OpenRead(wavPath);

        List<TranscriptSegment> segments = [];
        string? language = null;

        await foreach (SegmentData segment in processor
            .ProcessAsync(stream, cancellationToken)
            .ConfigureAwait(false))
        {
            language ??= segment.Language;

            string text = segment.Text?.Trim() ?? string.Empty;
            if (text.Length > 0)
            {
                segments.Add(new TranscriptSegment(segment.Start, segment.End, text));
            }
        }

        return new TranscriptionResult(true, language, segments, null);
    }
}
