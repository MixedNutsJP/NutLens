using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Analysis;
using NutLens.Core.Analysis;
using NutLens.Core.Distribution;
using NutLens.Core.Settings;
using Xunit;

namespace NutLens.Core.Tests;

/// <summary>選べるモデル（D-04 / ADR-0034）。</summary>
public sealed class WhisperModelChoiceTests
{
    [Fact]
    public void 多言語版を六つ出す()
    {
        Assert.Equal(
            ["tiny", "base", "small", "medium", "large-v3-turbo", "large-v3"],
            WhisperModelChoice.All.Select(static m => m.Name));
    }

    [Fact]
    public void 英語専用は出さない()
    {
        // **日本語を文字起こしできない。** F-11 の「日本語と英語に対応する」を満たせなくなる。
        Assert.DoesNotContain(
            WhisperModelChoice.All,
            static m => m.Name.EndsWith(".en", StringComparison.Ordinal)
                || m.FileName.Contains(".en.", StringComparison.Ordinal));
    }

    [Fact]
    public void 旧版のlargeは出さない()
    {
        // large-v3 と同じ 2951 MB で、選ぶ理由がない。
        Assert.DoesNotContain(WhisperModelChoice.All, static m => m.Name is "large-v1" or "large-v2");
    }

    [Fact]
    public void 推奨はsmall()
    {
        Assert.Equal("small", WhisperModelChoice.Recommended.Name);
        Assert.True(WhisperModelChoice.Recommended.IsRecommended);
    }

    [Fact]
    public void 推奨はちょうど一つ()
    {
        Assert.Single(WhisperModelChoice.All, static m => m.IsRecommended);
    }

    [Fact]
    public void ファイル名が重複しない()
    {
        // 同じ名前で落とすと、別のモデルを上書きする。
        Assert.Equal(
            WhisperModelChoice.All.Count,
            WhisperModelChoice.All.Select(static m => m.FileName).Distinct().Count());
    }

    [Fact]
    public void 大きさの順に並ぶ()
    {
        // 一覧は軽い順。利用者が上から読んで選べるようにする。
        // ただし large-v3-turbo は medium より少し大きいだけで、large-v3 より前に来る。
        long[] sizes = WhisperModelChoice.All.Select(static m => m.ApproximateBytes).ToArray();
        for (int i = 1; i < sizes.Length; i++)
        {
            Assert.True(sizes[i - 1] < sizes[i], $"{i} 番目で順序が崩れています");
        }
    }

    [Fact]
    public void largeV3turboはmediumとほぼ同じ大きさ()
    {
        // **ADR-0030 決定 2 の却下理由（3 GB 近い）が当てはまらない根拠。**
        // ここが崩れたら、turbo を出している理由を見直すこと。
        long medium = WhisperModelChoice.All.First(static m => m.Name == "medium").ApproximateMegabytes;
        long turbo = WhisperModelChoice.All.First(static m => m.Name == "large-v3-turbo").ApproximateMegabytes;

        Assert.True(turbo - medium < 200, $"差が {turbo - medium} MB あります");
    }

    [Fact]
    public void ハッシュの期待値はまだ無い()
    {
        // **実測していないので null のまま**（ADR-0030 宿題）。
        // 埋めたらこのテストを落として、代わりに「全部埋まっている」を確かめる。
        Assert.All(WhisperModelChoice.All, static m => Assert.Null(m.ExpectedSha256));
    }
}

/// <summary>更新の確認の顛末（D-02 / ADR-0028）。</summary>
public sealed class UpdateCheckResultTests
{
    [Theory]
    [InlineData(UpdateCheckStatus.UpToDate)]
    [InlineData(UpdateCheckStatus.Failed)]
    [InlineData(UpdateCheckStatus.NotInstalled)]
    [InlineData(UpdateCheckStatus.Disabled)]
    [InlineData(UpdateCheckStatus.Skipped)]
    public void 見つかったとき以外は尋ねない(UpdateCheckStatus status)
    {
        // 「最新です」も確認の失敗も画面に出さない（決定 2・3）。
        Assert.False(UpdateCheckResult.Of(status).ShouldPrompt);
    }

    [Fact]
    public void 見つかったときだけ尋ねる()
    {
        Assert.True(new UpdateCheckResult(UpdateCheckStatus.Available, "0.2.0", "0.1.0", null).ShouldPrompt);
    }

