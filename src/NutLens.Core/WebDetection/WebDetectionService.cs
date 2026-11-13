using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Analysis;
using NutLens.Core.Sampling;
using NutLens.Core.Security;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.Core.WebDetection;

/// <summary>
/// Web Detection を 1 回走らせる役（F-14 / 5.6 / ADR-0022）。
///
/// <b><see cref="Media.IMediaProcessingService"/> 側に混ぜない。</b>
/// あちらは「文字起こしと OCR を行う役」で、網の外へ出る処理を混ぜると
/// どのクラスが通信するのかが読めなくなる（D-08）。
/// </summary>
public interface IWebDetectionService
{
    /// <summary>
    /// 条件が揃っていなければ何もせず null を返す（＝送らない）。
    /// 送った場合は <c>web_detection.json</c> を書いて顛末を返す。
    ///
    /// <b>例外を投げない。</b>補助情報が取れなかっただけで解析を止めない。
    /// </summary>
    Task<WebDetectionOutcome?> RunAsync(
        SessionLayout layout,
        NutLensSettings settings,
        IList<string> warnings,
        CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class WebDetectionService : IWebDetectionService
{
    private readonly IWebDetectionClient client;
    private readonly ISecretStore secrets;
    private readonly IFrameFileLister frameLister;
    private readonly IFrameThinner thinner;

    public WebDetectionService(
        IWebDetectionClient client,
        ISecretStore secrets,
        IFrameFileLister frameLister,
        IFrameThinner thinner)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(frameLister);
        ArgumentNullException.ThrowIfNull(thinner);

        this.client = client;
        this.secrets = secrets;
        this.frameLister = frameLister;
        this.thinner = thinner;
    }

    public async Task<WebDetectionOutcome?> RunAsync(
        SessionLayout layout,
        NutLensSettings settings,
        IList<string> warnings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(warnings);

        WebDetectionSettings options = (settings.WebDetection ?? new WebDetectionSettings()).Normalize();

        // 鍵は「あるかどうか」を見る段で読み、使い終わったら捨てる。
        // 変数に長く置かない（ADR-0020）。
        string? apiKey = options.Enabled ? this.secrets.TryRead(SecretNames.GoogleCloudVisionApiKey) : null;

        if (!WebDetectionGate.CanSend(options, !string.IsNullOrWhiteSpace(apiKey)))
        {
            AddSkipWarning(warnings, WebDetectionGate.DescribeSkip(options, !string.IsNullOrWhiteSpace(apiKey)));
            return null;
        }

        IReadOnlyList<WebDetectionImage> images = this.SelectImages(layout, options.MaxImages);
        if (images.Count == 0)
        {
            warnings.Add("Web Detection に送るフレームがありませんでした。");
            return null;
        }

        WebDetectionOutcome outcome = await this.client
            .DetectAsync(images, apiKey!, options.Timeout, cancellationToken)
            .ConfigureAwait(false);

        TryWrite(layout.WebDetectionFile, outcome);

        if (!outcome.Succeeded)
        {
            warnings.Add($"Web Detection は使えませんでした（{outcome.Message}）。解析は続けました。");
        }

        return outcome;
    }

    /// <summary>
    /// 代表フレームを選ぶ。<b>新しい選び方を作らない</b>（ADR-0022 決定 1）。
    /// 間引き器は最初と最後を必ず残し、既にテストがある。
    /// </summary>
    private IReadOnlyList<WebDetectionImage> SelectImages(SessionLayout layout, int maxImages)
    {
        IReadOnlyList<FrameReference> frames = this.frameLister.List(layout);
        if (frames.Count == 0)
        {
            return [];
        }

        IReadOnlyList<FrameReference> selected = this.thinner.Thin(frames, maxImages);

        List<WebDetectionImage> images = new(selected.Count);
        foreach (FrameReference frame in selected)
        {
            string path = Path.Combine(
                layout.Root,
                frame.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            byte[]? bytes = TryReadAllBytes(path);
            if (bytes is not null)
            {
                images.Add(new WebDetectionImage(frame.RelativePath, frame.Elapsed, bytes));
            }
        }

        return images;
    }

    private static void AddSkipWarning(IList<string> warnings, WebDetectionSkipReason? reason)
    {
        switch (reason)
        {
            case WebDetectionSkipReason.ConsentOutdated:
                warnings.Add("Web Detection の説明が更新されました。もう一度同意してください。");
                break;

            case WebDetectionSkipReason.NoApiKey:
                warnings.Add("Web Detection の API キーが設定されていません。");
                break;

            // Disabled は既定の状態なので画面に出さない（詳細設計 05 §3.2）。
            default:
                break;
        }
    }

    private static byte[]? TryReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryWrite(string path, WebDetectionOutcome outcome)
    {
        try
        {
            WebDetectionDocument.Write(path, outcome);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 書けなくても解析は続ける。
        }
    }
}
