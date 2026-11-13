using System;
using System.Collections.Generic;
using NutLens.Core.Analysis;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class PromptComposerTests
{
    private const string Template = """
        # フレーム
        {{frames}}

        # 文字起こし
        {{transcript}}

        # OCR
        {{ocr}}

        # 外部の画像検索
        {{web_detection}}

        # メモ
        {{notes}}

        # 出力
        {{schema}}
        """;

    private static PromptInputs Inputs(
        IReadOnlyList<FrameReference>? frames = null,
        UserNotes? notes = null,
        string? transcript = null,
        string? ocr = null,
        string? webDetection = null) =>
        new(
            frames ?? [],
            notes ?? UserNotes.Empty,
            "schema/result.schema.json",
            transcript,
            ocr,
            webDetection);

    [Fact]
    public void フレームは相対パスと経過秒で並ぶ()
    {
        List<FrameReference> frames =
        [
            new FrameReference("frames/frame_001_t0000.0s.jpg", TimeSpan.Zero),
            new FrameReference("frames/frame_002_t0002.5s.jpg", TimeSpan.FromSeconds(2.5)),
        ];

        string prompt = new PromptComposer(Template).Compose(Inputs(frames));

        Assert.Contains("- `frames/frame_001_t0000.0s.jpg` （撮影開始から 0.0 秒）", prompt, StringComparison.Ordinal);
        Assert.Contains("- `frames/frame_002_t0002.5s.jpg` （撮影開始から 2.5 秒）", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void フレームが無ければその旨が入る()
    {
        string prompt = new PromptComposer(Template).Compose(Inputs());

        Assert.Contains(PromptComposer.NoFrames, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void 段階３の項目が無ければ無い旨の文言になる()
    {
        // 空欄のままにすると、Claude が「空＝無音」と「空＝取っていない」を区別できない。
        string prompt = new PromptComposer(Template).Compose(Inputs());

        Assert.Contains(PromptComposer.NoTranscript, prompt, StringComparison.Ordinal);
        Assert.Contains(PromptComposer.NoOcr, prompt, StringComparison.Ordinal);
        Assert.Contains(PromptComposer.NoWebDetection, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void 段階３の項目があれば差し込まれる()
    {
        string prompt = new PromptComposer(Template).Compose(
            Inputs(transcript: "ナレーションの本文", ocr: "テロップの文字", webDetection: "{\"a\":1}"));

        Assert.Contains("ナレーションの本文", prompt, StringComparison.Ordinal);
        Assert.Contains("テロップの文字", prompt, StringComparison.Ordinal);
        Assert.Contains("{\"a\":1}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(PromptComposer.NoTranscript, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void メモは入力された項目だけが行になる()
    {
        // 空の項目まで出すと「（未入力）」が並んでプロンプトが読みにくくなる。
        UserNotes notes = new(SourceUrl: "https://example.com/v/1", FreeText: "冒頭に雪山");

        string prompt = new PromptComposer(Template).Compose(Inputs(notes: notes));

        Assert.Contains("- 動画の URL: https://example.com/v/1", prompt, StringComparison.Ordinal);
        Assert.Contains("- メモ: 冒頭に雪山", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("投稿者", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("キャプション", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void メモが空なら無い旨の文言になる()
    {
        string prompt = new PromptComposer(Template).Compose(Inputs(notes: UserNotes.Empty));

        Assert.Contains(PromptComposer.NoNotes, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void 空白だけのメモは未入力として扱う()
    {
        UserNotes notes = new(SourceUrl: "   ", AuthorName: "", Caption: null, FreeText: "  ");

        Assert.True(notes.IsEmpty);
        Assert.Contains(PromptComposer.NoNotes, new PromptComposer(Template).Compose(Inputs(notes: notes)), StringComparison.Ordinal);
    }

    [Fact]
    public void スキーマの相対パスが入る()
    {
        string prompt = new PromptComposer(Template).Compose(Inputs());

        Assert.Contains("schema/result.schema.json", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void 未知のプレースホルダはそのまま残る()
    {
        // 利用者がテンプレートを編集して打ち間違えたとき、黙って消えると気づけない（ADR-0014 決定 4）。
        const string Typo = "本文 {{flames}} と {{transcrpt}} です。";

        string prompt = new PromptComposer(Typo).Compose(Inputs());

        Assert.Contains("{{flames}}", prompt, StringComparison.Ordinal);
        Assert.Contains("{{transcrpt}}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void 同じプレースホルダが複数あればすべて置換される()
    {
        string prompt = new PromptComposer("{{schema}} と {{schema}}").Compose(Inputs());

        Assert.Equal("schema/result.schema.json と schema/result.schema.json", prompt);
    }

    [Fact]
    public void 空のテンプレートは例外になる()
    {
        Assert.Throws<ArgumentException>(() => new PromptComposer("   "));
    }
}