    [Fact]
    public void 更新の既定は確認する()
    {
        UpdateSettings settings = new();

        Assert.True(settings.CheckOnStartup);
        Assert.Null(settings.SkippedVersion);
        Assert.Null(settings.LastCheckedAt);
    }
}

/// <summary>初回セットアップ（D-03 / D-07 / ADR-0029）。</summary>
public sealed class SetupSettingsTests
{
    [Fact]
    public void 初回は説明を見ていない扱い()
    {
        Assert.False(new SetupSettings().HasSeenCurrentNotice);
    }

    [Fact]
    public void 今の版まで見ていれば出さない()
    {
        Assert.True(new SetupSettings { CompletedVersion = SetupSettings.CurrentNoticeVersion }
            .HasSeenCurrentNotice);
    }

    [Fact]
    public void 説明の版を上げると見ていない扱いに戻る()
    {
        // 文面を変えたらもう一度出す（ADR-0029 決定 3）。
        Assert.False(new SetupSettings { CompletedVersion = SetupSettings.CurrentNoticeVersion - 1 }
            .HasSeenCurrentNotice);
    }
}

/// <summary>モデルのダウンロード（D-04 / ADR-0030）。</summary>
public sealed class WhisperModelDownloaderTests : IDisposable
{
    private readonly string root;

    public WhisperModelDownloaderTests()
    {
        this.root = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.root);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private string Destination(string name) => Path.Combine(this.root, name);

    private static WhisperModelChoice Tiny(string? sha = null) =>
        new("tiny", "ggml-tiny.bin", 1000, sha);

    private static WhisperModelDownloader Create(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)), ownsHttpClient: true);

    [Fact]
    public async Task 落として照合が通れば完成した名前になる()
    {
        byte[] payload = new byte[2000];
        using WhisperModelDownloader downloader = Create(_ => Ok(payload));

        string destination = this.Destination("ggml-tiny.bin");
        ModelDownloadResult result = await downloader
            .DownloadAsync(Tiny(), destination, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(destination));

        // 途中のファイルは残らない。
        Assert.False(File.Exists(destination + WhisperModelDownloader.PartialExtension));
    }

    [Fact]
    public async Task 小さすぎるものは壊れている扱いにする()
    {
        // HTML のエラーページが降ってきた場合もここで落ちる。
        using WhisperModelDownloader downloader = Create(_ => Ok(new byte[10]));

        string destination = this.Destination("ggml-tiny.bin");
        ModelDownloadResult result = await downloader
            .DownloadAsync(Tiny(), destination, null, CancellationToken.None);

        Assert.Equal(ModelDownloadStatus.Corrupted, result.Status);

        // **完成した名前のファイルを作らない。**
        // 作ると次の起動で「モデルがある」と判定され、壊れたモデルを読もうとする。
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + WhisperModelDownloader.PartialExtension));
    }

    [Fact]
    public async Task ハッシュが合わなければ壊れている扱いにする()
    {
        using WhisperModelDownloader downloader = Create(_ => Ok(new byte[2000]));

        string destination = this.Destination("ggml-tiny.bin");
        ModelDownloadResult result = await downloader
            .DownloadAsync(Tiny(sha: new string('0', 64)), destination, null, CancellationToken.None);

        Assert.Equal(ModelDownloadStatus.Corrupted, result.Status);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task 途中のファイルがあれば続きから求める()
    {
        string destination = this.Destination("ggml-tiny.bin");
        File.WriteAllBytes(destination + WhisperModelDownloader.PartialExtension, new byte[700]);

        long? requestedFrom = null;
        using WhisperModelDownloader downloader = Create(request =>
        {
            requestedFrom = request.Headers.Range?.Ranges.FirstOrDefault()?.From;
            return Partial(new byte[1300]);
        });

        ModelDownloadResult result = await downloader
            .DownloadAsync(Tiny(), destination, null, CancellationToken.None);

        Assert.Equal(700, requestedFrom);
        Assert.True(result.Succeeded);

        // 700（既にある）＋ 1300（続き）＝ 2000。
        Assert.Equal(2000, new FileInfo(destination).Length);
    }

    [Fact]
    public async Task サーバが続きを扱えなければ最初から落とし直す()
    {
        // **黙って続きに足さない。** 足すと壊れたファイルができる。
        string destination = this.Destination("ggml-tiny.bin");
        File.WriteAllBytes(destination + WhisperModelDownloader.PartialExtension, new byte[700]);

        using WhisperModelDownloader downloader = Create(_ => Ok(new byte[2000]));

        ModelDownloadResult result = await downloader
            .DownloadAsync(Tiny(), destination, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(2000, new FileInfo(destination).Length);
    }

    [Fact]
    public async Task 失敗しても例外にしない()
    {
        using WhisperModelDownloader downloader = Create(_ => throw new HttpRequestException("切れました"));

        ModelDownloadResult result = await downloader
            .DownloadAsync(Tiny(), this.Destination("ggml-tiny.bin"), null, CancellationToken.None);

        Assert.Equal(ModelDownloadStatus.Network, result.Status);
    }

    [Fact]
    public async Task 進捗を報告する()
    {
        using WhisperModelDownloader downloader = Create(_ => Ok(new byte[2000]));

        // **Progress<T> を使わない。** あれは報告を別スレッドへ投げるので、
        // 受け取り終わるのを時間で待つことになる。
        // 待ち時間で判定するテストは、負荷が高いときに落ちる（実際に 1 度落ちた）。
        SynchronousProgress reports = new();

        await downloader.DownloadAsync(
            Tiny(),
            this.Destination("ggml-tiny.bin"),
            reports,
            CancellationToken.None);

        Assert.NotEmpty(reports.Reports);
        Assert.Equal(2000, reports.Reports[^1].ReceivedBytes);
    }

    /// <summary>その場で記録する <see cref="IProgress{T}"/>。時間に頼らないため。</summary>
    private sealed class SynchronousProgress : IProgress<ModelDownloadProgress>
    {
        public List<ModelDownloadProgress> Reports { get; } = [];

        public void Report(ModelDownloadProgress value) => this.Reports.Add(value);
    }

    private static HttpResponseMessage Ok(byte[] payload) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };

    private static HttpResponseMessage Partial(byte[] payload) =>
        new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(payload) };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => this.respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(this.respond(request));
    }
}

