using System;
using System.Globalization;
using System.Text;

namespace NutLens.Core.Media;

/// <summary>
/// 文字起こしと OCR の結果をファイルの中身にする（ADR-0017 決定 3 / ADR-0018 決定 4）。
///
/// どちらも<b>利用者が開いて読む</b>ものであり、同時に Claude への入力でもある。
/// 時刻を残すのは、フレームの経過秒と突き合わせられるようにするため。
/// </summary>
public sealed class MediaTextFormatter
{
    public const string NoTranscriptText = "（音声から文字を起こせませんでした）";
    public const string NoOcrText = "（テロップ・字幕は読み取れませんでした）";

    public string FormatTranscript(TranscriptionResult result, string? modelName)
    {
        ArgumentNullException.ThrowIfNull(result);

        StringBuilder builder = new();
        builder.Append("# 文字起こし（言語: ")
            .Append(result.Language ?? "不明")
            .Append(" / モデル: ")
            .Append(string.IsNullOrWhiteSpace(modelName) ? "不明" : modelName)
            .AppendLine("）")
            .AppendLine();

        if (!result.HasText)
        {
            builder.AppendLine(NoTranscriptText);
            return builder.ToString();
        }

        foreach (TranscriptSegment segment in result.Segments)
        {
            builder.Append('[')
                .Append(FormatTimestamp(segment.Start))
                .Append(" - ")
                .Append(FormatTimestamp(segment.End))
                .Append("] ")
                .AppendLine(segment.Text.Trim());
        }

        return builder.ToString();
    }

    public string FormatOcr(OcrResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        StringBuilder builder = new();
        builder.Append(CultureInfo.InvariantCulture, $"# テロップ・字幕の OCR（言語: {result.Language ?? "不明"}")
            .Append(CultureInfo.InvariantCulture, $" / フレーム {result.FrameCount} 枚中 {result.FramesWithText} 枚から検出）")
            .AppendLine()
            .AppendLine();

        if (!result.HasText)
        {
            builder.AppendLine(NoOcrText);
            return builder.ToString();
        }

        foreach (OcrLine line in result.Lines)
        {
            builder.Append(CultureInfo.InvariantCulture, $"[{line.Elapsed.TotalSeconds,6:0.0}s] ")
                .AppendLine(line.Text);
        }

        return builder.ToString();
    }

    /// <summary>mm:ss.f。1 時間を超える音声は想定しない（撮影時間の上限は 3600 秒）。</summary>
    internal static string FormatTimestamp(TimeSpan value) => string.Format(
        CultureInfo.InvariantCulture,
        "{0:00}:{1:00}.{2:0}",
        (int)value.TotalMinutes,
        value.Seconds,
        value.Milliseconds / 100);
}
