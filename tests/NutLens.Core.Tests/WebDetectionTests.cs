using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Analysis;
using NutLens.Core.Analysis;
using NutLens.Core.Sampling;
using NutLens.Core.Security;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using NutLens.Core.WebDetection;
using Xunit;

namespace NutLens.Core.Tests;

/// <summary>
/// 送ってよいかの判定（ADR-0021 決定 1）。
///
/// <b>ここが一番壊してはいけない。</b>
/// この判定が緩むと、同意していない利用者の画面が Google へ送られる。
/// </summary>
public sealed class WebDetectionGateTests
{
    [Fact]
    public void 既定では送らない()
    {
        WebDetectionSettings settings = new();

        Assert.False(settings.Enabled);
        Assert.False(WebDetectionGate.CanSend(settings, hasApiKey: true));
    }

    [Fact]
    public void 有効の印だけ立てても送らない()
    {
        // settings.json を手で書き換えた状況。consentVersion は 0 のまま。
        WebDetectionSettings settings = new() { Enabled = true };

        Assert.False(WebDetectionGate.CanSend(settings, hasApiKey: true));
        Assert.Equal(WebDetectionSkipReason.ConsentOutdated, WebDetectionGate.DescribeSkip(settings, true));
    }

    [Fact]
    public void 同意の版が古いと送らない()
    {
        WebDetectionSettings settings = new()
        {
            Enabled = true,
            ConsentVersion = WebDetectionSettings.CurrentConsentVersion - 1,
        };

        Assert.False(WebDetectionGate.CanSend(settings, hasApiKey: true));
    }

    [Fact]
    public void 鍵が無いと送らない()
    {
        WebDetectionSettings settings = new()
        {
            Enabled = true,
            ConsentVersion = WebDetectionSettings.CurrentConsentVersion,
        };

        Assert.False(WebDetectionGate.CanSend(settings, hasApiKey: false));
        Assert.Equal(WebDetectionSkipReason.NoApiKey, WebDetectionGate.DescribeSkip(settings, false));
    }

    [Fact]
    public void 三つ揃えば送る()
    {
        WebDetectionSettings settings = new()
        {
            Enabled = true,
            ConsentVersion = WebDetectionSettings.CurrentConsentVersion,
        };

        Assert.True(WebDetectionGate.CanSend(settings, hasApiKey: true));
        Assert.Null(WebDetectionGate.DescribeSkip(settings, true));
    }

    [Fact]
    public void オフのときは理由を画面に出さない()
    {
        // 「使わない設定」は正常な状態。警告を出すと、使っていない利用者に毎回出る。
        Assert.Equal(
            WebDetectionSkipReason.Disabled,
            WebDetectionGate.DescribeSkip(new WebDetectionSettings(), hasApiKey: false));
    }
}

/// <summary>web_detection.json の読み書き（ADR-0022 決定 3・4・6）。</summary>
public sealed class WebDetectionDocumentTests : IDisposable
{
    private readonly string root;

    public WebDetectionDocumentTests()
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

    private string Path_(string name) => System.IO.Path.Combine(this.root, name);

    [Fact]
    public void 書いて読むと同じ()
    {
        string path = this.Path_("web_detection.json");
        WebDetectionOutcome written = new(
            WebDetectionStatus.Ok,
            2,
            [
                new WebDetectionFrameResult(
                    "frames/frame_001_t0000.0s.jpg",
                    0.0,
                    ["movie scene"],
                    [new WebEntity("タクシー", 0.72)],
                    [new WebPageMatch("ある映画", "https://example.com/a")]),
            ],
            Message: null);

        WebDetectionDocument.Write(path, written);
        WebDetectionOutcome? read = WebDetectionDocument.TryRead(path);

        Assert.NotNull(read);
        Assert.Equal(WebDetectionStatus.Ok, read.Status);
        Assert.Equal(2, read.SentImageCount);
        Assert.Equal("タクシー", read.Frames[0].WebEntities[0].Description);
        Assert.Equal("https://example.com/a", read.Frames[0].PagesWithMatchingImages[0].Url);
    }

    [Fact]
    public void 失敗も書き残す()
    {
        // 「使っていない」と「試したが失敗した」を結果画面が区別できるようにするため。
        string path = this.Path_("web_detection.json");
        WebDetectionDocument.Write(
            path,
            WebDetectionOutcome.Failed(WebDetectionStatus.Quota, "利用上限に達しました"));

        WebDetectionOutcome? read = WebDetectionDocument.TryRead(path);

        Assert.NotNull(read);
        Assert.Equal(WebDetectionStatus.Quota, read.Status);
        Assert.False(read.Succeeded);
    }

