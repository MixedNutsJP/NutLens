using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NutLens.Core.Analysis;

/// <summary>
/// テンプレートの <c>{{名前}}</c> を差し替えてプロンプトを作る（F-21 / ADR-0014 決定 4）。
///
/// 条件分岐もループも持たない。差し込むのは 6 項目しかないので、単純置換で足りる。
/// テンプレート言語を保守し始めると、そちらの不具合を追うことになる。
/// </summary>
public sealed class PromptComposer : IPromptComposer
{
    public const string FramesPlaceholder = "{{frames}}";
    public const string TranscriptPlaceholder = "{{transcript}}";
    public const string OcrPlaceholder = "{{ocr}}";
    public const string WebDetectionPlaceholder = "{{web_detection}}";
    public const string NotesPlaceholder = "{{notes}}";
    public const string SchemaPlaceholder = "{{schema}}";

    public const string NoFrames = "（フレームなし）";
    public const string NoTranscript = "（音声の文字起こしはありません）";
    public const string NoOcr = "（OCR の結果はありません）";
    public const string NoWebDetection = "（外部の画像検索は使っていません）";
    public const string NoNotes = "（メモなし）";

    private readonly string template;

    public PromptComposer(string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        this.template = template;
    }

    public string Compose(PromptInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        // 未知のプレースホルダは触らない。打ち間違いが黙って消えると、
        // 生成されたプロンプトを見ても気づけない（ADR-0014 決定 4）。
        StringBuilder builder = new(this.template);
        builder.Replace(FramesPlaceholder, FormatFrames(inputs.Frames));
        builder.Replace(TranscriptPlaceholder, Fallback(inputs.Transcript, NoTranscript));
        builder.Replace(OcrPlaceholder, Fallback(inputs.OcrText, NoOcr));
        builder.Replace(WebDetectionPlaceholder, Fallback(inputs.WebDetectionJson, NoWebDetection));
        builder.Replace(NotesPlaceholder, FormatNotes(inputs.Notes));
        builder.Replace(SchemaPlaceholder, inputs.SchemaRelativePath);

        return builder.ToString();
    }

    private static string FormatFrames(IReadOnlyList<FrameReference> frames)
    {
        if (frames.Count == 0)
        {
            return NoFrames;
        }

        StringBuilder builder = new();
        foreach (FrameReference frame in frames)
        {
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "- `{0}` （撮影開始から {1:0.0} 秒）",
                frame.RelativePath,
                frame.Elapsed.TotalSeconds));
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatNotes(UserNotes notes)
    {
        if (notes.IsEmpty)
        {
            return NoNotes;
        }

        // 空の項目は行ごと出さない。「（未入力）」が並ぶとプロンプトが読みにくくなる。
        StringBuilder builder = new();
        AppendIfPresent(builder, "動画の URL", notes.SourceUrl);
        AppendIfPresent(builder, "投稿者", notes.AuthorName);
        AppendIfPresent(builder, "キャプション", notes.Caption);
        AppendIfPresent(builder, "メモ", notes.FreeText);

        return builder.ToString().TrimEnd();
    }

    private static void AppendIfPresent(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append("- ").Append(label).Append(": ").AppendLine(value.Trim());
        }
    }

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
