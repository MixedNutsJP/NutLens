using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Distribution;

namespace NutLens.Analysis;

/// <summary>
/// Whisper の ggml モデルを落とす（D-04 / ADR-0030）。
///
/// <b>外へ出る経路はこのプロジェクトだけ</b>（ADR-0011 / D-08）。
/// ただしこれは<b>受信</b>で、送るデータは無い。
///
/// <b>例外を外へ出さない。</b>失敗は <see cref="ModelDownloadStatus"/> で返す。
/// </summary>
public sealed class WhisperModelDownloader : IModelDownloader, IDisposable
{
    /// <summary>
    /// whisper.cpp 本家が配っている ggml モデル（MIT）。
    /// <b>設定で変えられるようにしない。</b>落とし先が設定ファイルで差し替えられると、
    /// 「どこから落ちてくるか」が崩れる（ADR-0030 決定 1）。
    /// </summary>
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>落としている途中であることを示す拡張子。<b>完成した名前で途中の状態を作らない。</b></summary>
    internal const string PartialExtension = ".part";

    private readonly HttpClient http;
    private readonly bool ownsHttpClient;

    public WhisperModelDownloader()
        : this(CreateDefaultClient(), ownsHttpClient: true)
    {
    }

    /// <summary>テストから偽の <see cref="HttpMessageHandler"/> を差し込むための口。</summary>
    public WhisperModelDownloader(HttpClient http, bool ownsHttpClient = false)
    {
        ArgumentNullException.ThrowIfNull(http);

        this.http = http;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<ModelDownloadResult> DownloadAsync(
        WhisperModelChoice model,
        string destinationPath,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string partialPath = destinationPath + PartialExtension;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            long resumeFrom = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            await this.ReceiveAsync(model, partialPath, resumeFrom, progress, cancellationToken)
                .ConfigureAwait(false);

            ModelDownloadResult verified = await VerifyAsync(model, partialPath, cancellationToken)
                .ConfigureAwait(false);

            if (!verified.Succeeded)
            {
                // 壊れたものは残さない。残すと「再開」で壊れた続きを足すことになる。
                TryDelete(partialPath);
                return verified;
            }

            // **照合が通ってからだけ、完成した名前にする**（ADR-0030 決定 3）。
            File.Move(partialPath, destinationPath, overwrite: true);
            return new ModelDownloadResult(ModelDownloadStatus.Completed, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 途中のファイルは**残す**。次に再開できるようにするため（D-04）。
            return new ModelDownloadResult(ModelDownloadStatus.Cancelled, null);
        }
        catch (HttpRequestException)
        {
            return new ModelDownloadResult(ModelDownloadStatus.Network, "ネットワークに繋がりませんでした");
        }
        catch (IOException exception)
        {
            return new ModelDownloadResult(ModelDownloadStatus.Network, exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new ModelDownloadResult(ModelDownloadStatus.Network, exception.Message);
        }
    }

    private async Task ReceiveAsync(
        WhisperModelChoice model,
        string partialPath,
        long resumeFrom,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, BaseUrl + model.FileName);

        if (resumeFrom > 0)
        {
            request.Headers.Range = new RangeHeaderValue(resumeFrom, null);
        }

        using HttpResponseMessage response = await this.http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        // Range を求めたのに 200 が返ったら、サーバは途中からを扱えない。
        // **黙って続きに足さない。** 足すと壊れたファイルができる（ADR-0030 決定 3）。
        bool append = resumeFrom > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (resumeFrom > 0 && !append)
        {
            resumeFrom = 0;
        }

        response.EnsureSuccessStatusCode();

        long? total = response.Content.Headers.ContentLength is long length
            ? length + resumeFrom
            : null;

        using FileStream file = new(
            partialPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        using Stream network = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        byte[] buffer = new byte[81920];
        long received = resumeFrom;
        progress?.Report(new ModelDownloadProgress(received, total));

        int read;
        while ((read = await network.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new ModelDownloadProgress(received, total));
        }
    }

    /// <summary>
    /// 落ちてきたものを確かめる（ADR-0030 決定 4）。
    ///
    /// <b>期待値をまだ実測していないモデルがある</b>（<c>ExpectedSha256</c> が null）。
    /// その場合はハッシュの照合を飛ばし、<b>大きさの妥当性だけ</b>を見る。
    /// 飛ばしたことを隠さない（設計書 07 §4.4 に書いてある）。
    /// </summary>
    internal static async Task<ModelDownloadResult> VerifyAsync(
        WhisperModelChoice model,
        string path,
        CancellationToken cancellationToken)
    {
        long actual = new FileInfo(path).Length;

        // 目安の半分を下回るものは、途中で切れているか別のファイル。
        // HTML のエラーページが降ってきた場合もここで落ちる。
        if (actual < model.ApproximateBytes / 2)
        {
            return new ModelDownloadResult(
                ModelDownloadStatus.Corrupted,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "落ちてきた大きさが小さすぎます（{0} バイト）。",
                    actual));
        }

        if (model.ExpectedSha256 is not string expected)
        {
            return new ModelDownloadResult(ModelDownloadStatus.Completed, null);
        }

        string actualHash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);

        return string.Equals(actualHash, expected, StringComparison.OrdinalIgnoreCase)
            ? new ModelDownloadResult(ModelDownloadStatus.Completed, null)
            : new ModelDownloadResult(ModelDownloadStatus.Corrupted, "ハッシュが一致しませんでした。");
    }

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static HttpClient CreateDefaultClient() => new()
    {
        // 1.5 GB を落とすことがある。既定の 100 秒では足りない。
        Timeout = TimeSpan.FromHours(2),
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (this.ownsHttpClient)
        {
            this.http.Dispose();
        }
    }
}
