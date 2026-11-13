using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Analysis;
using NutLens.Core.Analysis;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class AnalysisServiceTests : IDisposable
{
    private const string Template = "フレーム:\n{{frames}}\n文字起こし:\n{{transcript}}\nメモ:\n{{notes}}\nスキーマ: {{schema}}";

    private readonly string root;
    private readonly AppDataPaths paths;
    private readonly FileSystemSessionWorkspace workspace;
    private readonly SessionLayout layout;
    private readonly AnalyzerFiles analyzer;

    public AnalysisServiceTests()
    {
        this.root = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.root);
        this.paths = new AppDataPaths(this.root);
        this.workspace = new FileSystemSessionWorkspace(this.paths);
        this.layout = this.workspace.Create(SessionId.New());

        // analyzer/ を配置した状態を手で作る。
        string schemaDirectory = Path.Combine(this.layout.Root, "schema");
        Directory.CreateDirectory(schemaDirectory);
        string templatePath = Path.Combine(this.layout.Root, "prompt-template.md");
        string schemaPath = Path.Combine(schemaDirectory, "result.schema.json");
        File.WriteAllText(templatePath, Template);
        File.WriteAllText(schemaPath, """{"type":"object"}""");

        this.analyzer = new AnalyzerFiles(
            Path.Combine(this.layout.Root, "CLAUDE.md"),
            templatePath,
            schemaPath,
            "schema/result.schema.json",
            Path.Combine(this.layout.Root, ".claude", "skills", "identify-movie", "SKILL.md"));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private AnalysisService CreateService(FakeAnalysisEngine engine) => new(
        new FakeDeployer(this.analyzer),
        engine,
        new AnalysisResultParser(),
        new FileSystemFrameLister(),
        TimeProvider.System);

    private void WriteFrames(params double[] seconds)
    {
        foreach (double second in seconds)
        {
            string name = SessionLayout.FrameFileName(
                Array.IndexOf(seconds, second) + 1,
                TimeSpan.FromSeconds(second),
                ".jpg");
            File.WriteAllBytes(Path.Combine(this.layout.FramesDirectory, name), [1, 2, 3]);
        }
    }

    [Fact]
    public void プロンプトにフレームの一覧が入る()
    {
        this.WriteFrames(0.0, 2.0, 4.0);
        AnalysisService service = this.CreateService(new FakeAnalysisEngine());

        PromptPreparation preparation = service.PreparePrompt(this.layout, UserNotes.Empty);

        Assert.Equal(3, preparation.FrameCount);
        Assert.Contains("frames/frame_001_t0000.0s.jpg", preparation.Prompt, StringComparison.Ordinal);
        Assert.Contains("frames/frame_003_t0004.0s.jpg", preparation.Prompt, StringComparison.Ordinal);
        Assert.Contains("schema/result.schema.json", preparation.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void フレームは経過秒の順に並ぶ()
    {
        this.WriteFrames(10.0, 0.0, 5.0);
        AnalysisService service = this.CreateService(new FakeAnalysisEngine());

        PromptPreparation preparation = service.PreparePrompt(this.layout, UserNotes.Empty);

        int first = preparation.Prompt.IndexOf("t0000.0s", StringComparison.Ordinal);
        int middle = preparation.Prompt.IndexOf("t0005.0s", StringComparison.Ordinal);
        int last = preparation.Prompt.IndexOf("t0010.0s", StringComparison.Ordinal);

        Assert.True(first < middle && middle < last, "経過秒の順に並んでいない");
    }

    [Fact]
    public void 文字起こしがあればプロンプトに入る()
    {
        this.WriteFrames(0.0);
        File.WriteAllText(this.layout.TranscriptFile, "ナレーションの本文");
        AnalysisService service = this.CreateService(new FakeAnalysisEngine());

        PromptPreparation preparation = service.PreparePrompt(this.layout, UserNotes.Empty);

        Assert.Contains("ナレーションの本文", preparation.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void WebDetectionは成功した回だけプロンプトに入る()
    {
        // ADR-0022 決定 6。失敗した回の web_detection.json をそのまま差し込むと、
        // 「参考情報」としてエラーの記録が Claude に渡る。
        File.WriteAllText(this.analyzer.PromptTemplatePath, "参考:\n{{web_detection}}");
        this.WriteFrames(0.0);
        AnalysisService service = this.CreateService(new FakeAnalysisEngine());

        NutLens.Core.WebDetection.WebDetectionDocument.Write(
            this.layout.WebDetectionFile,
            NutLens.Core.WebDetection.WebDetectionOutcome.Failed(
                NutLens.Core.WebDetection.WebDetectionStatus.Auth,
                "API キーが拒否されました"));

        Assert.DoesNotContain(
            "拒否",
            service.PreparePrompt(this.layout, UserNotes.Empty).Prompt,
            StringComparison.Ordinal);

        NutLens.Core.WebDetection.WebDetectionDocument.Write(
            this.layout.WebDetectionFile,
            new NutLens.Core.WebDetection.WebDetectionOutcome(
                NutLens.Core.WebDetection.WebDetectionStatus.Ok,
                1,
                [
                    new NutLens.Core.WebDetection.WebDetectionFrameResult(
                        "frames/frame_001_t0000.0s.jpg",
                        0.0,
                        ["taxi chase"],
                        [],
                        []),
                ],
                Message: null));

        Assert.Contains(
            "taxi chase",
            service.PreparePrompt(this.layout, UserNotes.Empty).Prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task 送信した内容がプロンプトファイルに残る()
    {
        // 送った内容と残っている内容が食い違うと、不具合報告から原因を追えない。
        this.WriteFrames(0.0);
        FakeAnalysisEngine engine = new();
        AnalysisService service = this.CreateService(engine);

        const string Edited = "利用者が書き換えたプロンプト";
        await service.AnalyzeAsync(
            new AnalysisRequest(this.layout, this.analyzer, Edited, new ClaudeSettings(), Resume: false),
            progress: null,
            CancellationToken.None);

        Assert.Equal(Edited, File.ReadAllText(this.layout.PromptFile));
        Assert.Equal(Edited, engine.LastInvocation!.Prompt);
    }

    [Fact]
    public async Task 起動の指示にセッションと作業ディレクトリが入る()
    {
        this.WriteFrames(0.0);
        FakeAnalysisEngine engine = new();
        AnalysisService service = this.CreateService(engine);

        await service.AnalyzeAsync(
            new AnalysisRequest(this.layout, this.analyzer, "本文", new ClaudeSettings { MaxTurns = 42 }, Resume: false),
            progress: null,
            CancellationToken.None);

        ClaudeInvocation invocation = engine.LastInvocation!;
        Assert.Equal(this.layout.Root, invocation.WorkingDirectory);
        Assert.Equal(this.layout.Id, invocation.SessionId);
        Assert.Equal(this.layout.ClaudeLogFile, invocation.LogFilePath);
        Assert.Equal(42, invocation.MaxTurns);
        Assert.False(invocation.Resume);
        Assert.Equal("""{"type":"object"}""", invocation.SchemaJson);
    }

    [Fact]
    public async Task 再調査は_Resume_が真になる()
    {
        this.WriteFrames(0.0);
        FakeAnalysisEngine engine = new();
        AnalysisService service = this.CreateService(engine);

        await service.AnalyzeAsync(
            new AnalysisRequest(this.layout, this.analyzer, "追加情報", new ClaudeSettings(), Resume: true),
            progress: null,
            CancellationToken.None);

        Assert.True(engine.LastInvocation!.Resume);
    }

    [Fact]
    public async Task 結果がパースされて返る()
    {
        this.WriteFrames(0.0);
        FakeAnalysisEngine engine = new("""
            {"subtype":"success","is_error":false,
             "structured_output":{"candidates":[{"title_ja":"テスト作品","type":"movie","confidence":0.9}],
                                  "observed_features":[],"unverified_points":[],"search_queries_used":[]}}
            """);
        AnalysisService service = this.CreateService(engine);

        AnalysisOutcome outcome = await service.AnalyzeAsync(
            new AnalysisRequest(this.layout, this.analyzer, "本文", new ClaudeSettings(), Resume: false),
            progress: null,
            CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.Equal("テスト作品", Assert.Single(outcome.Result!.Candidates).TitleJa);
    }

    [Fact]
    public async Task キャンセルは呼び出し元へ伝わる()
    {
        this.WriteFrames(0.0);
        FakeAnalysisEngine engine = new() { ThrowOnCancel = true };
        AnalysisService service = this.CreateService(engine);

        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.AnalyzeAsync(
            new AnalysisRequest(this.layout, this.analyzer, "本文", new ClaudeSettings(), Resume: false),
            progress: null,
            cts.Token));
    }

    [Theory]
    [InlineData("frame_001_t0000.0s.jpg", 0.0)]
    [InlineData("frame_017_t0123.5s.jpg", 123.5)]
    [InlineData("frame_999_t3599.9s.jpg", 3599.9)]
    [InlineData("なまえがちがう.jpg", 0.0)]
    [InlineData("frame_001_tXXX.jpg", 0.0)]
    public void ファイル名から経過秒を読み取る(string fileName, double expected)
    {
        // 読めなくても 0 秒にする。名前の揺れだけで解析全体を止めない。
        Assert.Equal(TimeSpan.FromSeconds(expected), FileSystemFrameLister.ParseElapsed(fileName));
    }

    private sealed class FakeDeployer : IAnalyzerTemplateDeployer
    {
        private readonly AnalyzerFiles files;

        public FakeDeployer(AnalyzerFiles files) => this.files = files;

        public AnalyzerFiles Deploy(SessionLayout layout) => this.files;
    }

    private sealed class FakeAnalysisEngine : IAnalysisEngine
    {
        private readonly string stdout;

        public FakeAnalysisEngine(string? stdout = null) =>
            this.stdout = stdout ?? """
                {"subtype":"success","is_error":false,
                 "structured_output":{"candidates":[],"observed_features":[],"unverified_points":[],"search_queries_used":[]}}
                """;

        public ClaudeInvocation? LastInvocation { get; private set; }

        public bool ThrowOnCancel { get; init; }

        public Task<ClaudeRunOutput> RunAsync(ClaudeInvocation invocation, CancellationToken cancellationToken)
        {
            this.LastInvocation = invocation;

            if (this.ThrowOnCancel)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(new ClaudeRunOutput(0, this.stdout, string.Empty, TimeSpan.FromSeconds(1)));
        }
    }
}

public sealed class ClaudeInvocationArgumentTests
{
    private static ClaudeInvocation Invocation(bool resume) => new(
        WorkingDirectory: @"C:\work\session",
        Prompt: "プロンプト本文",
        SchemaJson: """{"type":"object"}""",
        SessionId: SessionId.Parse("11111111-2222-3333-4444-555555555555"),
        MaxTurns: 30,
        LogFilePath: @"C:\work\session\claude.log",
        Resume: resume,
        ExecutablePath: null);

    [Fact]
    public void 初回は_session_id_を渡す()
    {
        List<string> arguments = [.. ClaudeCliAnalysisEngine.BuildArguments(Invocation(resume: false))];

        Assert.Contains("--session-id", arguments);
        Assert.DoesNotContain("--resume", arguments);
        Assert.Equal("11111111-2222-3333-4444-555555555555", arguments[arguments.IndexOf("--session-id") + 1]);
    }

    [Fact]
    public void 再調査は_resume_に入れ替わる()
    {
        List<string> arguments = [.. ClaudeCliAnalysisEngine.BuildArguments(Invocation(resume: true))];

        Assert.Contains("--resume", arguments);
        Assert.DoesNotContain("--session-id", arguments);
    }

    [Fact]
    public void 書き込みとシェルを許可しない()
    {
        // 5.4 の要求。ツール集合そのものを絞り、許可リストも渡し、問い合わせは自動拒否にする。
        List<string> arguments = [.. ClaudeCliAnalysisEngine.BuildArguments(Invocation(resume: false))];

        Assert.Equal("Read,WebSearch,WebFetch", arguments[arguments.IndexOf("--tools") + 1]);
        Assert.Equal("Read,WebSearch,WebFetch", arguments[arguments.IndexOf("--allowedTools") + 1]);
        Assert.Equal("none", arguments[arguments.IndexOf("--permission-prompts") + 1]);

        Assert.DoesNotContain(arguments, a => a.Contains("Bash", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, a => a.Contains("Write", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, a => a.Contains("Edit", StringComparison.Ordinal));
    }

    [Fact]
    public void スキーマはインラインで渡す()
    {
        // --json-schema はファイルパスを受け付けない（実測。ADR-0006 実装で分かったこと 2）。
        List<string> arguments = [.. ClaudeCliAnalysisEngine.BuildArguments(Invocation(resume: false))];

        Assert.Equal("""{"type":"object"}""", arguments[arguments.IndexOf("--json-schema") + 1]);
    }

    [Fact]
    public void プロンプトは引数に載らない()
    {
        // ADR-0038。プロンプトの中身は OCR と文字起こし＝**画面に映っていた任意の文字列**。
        // 引数に載せると、npm 版（cmd.exe 経由）でシェルに解釈される。
        List<string> arguments = [.. ClaudeCliAnalysisEngine.BuildArguments(Invocation(resume: false))];

        Assert.DoesNotContain("プロンプト本文", arguments);
    }

    [Fact]
    public void シェルの記号を含むプロンプトでも引数に漏れない()
    {
        // CVE-2024-24576 と同じ形。引用符で引用を抜けて、後続の & が実行される。
        const string Payload = """A" & calc & "B""";

        ClaudeInvocation invocation = Invocation(resume: false) with { Prompt = Payload };
        List<string> arguments = [.. ClaudeCliAnalysisEngine.BuildArguments(invocation)];

        Assert.DoesNotContain(arguments, a => a.Contains("calc", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, a => a.Contains('&', StringComparison.Ordinal));
    }

    [Fact]
    public void 標準入力を開いて_UTF8_で書く()
    {
        // 開き忘れると StandardInput が使えず、プロンプトを渡す先が無い。
        // 符号化を指定しないと**既定は現在のコードページ**で、日本語が化ける。
        ClaudeExecutable executable = new(@"C:\claude\claude.exe", false, "2.1.322");
        System.Diagnostics.ProcessStartInfo info =
            ClaudeCliAnalysisEngine.BuildStartInfo(executable, Invocation(resume: false));

        Assert.True(info.RedirectStandardInput);
        Assert.Equal(System.Text.Encoding.UTF8.CodePage, info.StandardInputEncoding?.CodePage);
    }

    [Fact]
    public void 最大ターン数と出力形式が入る()
    {
        List<string> arguments = [.. ClaudeCliAnalysisEngine.BuildArguments(Invocation(resume: false))];

        Assert.Equal("30", arguments[arguments.IndexOf("--max-turns") + 1]);
        Assert.Equal("json", arguments[arguments.IndexOf("--output-format") + 1]);
        Assert.Contains("-p", arguments);
    }

    [Fact]
    public void セッションが使用中なら再調査として実行し直す()
    {
        // 解析を取り消して始め直すと、同じ --session-id は拒否される（実測）。
        // 受け止めないと、取り消した直後にもう一度押しただけで解析できなくなる。
        ClaudeRunOutput inUse = new(
            1,
            string.Empty,
            "Error: Session ID e7c9e6d2-240d-4f6b-bdaf-0031589bba0d is already in use.",
            TimeSpan.FromSeconds(0.2));

        Assert.True(ClaudeCliAnalysisEngine.ShouldRetryAsResume(wasResume: false, inUse));
    }

    [Fact]
    public void 既に再調査なら_やり直さない()
    {
        // 無限に再実行しないこと。
        ClaudeRunOutput inUse = new(1, string.Empty, "already in use", TimeSpan.Zero);

        Assert.False(ClaudeCliAnalysisEngine.ShouldRetryAsResume(wasResume: true, inUse));
    }

    [Fact]
    public void 別の理由の失敗ではやり直さない()
    {
        ClaudeRunOutput other = new(1, string.Empty, "Error: not logged in", TimeSpan.Zero);

        Assert.False(ClaudeCliAnalysisEngine.ShouldRetryAsResume(wasResume: false, other));
    }

    [Fact]
    public void 成功していればやり直さない()
    {
        ClaudeRunOutput success = new(0, "{}", "already in use", TimeSpan.Zero);

        Assert.False(ClaudeCliAnalysisEngine.ShouldRetryAsResume(wasResume: false, success));
    }

    [Theory]
    [InlineData(@"C:\tools\claude.cmd", true)]
    [InlineData(@"C:\tools\claude.BAT", true)]
    [InlineData(@"C:\tools\claude.exe", false)]
    [InlineData(@"/usr/bin/claude", false)]
    public void cmd_経由が必要かを拡張子で判断する(string path, bool expected)
    {
        // .NET からは .cmd / .bat を直接起動できない（npm 版がこれに当たる）。
        Assert.Equal(expected, ClaudeCliLocator.RequiresCmdShell(path));
    }
}
