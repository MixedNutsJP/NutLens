using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Analysis;
using NutLens.Capture.Security;
using NutLens.Core.Analysis;
using NutLens.Core.Sampling;
using NutLens.Core.Security;
using NutLens.Core.Sessions;
using NutLens.Core.WebDetection;

namespace NutLens.CaptureProbe;

/// <summary>
/// 保存済みのフレームで Web Detection を試す（撮影を伴わない）。
///
/// <b>実際に Google へ送信し、利用者の Google Cloud に課金される。</b>
/// 送る枚数は既定 3 枚（＝3 ユニット）。
///
/// 鍵は資格情報マネージャーから読む（ADR-0020）。
/// 引数で鍵を受け取らないのは、コマンド履歴に平文で残るため。
/// </summary>
internal static class WebDetectionProbe
{
    public static async Task<int> RunAsync(string framesDirectory, int maxImages)
    {
        if (!Directory.Exists(framesDirectory))
        {
            Console.Error.WriteLine($"フレームのフォルダが見つかりません: {framesDirectory}");
            return 5;
        }

        ISecretStore secrets = new CredentialManagerSecretStore();
        string? apiKey = secrets.TryRead(SecretNames.GoogleCloudVisionApiKey);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("API キーが資格情報マネージャーにありません。");
            Console.Error.WriteLine($"名前: {SecretNames.GoogleCloudVisionApiKey}");
            Console.Error.WriteLine("アプリの「高度な設定（上級者向け）」から設定してください。");
            return 6;
        }

        IReadOnlyList<WebDetectionImage> images = Select(framesDirectory, maxImages);
        if (images.Count == 0)
        {
            Console.Error.WriteLine("送れるフレームがありません。");
            return 7;
        }

        Console.WriteLine($"送る枚数 : {images.Count}");
        foreach (WebDetectionImage image in images)
        {
            Console.WriteLine(
                $"  {image.RelativePath}（{image.Elapsed.TotalSeconds:0.0} 秒 / {image.JpegBytes.Length / 1024} KB）");
        }

        Console.WriteLine();
        Console.WriteLine("Google Cloud Vision へ送ります（課金されます）…");

        using VisionWebDetectionClient client = new();
        WebDetectionOutcome outcome = await client
            .DetectAsync(images, apiKey, TimeSpan.FromSeconds(30), CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"結果 : {WebDetectionDocument.ToStatusText(outcome.Status)}");

        if (!outcome.Succeeded)
        {
            Console.Error.WriteLine($"理由 : {outcome.Message}");
            return 8;
        }

        Console.WriteLine();
        Console.WriteLine(WebDetectionDocument.Format(outcome));
        return 0;
    }

    /// <summary>
    /// アプリと同じ選び方（ADR-0022 決定 1）。
    /// 別の選び方を使うと、この確認が本番の挙動を確かめたことにならない。
    /// </summary>
    private static IReadOnlyList<WebDetectionImage> Select(string framesDirectory, int maxImages)
    {
        List<FrameReference> frames = [];
        foreach (string path in Directory.GetFiles(framesDirectory))
        {
            frames.Add(new FrameReference(
                Path.GetFileName(path),
                FileSystemFrameLister.ParseElapsed(Path.GetFileName(path))));
        }

        frames.Sort(static (left, right) => left.Elapsed.CompareTo(right.Elapsed));

        IReadOnlyList<FrameReference> selected = new EvenTimeFrameThinner().Thin(frames, maxImages);

        List<WebDetectionImage> images = new(selected.Count);
        foreach (FrameReference frame in selected)
        {
            images.Add(new WebDetectionImage(
                $"{SessionLayout.FramesDirectoryName}/{frame.RelativePath}",
                frame.Elapsed,
                File.ReadAllBytes(Path.Combine(framesDirectory, frame.RelativePath))));
        }

        return images;
    }
}
