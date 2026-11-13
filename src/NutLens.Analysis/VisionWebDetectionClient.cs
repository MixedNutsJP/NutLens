using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.WebDetection;

namespace NutLens.Analysis;

/// <summary>
/// Google Cloud Vision の Web Detection を REST で直接呼ぶ（F-14 / 5.6 / ADR-0019）。
///
/// <b>ここと <see cref="ClaudeCliAnalysisEngine"/> だけが外へ出る</b>（D-08）。
/// プロジェクト参照のグラフを見れば、外部送信の経路が 2 つしかないことが分かる（ADR-0011）。
///
/// <b>例外を外へ出さない。</b>失敗は <see cref="WebDetectionStatus"/> で返す。
/// HTTP の例外文には要求 URI が入ることがあり、
/// 端点の URL にはクエリ文字列として API キーが入っている（ADR-0020 決定 4）。
/// </summary>
public sealed class VisionWebDetectionClient : IWebDetectionClient, IDisposable
{
    /// <summary>
    /// 端点。<b>設定で変えられるようにしない。</b>
    /// 送り先を設定ファイルで差し替えられると、「外部送信は 2 経路だけ」が崩れる（D-08）。
    /// </summary>
    private const string Endpoint = "https://vision.googleapis.com/v1/images:annotate";

    /// <summary>要求する機能。<b>これ以外を足さない</b>（ADR-0019 決定 2）。</summary>
    internal const string WebDetectionFeature = "WEB_DETECTION";

    internal const int MaxResultsPerFeature = 10;

    private readonly HttpClient http;
    private readonly bool ownsHttpClient;

    public VisionWebDetectionClient()
        : this(new HttpClient(), ownsHttpClient: true)
    {
    }

    /// <summary>テストから偽の <see cref="HttpMessageHandler"/> を差し込むための口。</summary>
    public VisionWebDetectionClient(HttpClient http, bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(http);

        this.http = http;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<WebDetectionOutcome> DetectAsync(
        IReadOnlyList<WebDetectionImage> images,
        string apiKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        if (images.Count == 0)
        {
            return WebDetectionOutcome.Failed(WebDetectionStatus.Invalid, "送るフレームがありません。");
        }

        // 打ち切りは自前の CTS で行う。HttpClient.Timeout を使うと、
        // 利用者のキャンセルと時間切れが同じ例外で来て区別できない。
        using CancellationTokenSource timeoutSource = new(timeout);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            return await this
                .SendAsync(images, apiKey, linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 利用者が止めた。これは失敗ではないので、呼び出し側へ伝える。
            throw;
        }
        catch (OperationCanceledException)
        {
            return WebDetectionOutcome.Failed(
                WebDetectionStatus.Network,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "応答がありませんでした（{0:0} 秒で打ち切りました）",
                    timeout.TotalSeconds));
        }
        catch (HttpRequestException)
        {
            // 例外の中身を外へ出さない。要求 URI が含まれることがある。
            return WebDetectionOutcome.Failed(
                WebDetectionStatus.Network,
                "ネットワークに繋がりませんでした");
        }
        catch (JsonException)
        {
            return WebDetectionOutcome.Failed(
                WebDetectionStatus.Invalid,
                "応答を読み取れませんでした");
        }
    }

    private async Task<WebDetectionOutcome> SendAsync(
        IReadOnlyList<WebDetectionImage> images,
        string apiKey,
        CancellationToken cancellationToken)
    {
        VisionAnnotateRequest request = BuildRequest(images);

        // 鍵はクエリ文字列に乗る。この Uri を例外・ログ・画面のどこにも出さない。
        string url = $"{Endpoint}?key={Uri.EscapeDataString(apiKey)}";

        using HttpResponseMessage response = await this.http
            .PostAsJsonAsync(url, request, VisionJsonContext.Default.VisionAnnotateRequest, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return FromHttpStatus(response.StatusCode);
        }

        VisionAnnotateResponse? body = await response.Content
            .ReadFromJsonAsync(VisionJsonContext.Default.VisionAnnotateResponse, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return WebDetectionOutcome.Failed(WebDetectionStatus.Invalid, "応答が空でした");
        }

        // HTTP 200 でも本文に error が入ることがある（Google 標準のエラー表現）。
        if (body.Error is VisionError topLevel)
        {
            return FromVisionError(topLevel);
        }

        return Read(images, body);
    }

    /// <summary>
    /// 要求を組み立てる。<b>3 枚を 1 リクエストに並べる</b>（ADR-0019 決定 3）。
    /// 1 枚ずつ送ると、1 枚目で認証エラーが出ても残りを送ってしまい、失敗の回数だけ課金される。
    /// </summary>
    internal static VisionAnnotateRequest BuildRequest(IReadOnlyList<WebDetectionImage> images)
    {
        List<VisionImageRequest> requests = new(images.Count);
        foreach (WebDetectionImage image in images)
        {
            requests.Add(new VisionImageRequest(
                new VisionImage(Convert.ToBase64String(image.JpegBytes)),
                [new VisionFeature(WebDetectionFeature, MaxResultsPerFeature)]));
        }

        return new VisionAnnotateRequest(requests);
    }