    [Fact]
    public void 失敗した回はプロンプトに差し込まない()
    {
        string path = this.Path_("web_detection.json");
        WebDetectionDocument.Write(
            path,
            WebDetectionOutcome.Failed(WebDetectionStatus.Auth, "API キーが拒否されました"));

        Assert.Null(WebDetectionDocument.TryReadReference(path));
    }

    [Fact]
    public void 成功した回だけプロンプトに差し込む()
    {
        string path = this.Path_("web_detection.json");
        WebDetectionDocument.Write(
            path,
            new WebDetectionOutcome(
                WebDetectionStatus.Ok,
                1,
                [new WebDetectionFrameResult("frames/a.jpg", 1.5, ["taxi"], [], [])],
                Message: null));

        string? reference = WebDetectionDocument.TryReadReference(path);

        Assert.NotNull(reference);
        Assert.Contains("taxi", reference, StringComparison.Ordinal);

        // 誤りを含みうることは、参考情報そのものに書いておく（5.6）。
        Assert.Contains("誤りを含む可能性がある", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void ファイルが無ければ何も返さない()
    {
        Assert.Null(WebDetectionDocument.TryRead(this.Path_("ない.json")));
        Assert.Null(WebDetectionDocument.TryReadReference(this.Path_("ない.json")));
    }

    [Fact]
    public void 壊れたJSONでも落ちない()
    {
        string path = this.Path_("web_detection.json");
        File.WriteAllText(path, "{ これは JSON ではない");

        Assert.Null(WebDetectionDocument.TryRead(path));
    }
}

/// <summary>取り仕切り役（詳細設計 05 §8）。</summary>
public sealed class WebDetectionServiceTests : IDisposable
{
    private readonly string root;
    private readonly SessionLayout layout;

    public WebDetectionServiceTests()
    {
        this.root = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.root);

        AppDataPaths paths = new(this.root);
        FileSystemSessionWorkspace workspace = new(paths);
        this.layout = workspace.Create(SessionId.New());
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private void WriteFrames(int count)
    {
        for (int index = 1; index <= count; index++)
        {
            string name = SessionLayout.FrameFileName(index, TimeSpan.FromSeconds(index * 2.0), ".jpg");
            File.WriteAllBytes(Path.Combine(this.layout.FramesDirectory, name), [1, 2, 3, 4]);
        }
    }

    private static NutLensSettings Allowed(int maxImages = 3) => new()
    {
        WebDetection = new WebDetectionSettings
        {
            Enabled = true,
            ConsentVersion = WebDetectionSettings.CurrentConsentVersion,
            MaxImages = maxImages,
        },
    };

    private WebDetectionService Create(FakeWebDetectionClient client, string? apiKey) => new(
        client,
        new FakeSecretStore(apiKey),
        new FileSystemFrameLister(),
        new EvenTimeFrameThinner());

    [Fact]
    public async Task 既定では一度も呼ばない()
    {
        this.WriteFrames(5);
        FakeWebDetectionClient client = new();
        List<string> warnings = [];

        WebDetectionOutcome? outcome = await this
            .Create(client, "key")
            .RunAsync(this.layout, new NutLensSettings(), warnings, CancellationToken.None);

        Assert.Null(outcome);
        Assert.Equal(0, client.CallCount);
        Assert.False(File.Exists(this.layout.WebDetectionFile));

        // オフは正常な状態なので、警告も出さない。
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task 鍵が無ければ呼ばずに理由を残す()
    {
        this.WriteFrames(5);
        FakeWebDetectionClient client = new();
        List<string> warnings = [];

        WebDetectionOutcome? outcome = await this
            .Create(client, apiKey: null)
            .RunAsync(this.layout, Allowed(), warnings, CancellationToken.None);

        Assert.Null(outcome);
        Assert.Equal(0, client.CallCount);
        Assert.Single(warnings);
    }

    [Fact]
    public async Task 上限枚数まで間引いて送る()
    {
        this.WriteFrames(10);
        FakeWebDetectionClient client = new();

        WebDetectionOutcome? outcome = await this
            .Create(client, "key")
            .RunAsync(this.layout, Allowed(maxImages: 3), [], CancellationToken.None);

        Assert.NotNull(outcome);
        Assert.Equal(1, client.CallCount);
        Assert.Equal(3, client.LastImages.Count);
        Assert.Equal(3, outcome.SentImageCount);
    }

    [Fact]
    public async Task 最初と最後のフレームが必ず入る()
    {
        // 間引き器の性質をそのまま使っている（ADR-0022 決定 1）。
        this.WriteFrames(10);
        FakeWebDetectionClient client = new();

        await this
            .Create(client, "key")
            .RunAsync(this.layout, Allowed(maxImages: 3), [], CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(2), client.LastImages[0].Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(20), client.LastImages[^1].Elapsed);
    }

    [Fact]
    public async Task 枚数がしきい値以下ならそのまま送る()
    {
        this.WriteFrames(2);
        FakeWebDetectionClient client = new();

        await this
            .Create(client, "key")
            .RunAsync(this.layout, Allowed(maxImages: 3), [], CancellationToken.None);

        Assert.Equal(2, client.LastImages.Count);
    }

    [Fact]
    public async Task 成功したらファイルを書く()
    {
        this.WriteFrames(3);
        FakeWebDetectionClient client = new();

        await this
            .Create(client, "key")
            .RunAsync(this.layout, Allowed(), [], CancellationToken.None);

        Assert.True(File.Exists(this.layout.WebDetectionFile));
        Assert.NotNull(WebDetectionDocument.TryReadReference(this.layout.WebDetectionFile));
    }

    [Fact]
    public async Task 失敗しても例外にせず書き残して続ける()
    {
        this.WriteFrames(3);
        FakeWebDetectionClient client = new(
            WebDetectionOutcome.Failed(WebDetectionStatus.Network, "ネットワークに繋がりませんでした"));
        List<string> warnings = [];

        WebDetectionOutcome? outcome = await this
            .Create(client, "key")
            .RunAsync(this.layout, Allowed(), warnings, CancellationToken.None);

        Assert.NotNull(outcome);
        Assert.False(outcome.Succeeded);
        Assert.True(File.Exists(this.layout.WebDetectionFile));
        Assert.Single(warnings);

        // 失敗した回はプロンプトに差し込まない。
        Assert.Null(WebDetectionDocument.TryReadReference(this.layout.WebDetectionFile));
    }

    [Fact]
    public async Task フレームが無ければ呼ばない()
    {
        FakeWebDetectionClient client = new();
        List<string> warnings = [];

        WebDetectionOutcome? outcome = await this
            .Create(client, "key")
            .RunAsync(this.layout, Allowed(), warnings, CancellationToken.None);

        Assert.Null(outcome);
        Assert.Equal(0, client.CallCount);
        Assert.Single(warnings);
    }
}

/// <summary>Vision API の要求の組み立てと応答の読み取り（ADR-0019）。</summary>
public sealed class VisionWebDetectionClientTests
{
    private static IReadOnlyList<WebDetectionImage> Images(int count)
    {
        List<WebDetectionImage> images = new(count);
        for (int index = 0; index < count; index++)
        {
            images.Add(new WebDetectionImage(
                $"frames/frame_{index:D3}.jpg",
                TimeSpan.FromSeconds(index),
                [(byte)index, 2, 3]));
        }

        return images;
    }

    [Fact]
    public void 要求する機能はWebDetectionだけ()
    {
        // FACE_DETECTION は顔の検出そのもので、要件が明示的に禁じている。
        VisionAnnotateRequest request = VisionWebDetectionClient.BuildRequest(Images(3));

        Assert.All(request.Requests, item =>
        {
            Assert.Single(item.Features);
            Assert.Equal("WEB_DETECTION", item.Features[0].Type);
        });
    }

    [Fact]
    public void 複数枚を一つの要求に並べる()
    {
        // 1 枚ずつ送ると、失敗の回数だけ課金される。
        VisionAnnotateRequest request = VisionWebDetectionClient.BuildRequest(Images(3));

        Assert.Equal(3, request.Requests.Count);
    }

    [Fact]
    public void 応答から三つだけ拾う()
    {
        VisionAnnotateResponse body = new(
            [
                new VisionImageResponse(
                    new VisionWebDetection(
                        [new VisionWebEntity("/m/01", "タクシー", 0.8), new VisionWebEntity("/m/02", null, 0.1)],
                        [new VisionWebPage("https://example.com/a", "ある映画")],
                        [new VisionBestGuessLabel("movie scene", "en")]),
                    Error: null),
            ],
            Error: null);

        WebDetectionOutcome outcome = VisionWebDetectionClient.Read(Images(1), body);

        Assert.Equal(WebDetectionStatus.Ok, outcome.Status);
        Assert.Equal("movie scene", outcome.Frames[0].BestGuessLabels[0]);

        // description が無い項目は捨てる（entityId だけでは誰も読めない）。
        Assert.Single(outcome.Frames[0].WebEntities);
        Assert.Equal("ある映画", outcome.Frames[0].PagesWithMatchingImages[0].Title);
    }

    [Fact]
    public void 題名の無いページはURLで代用する()
    {
        VisionAnnotateResponse body = new(
            [
                new VisionImageResponse(
                    new VisionWebDetection(null, [new VisionWebPage("https://example.com/b", null)], null),
                    Error: null),
            ],
            Error: null);

        WebDetectionOutcome outcome = VisionWebDetectionClient.Read(Images(1), body);

        Assert.Equal("https://example.com/b", outcome.Frames[0].PagesWithMatchingImages[0].Title);
    }

    [Fact]
    public void 中身が空でも落ちない()
    {
        VisionAnnotateResponse body = new([new VisionImageResponse(null, null)], Error: null);

        WebDetectionOutcome outcome = VisionWebDetectionClient.Read(Images(1), body);

        Assert.Equal(WebDetectionStatus.Ok, outcome.Status);
        Assert.Empty(outcome.Frames[0].WebEntities);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, WebDetectionStatus.Auth)]
    [InlineData(HttpStatusCode.Forbidden, WebDetectionStatus.Auth)]
    [InlineData(HttpStatusCode.TooManyRequests, WebDetectionStatus.Quota)]
    [InlineData(HttpStatusCode.BadRequest, WebDetectionStatus.Invalid)]
    [InlineData(HttpStatusCode.InternalServerError, WebDetectionStatus.Network)]
    [InlineData(HttpStatusCode.ServiceUnavailable, WebDetectionStatus.Network)]
    public void HTTPの状態を失敗の種類に振り分ける(HttpStatusCode status, WebDetectionStatus expected)
    {
        Assert.Equal(expected, VisionWebDetectionClient.FromHttpStatus(status).Status);
    }

    [Theory]
    [InlineData("RESOURCE_EXHAUSTED", WebDetectionStatus.Quota)]
    [InlineData("UNAUTHENTICATED", WebDetectionStatus.Auth)]
    [InlineData("PERMISSION_DENIED", WebDetectionStatus.Auth)]
    public void 本文のエラーも振り分ける(string status, WebDetectionStatus expected)
    {
        Assert.Equal(expected, VisionWebDetectionClient.FromVisionError(new VisionError(429, "…", status)).Status);
    }

    [Fact]
    public void 失敗の文に鍵やURLを混ぜない()
    {
        // 端点の URL にはクエリ文字列として API キーが入っている（ADR-0020 決定 4）。
        foreach (HttpStatusCode status in new[]
        {
            HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError,
        })
        {
            string message = VisionWebDetectionClient.FromHttpStatus(status).Message ?? string.Empty;

            Assert.DoesNotContain("key=", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("googleapis.com", message, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>偽の Vision。通信しない。</summary>
internal sealed class FakeWebDetectionClient : IWebDetectionClient
{
    private readonly WebDetectionOutcome? forced;

    public FakeWebDetectionClient(WebDetectionOutcome? forced = null) => this.forced = forced;

    public int CallCount { get; private set; }

    public IReadOnlyList<WebDetectionImage> LastImages { get; private set; } = [];

    public string? LastApiKey { get; private set; }

    public Task<WebDetectionOutcome> DetectAsync(
        IReadOnlyList<WebDetectionImage> images,
        string apiKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        this.CallCount++;
        this.LastImages = images;
        this.LastApiKey = apiKey;

        if (this.forced is WebDetectionOutcome outcome)
        {
            return Task.FromResult(outcome);
        }

        List<WebDetectionFrameResult> frames = images
            .Select(image => new WebDetectionFrameResult(
                image.RelativePath,
                image.Elapsed.TotalSeconds,
                ["テスト"],
                [],
                []))
            .ToList();

        return Task.FromResult(new WebDetectionOutcome(
            WebDetectionStatus.Ok,
            images.Count,
            frames,
            Message: null));
    }
}

/// <summary>資格情報マネージャーに触らない秘密の置き場。</summary>
internal sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> values = [];

    public FakeSecretStore(string? apiKey = null)
    {
        if (apiKey is not null)
        {
            this.values[SecretNames.GoogleCloudVisionApiKey] = apiKey;
        }
    }

    public string? TryRead(string name) => this.values.GetValueOrDefault(name);

    public void Write(string name, string secret) => this.values[name] = secret;

    public void Delete(string name) => this.values.Remove(name);
}
