using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Settings;

namespace NutLens.Core.WebDetection;

/// <summary>Vision API へ送る 1 枚（ADR-0019 決定 3：まとめて 1 往復で送る）。</summary>
public sealed record WebDetectionImage(string RelativePath, TimeSpan Elapsed, byte[] JpegBytes);

/// <summary>
/// 顛末。<b>失敗の種類を分ける</b>のは、利用者が次に何をすればよいかが違うため
/// （ADR-0022 決定 5）。
/// </summary>
public enum WebDetectionStatus
{
    /// <summary>応答が返り、読めた。</summary>
    Ok,

    /// <summary>名前解決・接続できない、タイムアウト。</summary>
    Network,

    /// <summary>HTTP 401 / 403。鍵が拒否された。</summary>
    Auth,

    /// <summary>HTTP 429 または RESOURCE_EXHAUSTED。利用上限。</summary>
    Quota,

    /// <summary>HTTP 400、応答が読めない。</summary>
    Invalid,
}

/// <summary>Web Detection が推測した実体。</summary>
public sealed record WebEntity(string Description, double? Score);

/// <summary>同じ画像が載っているページ（5.6 が挙げた 3 つのうちの 1 つ）。</summary>
public sealed record WebPageMatch(string Title, string Url);

/// <summary>フレーム 1 枚分の結果。</summary>
public sealed record WebDetectionFrameResult(
    string File,
    double ElapsedSeconds,
    IReadOnlyList<string> BestGuessLabels,
    IReadOnlyList<WebEntity> WebEntities,
    IReadOnlyList<WebPageMatch> PagesWithMatchingImages);

/// <summary>
/// 1 回の Web Detection の顛末。これがそのまま <c>web_detection.json</c> になる
/// （ADR-0022 決定 4：成功も失敗も同じ 1 ファイルに残す）。
/// </summary>
public sealed record WebDetectionOutcome(
    WebDetectionStatus Status,
    int SentImageCount,
    IReadOnlyList<WebDetectionFrameResult> Frames,
    string? Message)
{
    public static WebDetectionOutcome Failed(WebDetectionStatus status, string message) =>
        new(status, 0, [], message);

    public bool Succeeded => this.Status == WebDetectionStatus.Ok;
}

/// <summary>
/// Vision API を呼ぶ役（F-14 / ADR-0019）。実装は <c>NutLens.Analysis</c>。
///
/// <b>例外を投げない。</b>失敗は <see cref="WebDetectionStatus"/> で返す。
/// 補助情報が取れなかっただけで解析を止めない（概要設計 §10.1）。
/// </summary>
public interface IWebDetectionClient
{
    /// <param name="apiKey">
    /// 呼ぶ側が <see cref="Security.ISecretStore"/> から読んで渡す。
    /// <b>クライアントは鍵の出どころを知らない</b>（ADR-0020 決定 3：
    /// 資格情報マネージャーは Windows 専用で、この実装は net10.0）。
    /// </param>
    Task<WebDetectionOutcome> DetectAsync(
        IReadOnlyList<WebDetectionImage> images,
        string apiKey,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

/// <summary>
/// 送ってよいかの判定（ADR-0021 決定 1）。
///
/// <b>3 つの AND。</b>1 つでも欠ければ送らない。
/// <c>settings.json</c> を手で書き換えて <c>Enabled</c> と <c>ConsentVersion</c> を
/// 揃えても、<b>API キーは settings.json に存在しない</b>ので 3 つめが成立しない。
/// 検査を足して弾くのではなく、手書きでは揃えられない条件を混ぜている。
/// </summary>
public static class WebDetectionGate
{
    public static bool CanSend(WebDetectionSettings settings, bool hasApiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Enabled
            && settings.ConsentVersion == WebDetectionSettings.CurrentConsentVersion
            && hasApiKey;
    }

    /// <summary>
    /// 送らない理由。送れるときは null。
    ///
    /// <b>黙って何もしない、をしない。</b>
    /// 「有効にしたのに動かない」が原因不明になる（詳細設計 05 §3.2）。
    /// </summary>
    public static WebDetectionSkipReason? DescribeSkip(WebDetectionSettings settings, bool hasApiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Enabled)
        {
            // 使わない設定なので正常。画面に出さない。
            return WebDetectionSkipReason.Disabled;
        }

        if (settings.ConsentVersion != WebDetectionSettings.CurrentConsentVersion)
        {
            return WebDetectionSkipReason.ConsentOutdated;
        }

        return hasApiKey ? null : WebDetectionSkipReason.NoApiKey;
    }
}

/// <summary>送らなかった理由。</summary>
public enum WebDetectionSkipReason
{
    /// <summary>設定でオフ。<b>これだけは画面に出さない</b>（既定の状態なので）。</summary>
    Disabled,

    /// <summary>同意文が更新された。もう一度同意が要る（ADR-0021 決定 4）。</summary>
    ConsentOutdated,

    /// <summary>鍵が資格情報マネージャーに無い。</summary>
    NoApiKey,
}