    /// <summary>応答から 5.6 が挙げた 3 つだけを拾う。</summary>
    internal static WebDetectionOutcome Read(
        IReadOnlyList<WebDetectionImage> images,
        VisionAnnotateResponse body)
    {
        IReadOnlyList<VisionImageResponse> responses = body.Responses ?? [];

        List<WebDetectionFrameResult> frames = new(images.Count);
        for (int index = 0; index < images.Count; index++)
        {
            if (index >= responses.Count)
            {
                break;
            }

            VisionImageResponse item = responses[index];

            // 画像ごとのエラーは、全体の失敗として扱う。
            // 「3 枚中 1 枚だけ認証エラー」は起こらない種類の失敗なので、分けても使い道がない。
            if (item.Error is VisionError perImage && perImage.Code is not null)
            {
                return FromVisionError(perImage);
            }

            frames.Add(new WebDetectionFrameResult(
                images[index].RelativePath,
                images[index].Elapsed.TotalSeconds,
                ReadLabels(item.WebDetection),
                ReadEntities(item.WebDetection),
                ReadPages(item.WebDetection)));
        }

        return new WebDetectionOutcome(WebDetectionStatus.Ok, images.Count, frames, Message: null);
    }

    private static IReadOnlyList<string> ReadLabels(VisionWebDetection? detection)
    {
        if (detection?.BestGuessLabels is not IReadOnlyList<VisionBestGuessLabel> labels)
        {
            return [];
        }

        List<string> result = [];
        foreach (VisionBestGuessLabel label in labels)
        {
            if (!string.IsNullOrWhiteSpace(label.Label))
            {
                result.Add(label.Label);
            }
        }

        return result;
    }

    private static IReadOnlyList<WebEntity> ReadEntities(VisionWebDetection? detection)
    {
        if (detection?.WebEntities is not IReadOnlyList<VisionWebEntity> entities)
        {
            return [];
        }

        List<WebEntity> result = [];
        foreach (VisionWebEntity entity in entities)
        {
            // description が無い項目は entityId しか持たない。利用者にも Claude にも読めない。
            if (!string.IsNullOrWhiteSpace(entity.Description))
            {
                result.Add(new WebEntity(entity.Description, entity.Score));
            }
        }

        return result;
    }

    private static IReadOnlyList<WebPageMatch> ReadPages(VisionWebDetection? detection)
    {
        if (detection?.PagesWithMatchingImages is not IReadOnlyList<VisionWebPage> pages)
        {
            return [];
        }

        List<WebPageMatch> result = [];
        foreach (VisionWebPage page in pages)
        {
            if (!string.IsNullOrWhiteSpace(page.Url))
            {
                // 題名が無いページもある。URL だけでも Claude が WebFetch で開ける。
                result.Add(new WebPageMatch(page.PageTitle ?? page.Url, page.Url));
            }
        }

        return result;
    }

    /// <summary>HTTP の状態から失敗の種類を決める（詳細設計 05 §7）。</summary>
    internal static WebDetectionOutcome FromHttpStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => WebDetectionOutcome.Failed(
            WebDetectionStatus.Auth,
            "API キーが拒否されました。キーと Vision API の有効化をご確認ください"),

        HttpStatusCode.TooManyRequests => WebDetectionOutcome.Failed(
            WebDetectionStatus.Quota,
            "利用上限に達しました"),

        // 5xx は Google 側の一時的な不調。利用者にできることは「時間を置く」で、
        // 繋がらなかったときと同じなので同じ種類にする。
        >= HttpStatusCode.InternalServerError => WebDetectionOutcome.Failed(
            WebDetectionStatus.Network,
            "Google のサーバが応答しませんでした"),

        _ => WebDetectionOutcome.Failed(
            WebDetectionStatus.Invalid,
            string.Format(CultureInfo.InvariantCulture, "要求が拒否されました（HTTP {0:D}）", (int)status)),
    };

    /// <summary>本文の error から失敗の種類を決める。HTTP 200 でも入ることがある。</summary>
    internal static WebDetectionOutcome FromVisionError(VisionError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // error.message をそのまま出さない。Google の文面に要求の中身が含まれることがある。
        return error.Status switch
        {
            "RESOURCE_EXHAUSTED" => WebDetectionOutcome.Failed(
                WebDetectionStatus.Quota,
                "利用上限に達しました"),

            "UNAUTHENTICATED" or "PERMISSION_DENIED" => WebDetectionOutcome.Failed(
                WebDetectionStatus.Auth,
                "API キーが拒否されました。キーと Vision API の有効化をご確認ください"),

            _ => error.Code is int code
                ? FromHttpStatus((HttpStatusCode)code)
                : WebDetectionOutcome.Failed(WebDetectionStatus.Invalid, "応答を読み取れませんでした"),
        };
    }

    public void Dispose()
    {
        if (this.ownsHttpClient)
        {
            this.http.Dispose();
        }
    }
}
