using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Analysis;
using NutLens.Core.Media;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class OcrTextCollectorTests
{
    [Fact]
    public void 同じ文字列は一度だけ残る()
    {
        // ショート動画はループ再生され、テロップは数秒出しっぱなしになる（ADR-0018 決定 3）。
        OcrTextCollector collector = new();

        Assert.True(collector.TryAdd(TimeSpan.Zero, "タクシーに乗ったら"));
        Assert.False(collector.TryAdd(TimeSpan.FromSeconds(2), "タクシーに乗ったら"));
        Assert.False(collector.TryAdd(TimeSpan.FromSeconds(4), "タクシーに乗ったら"));

        OcrLine line = Assert.Single(collector.ToLines());
        Assert.Equal(TimeSpan.Zero, line.Elapsed);
    }

    [Fact]
    public void 最初に出たフレームの時刻だけが残る()
    {
        OcrTextCollector collector = new();

        collector.TryAdd(TimeSpan.FromSeconds(6), "あとで出た行");
        collector.TryAdd(TimeSpan.FromSeconds(8), "あとで出た行");

        Assert.Equal(TimeSpan.FromSeconds(6), Assert.Single(collector.ToLines()).Elapsed);
    }

    [Theory]
    [InlineData("１台のタクシー", "1台のタクシー")]
    [InlineData("ＴＡＸＩ", "TAXI")]
    [InlineData("タクシー  に   乗った", "タクシー に 乗った")]
    [InlineData("  前後に空白  ", "前後に空白")]
    public void 全角半角と空白の揺れは同じものとして扱う(string first, string second)
    {
        OcrTextCollector collector = new();

        Assert.True(collector.TryAdd(TimeSpan.Zero, first));
        Assert.False(collector.TryAdd(TimeSpan.FromSeconds(2), second));
    }

    [Fact]
    public void 大文字小文字は揃えない()
    {
        // 「TAXi3」と「taxi3」を同じにすると、固有名詞の表記という手がかりが消える。
        OcrTextCollector collector = new();

        Assert.True(collector.TryAdd(TimeSpan.Zero, "TAXi3"));
        Assert.True(collector.TryAdd(TimeSpan.FromSeconds(2), "taxi3"));
        Assert.Equal(2, collector.Count);
    }

    [Fact]
    public void 全角半角の変換は出力に持ち込まない()
    {
        // 全角半角を揃えるのは重複判定のときだけ。出力の文字はそのまま残す。
        // 整えるのは、認識器が入れた空白だけ（ADR-0018 実装で分かったこと 2）。
        OcrTextCollector collector = new();

        collector.TryAdd(TimeSpan.Zero, "  ＴＡＸＩ  ３  ");

        Assert.Equal("ＴＡＸＩ ３", Assert.Single(collector.ToLines()).Text);
    }

    [Theory]
    [InlineData("タ ク シ ー に 乗 っ た ら", "タクシーに乗ったら")]
    [InlineData("絶 体 絶 命 瞬 間", "絶体絶命瞬間")]
    [InlineData("後 ろ の 車 に 乗 っ て く だ さ い", "後ろの車に乗ってください")]
    public void 日本語の分かち書きは詰める(string recognized, string expected)
    {
        // Windows の日本語 OCR は 1 文字ずつを「語」として返すため、
        // 行の文字列が分かち書きになる。これは認識器の都合であって原文ではない。
        OcrTextCollector collector = new();

        collector.TryAdd(TimeSpan.Zero, recognized);

        Assert.Equal(expected, Assert.Single(collector.ToLines()).Text);
    }

    [Theory]
    [InlineData("TAXi 3 2003", "TAXi 3 2003")]
    [InlineData("1 台のタクシー", "1 台のタクシー")]
    [InlineData("Inside the penthouse", "Inside the penthouse")]
    public void 英数字の間の空白は残す(string recognized, string expected)
    {
        // 詰めると「TAXi 3」が「TAXi3」になり、別の文字列を作ってしまう。
        OcrTextCollector collector = new();

        collector.TryAdd(TimeSpan.Zero, recognized);

        Assert.Equal(expected, Assert.Single(collector.ToLines()).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空の行は捨てる(string? text)
    {
        OcrTextCollector collector = new();

        Assert.False(collector.TryAdd(TimeSpan.Zero, text));
        Assert.Empty(collector.ToLines());
    }
}

public sealed class MediaTextFormatterTests
{
    private readonly MediaTextFormatter formatter = new();

    [Fact]
    public void 文字起こしに言語とモデル名が入る()
    {
        TranscriptionResult result = new(
            true,
            "ja",
            [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(4.2), "タクシーに乗ったら")],
            null);

        string text = this.formatter.FormatTranscript(result, "ggml-base.bin");

        Assert.Contains("言語: ja", text, StringComparison.Ordinal);
        Assert.Contains("モデル: ggml-base.bin", text, StringComparison.Ordinal);
        Assert.Contains("[00:00.0 - 00:04.2] タクシーに乗ったら", text, StringComparison.Ordinal);
    }

    [Fact]
    public void 文字起こしが空なら無い旨の文言になる()
    {
        // 空欄のままにすると「無音」と「取っていない」が区別できない。
        TranscriptionResult result = new(true, "ja", [], null);

        Assert.Contains(MediaTextFormatter.NoTranscriptText, this.formatter.FormatTranscript(result, "x.bin"), StringComparison.Ordinal);
    }

    [Fact]
    public void OCR_に検出枚数が入る()
    {
        OcrResult result = new(
            true,
            "ja",
            9,
            12,
            [new OcrLine(TimeSpan.Zero, "タクシーに乗ったら")],
            null);

        string text = this.formatter.FormatOcr(result);

        Assert.Contains("言語: ja", text, StringComparison.Ordinal);
        Assert.Contains("フレーム 12 枚中 9 枚から検出", text, StringComparison.Ordinal);
        Assert.Contains("0.0s] タクシーに乗ったら", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "00:00.0")]
    [InlineData(4.25, "00:04.2")]
    [InlineData(65.9, "01:05.9")]
    [InlineData(610.0, "10:10.0")]
    public void 時刻の書式(double seconds, string expected)
    {
        Assert.Equal(expected, MediaTextFormatter.FormatTimestamp(TimeSpan.FromSeconds(seconds)));
    }
}

public sealed class MediaProcessingServiceTests : IDisposable
{
    private readonly string root;
    private readonly AppDataPaths paths;
    private readonly FileSystemSessionWorkspace workspace;
    private readonly SessionLayout layout;

    public MediaProcessingServiceTests()
    {
        this.root = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.root);
        this.paths = new AppDataPaths(this.root);
        this.workspace = new FileSystemSessionWorkspace(this.paths);
        this.layout = this.workspace.Create(SessionId.New());
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private void WriteAudio() => File.WriteAllBytes(this.layout.AudioFile, new byte[64]);

    private void WriteFrame(double seconds)
    {
        string name = SessionLayout.FrameFileName(1, TimeSpan.FromSeconds(seconds), ".jpg");
        File.WriteAllBytes(Path.Combine(this.layout.FramesDirectory, name), [1, 2, 3]);
    }

    private MediaProcessingService Create(FakeTranscriber transcriber, FakeOcrEngine ocr) =>
        new(transcriber, ocr, new FileSystemFrameLister());

    [Fact]
    public async Task 文字起こしとOCRの両方が書き出される()
    {
        this.WriteAudio();
        this.WriteFrame(0);

        MediaProcessingService service = this.Create(new FakeTranscriber(), new FakeOcrEngine());

        MediaProcessingResult result = await service.ProcessAsync(
            this.layout,
            [],
            new NutLensSettings(),
            progress: null,
            CancellationToken.None);

        Assert.True(result.Transcription.Succeeded);
        Assert.True(result.Ocr.Succeeded);
        Assert.True(File.Exists(this.layout.TranscriptFile));
        Assert.True(File.Exists(this.layout.OcrFile));
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task 音声が無ければ文字起こしを飛ばし_OCRは続く()
    {
        this.WriteFrame(0);

        MediaProcessingService service = this.Create(new FakeTranscriber(), new FakeOcrEngine());

        MediaProcessingResult result = await service.ProcessAsync(
            this.layout,
            [],
            new NutLensSettings(),
            progress: null,
            CancellationToken.None);

        Assert.False(result.Transcription.Succeeded);
        Assert.True(result.Ocr.Succeeded);
        Assert.False(File.Exists(this.layout.TranscriptFile));
        Assert.True(File.Exists(this.layout.OcrFile));
    }

    [Fact]
    public async Task OCRが使えなくても文字起こしは続く()
    {
        this.WriteAudio();
        this.WriteFrame(0);

        MediaProcessingService service = this.Create(
            new FakeTranscriber(),
            new FakeOcrEngine(available: false));

        MediaProcessingResult result = await service.ProcessAsync(
            this.layout,
            [],
            new NutLensSettings(),
            progress: null,
            CancellationToken.None);

        Assert.True(result.Transcription.Succeeded);
        Assert.False(result.Ocr.Succeeded);
        Assert.True(File.Exists(this.layout.TranscriptFile));
        Assert.False(File.Exists(this.layout.OcrFile));
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task 両方だめでも結果を返す()
    {
        // 補助情報が取れなかっただけで解析を止めない（概要設計 §10.1）。
        MediaProcessingService service = this.Create(
            new FakeTranscriber(),
            new FakeOcrEngine(available: false));

        MediaProcessingResult result = await service.ProcessAsync(
            this.layout,
            [],
            new NutLensSettings(),
            progress: null,
            CancellationToken.None);

        Assert.False(result.Transcription.Succeeded);
        Assert.False(result.Ocr.Succeeded);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task 設定で切っていれば呼ばれない()
    {
        this.WriteAudio();
        this.WriteFrame(0);

        FakeTranscriber transcriber = new();
        FakeOcrEngine ocr = new();
        MediaProcessingService service = this.Create(transcriber, ocr);

        NutLensSettings settings = new()
        {
            Whisper = new WhisperSettings { Enabled = false },
            Ocr = new OcrSettings { Enabled = false },
        };

        await service.ProcessAsync(this.layout, [], settings, progress: null, CancellationToken.None);

        Assert.False(transcriber.Called);
        Assert.False(ocr.Called);
    }

    private sealed class FakeTranscriber : ITranscriber
    {
        public bool Called { get; private set; }

        public Task<TranscriptionResult> TranscribeAsync(
            string wavPath,
            string? modelPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            this.Called = true;
            progress?.Report(1.0);
            return Task.FromResult(new TranscriptionResult(
                true,
                "ja",
                [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "テスト")],
                null));
        }
    }

    private sealed class FakeOcrEngine : IOcrEngine
    {
        private readonly bool available;

        public FakeOcrEngine(bool available = true) => this.available = available;

        public bool Called { get; private set; }

        public bool IsAvailable(out string? reason)
        {
            reason = this.available ? null : "テスト用に OCR が使えない設定です。";
            return this.available;
        }

        public Task<OcrResult> RecognizeAsync(
            IReadOnlyList<OcrTarget> frames,
            string? preferredLanguage,
            CancellationToken cancellationToken)
        {
            this.Called = true;
            return Task.FromResult(new OcrResult(
                true,
                "ja",
                frames.Count,
                frames.Count,
                [new OcrLine(TimeSpan.Zero, "テロップ")],
                null));
        }
    }
}
