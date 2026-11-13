using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.Core.Analysis;

/// <summary>プロンプトを作るための入力（セッションと利用者のメモ）。</summary>
public sealed record PromptPreparation(
    SessionLayout Layout,
    AnalyzerFiles Analyzer,
    string Prompt,
    int FrameCount);

/// <summary>解析の指示。</summary>
public sealed record AnalysisRequest(
    SessionLayout Layout,
    AnalyzerFiles Analyzer,
    string Prompt,
    ClaudeSettings Claude,
    bool Resume);

/// <summary>1 回の解析を取り仕切る役（詳細設計 03 §7）。</summary>
public interface IAnalysisService
{
    /// <summary>analyzer/ を配置し、フレームを数えてプロンプトを作る（F-21 / F-23）。</summary>
    PromptPreparation PreparePrompt(SessionLayout layout, UserNotes notes);

    /// <summary>プロンプトを保存して claude を起動し、結果を返す（F-24）。</summary>
    Task<AnalysisOutcome> AnalyzeAsync(
        AnalysisRequest request,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class AnalysisService : IAnalysisService
{
    private readonly IAnalyzerTemplateDeployer deployer;
    private readonly IAnalysisEngine engine;
    private readonly IAnalysisResultParser parser;
    private readonly IFrameFileLister frameLister;
    private readonly TimeProvider timeProvider;

    /// <summary>利用者が開いて読むファイルは UTF-8（BOM つき）で書く。</summary>
    internal static readonly System.Text.Encoding TextFileEncoding =
        new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public AnalysisService(
        IAnalyzerTemplateDeployer deployer,
        IAnalysisEngine engine,
        IAnalysisResultParser parser,
        IFrameFileLister frameLister,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(deployer);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(frameLister);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.deployer = deployer;
        this.engine = engine;
        this.parser = parser;
        this.frameLister = frameLister;
        this.timeProvider = timeProvider;
    }

    public PromptPreparation PreparePrompt(SessionLayout layout, UserNotes notes)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(notes);

        AnalyzerFiles analyzer = this.deployer.Deploy(layout);

        IReadOnlyList<FrameReference> frames = this.frameLister.List(layout);
        string template = File.ReadAllText(analyzer.PromptTemplatePath);
        PromptComposer composer = new(template);

        string prompt = composer.Compose(new PromptInputs(
            frames,
            notes,
            analyzer.SchemaRelativePath,
            Transcript: ReadIfExists(layout.TranscriptFile),
            OcrText: ReadIfExists(layout.OcrFile),

            // 生のファイルを読まない。status が ok のときだけ本文が返る（ADR-0022 決定 6）。
            // 失敗した回の web_detection.json をそのまま差し込むと、
            // 「参考情報」としてエラーの記録が Claude に渡る。
            WebDetectionJson: WebDetection.WebDetectionDocument.TryReadReference(layout.WebDetectionFile)));

        return new PromptPreparation(layout, analyzer, prompt, frames.Count);
    }

    public async Task<AnalysisOutcome> AnalyzeAsync(
        AnalysisRequest request,
        IProgress<AnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // F-22 で編集された内容を prompt.md に反映してから送る。
        // 送った内容と残っている内容が食い違うと、不具合報告から原因を追えない。
        // BOM を付けるのは、Windows のメモ帳などで開いたときに日本語が化けないようにするため。
        File.WriteAllText(request.Layout.PromptFile, request.Prompt, TextFileEncoding);

        long started = this.timeProvider.GetTimestamp();
        progress?.Report(new AnalysisProgress("Claude Code を起動しています…", TimeSpan.Zero));

        ClaudeSettings claude = request.Claude.Normalize();
        string schemaJson = File.ReadAllText(request.Analyzer.SchemaPath);

        ClaudeInvocation invocation = new(
            request.Layout.Root,
            request.Prompt,
            schemaJson,
            request.Layout.Id,
            claude.MaxTurns,
            request.Layout.ClaudeLogFile,
            request.Resume,
            claude.ExecutablePath);

        ClaudeRunOutput output = await this.engine
            .RunAsync(invocation, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new AnalysisProgress(
            "結果を読み取っています…",
            this.timeProvider.GetElapsedTime(started)));

        return this.parser.Parse(request.Layout.Id, output);
    }

    private static string? ReadIfExists(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;
}

/// <summary>セッションの frames/ を並べる役。相対パスと経過秒を取り出す。</summary>
public interface IFrameFileLister
{
    IReadOnlyList<FrameReference> List(SessionLayout layout);
}

/// <summary>
/// ファイル名から経過秒を読み取る実装。
/// 書式は <c>frame_001_t0002.0s.jpg</c>（<see cref="SessionLayout.FrameFileName"/>）。
/// </summary>
public sealed class FileSystemFrameLister : IFrameFileLister
{
    public IReadOnlyList<FrameReference> List(SessionLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!Directory.Exists(layout.FramesDirectory))
        {
            return [];
        }

        List<FrameReference> frames = [];
        foreach (string path in Directory.GetFiles(layout.FramesDirectory))
        {
            string name = Path.GetFileName(path);
            frames.Add(new FrameReference(
                $"{SessionLayout.FramesDirectoryName}/{name}",
                ParseElapsed(name)));
        }

        frames.Sort(static (left, right) => left.Elapsed.CompareTo(right.Elapsed));
        return frames;
    }

    /// <summary>
    /// ファイル名から経過秒を読む。書式は <c>frame_001_t0002.0s.jpg</c>。
    /// 読めなければ 0 秒。ここで例外にすると、名前の揺れだけで解析全体が止まる。
    /// </summary>
    public static TimeSpan ParseElapsed(string fileName)
    {
        int start = fileName.IndexOf("_t", StringComparison.Ordinal);
        if (start < 0)
        {
            return TimeSpan.Zero;
        }

        start += 2;
        int end = fileName.IndexOf('s', start);
        if (end <= start)
        {
            return TimeSpan.Zero;
        }

        return double.TryParse(
            fileName.AsSpan(start, end - start),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;
    }
}