/// <summary>claude -p のお試し（D-03 の (2) / ADR-0029 決定 1）。</summary>
public sealed class ClaudeUsabilityProbeTests
{
    private static ClaudeExecutable Native => new(@"C:\bin\claude.exe", false, "2.1.0");

    private static ClaudeExecutable Cmd => new(@"C:\npm\claude.cmd", true, "2.1.0");

    [Fact]
    public void 解析の経路と分かれている()
    {
        // ADR-0029 影響：本番の起動行に確認用の分岐を入れない。
        Assert.True(typeof(IClaudeProbe).IsAssignableFrom(typeof(ClaudeUsabilityProbe)));
        Assert.False(typeof(IAnalysisEngine).IsAssignableFrom(typeof(ClaudeUsabilityProbe)));
    }

    [Fact]
    public void ツールを与えない()
    {
        // 確認のつもりの実行が、書き込みやシェルを動かせる状態にならないようにする。
        IList<string> arguments = ClaudeUsabilityProbe.BuildStartInfo(Native).ArgumentList;

        int index = arguments.IndexOf("--tools");
        Assert.True(index >= 0);
        Assert.Equal(string.Empty, arguments[index + 1]);
        Assert.Contains("--permission-prompts", arguments);
        Assert.Contains("none", arguments);
    }

    [Fact]
    public void ターン数を一に絞る()
    {
        // 利用枠の消費を最小にする。
        IList<string> arguments = ClaudeUsabilityProbe.BuildStartInfo(Native).ArgumentList;

        int index = arguments.IndexOf("--max-turns");
        Assert.True(index >= 0);
        Assert.Equal("1", arguments[index + 1]);
    }

    [Fact]
    public void cmd版はcmd経由で起動する()
    {
        System.Diagnostics.ProcessStartInfo info = ClaudeUsabilityProbe.BuildStartInfo(Cmd);

        Assert.Equal("cmd.exe", info.FileName);
        Assert.Equal("/c", info.ArgumentList[0]);
        Assert.Equal(@"C:\npm\claude.cmd", info.ArgumentList[1]);
    }

    [Fact]
    public void ネイティブ版は直接起動する()
    {
        Assert.Equal(@"C:\bin\claude.exe", ClaudeUsabilityProbe.BuildStartInfo(Native).FileName);
    }
}
