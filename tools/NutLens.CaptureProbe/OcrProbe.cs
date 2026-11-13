using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Capture.Text;
using NutLens.Core.Analysis;
using NutLens.Core.Media;

namespace NutLens.CaptureProbe;

/// <summary>
/// 保存済みのフレームに OCR をかけて結果を出す。
///
/// <b>撮影を伴わない。</b>既にあるフレームで、
/// 「JPEG 品質 85 でテロップが読めるか」（ADR-0008 / ADR-0018 の宿題）を測れる。
/// </summary>
internal static class OcrProbe
{
    public static async Task<int> RunAsync(string framesDirectory)
    {
        if (!Directory.Exists(framesDirectory))
        {
            Console.Error.WriteLine($"フォルダが見つかりません: {framesDirectory}");
            return 5;
        }

        string[] files = Directory.GetFiles(framesDirectory, "*.jpg");
        Array.Sort(files, StringComparer.Ordinal);

        if (files.Length == 0)
        {
            Console.Error.WriteLine("JPEG がありません。");
            return 5;
        }

        WindowsOcrEngine engine = new();
        if (!engine.IsAvailable(out string? reason))
        {
            Console.Error.WriteLine($"OCR が使えません: {reason}");
            return 6;
        }

        Console.WriteLine($"利用できる認識言語: {string.Join(", ", WindowsOcrEngine.AvailableLanguageTags())}");
        Console.WriteLine($"フレーム: {files.Length} 枚");
        Console.WriteLine();

        List<OcrTarget> targets = new(files.Length);
        foreach (string file in files)
        {
            targets.Add(new OcrTarget(file, FileSystemFrameLister.ParseElapsed(Path.GetFileName(file))));
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        OcrResult result = await engine
            .RecognizeAsync(targets, preferredLanguage: null, CancellationToken.None)
            .ConfigureAwait(false);
        stopwatch.Stop();

        Console.WriteLine($"言語          : {result.Language}");
        Console.WriteLine($"検出したフレーム: {result.FramesWithText} / {result.FrameCount} 枚");
        Console.WriteLine($"重複除去後の行 : {result.Lines.Count} 行");
        Console.WriteLine($"所要          : {stopwatch.Elapsed.TotalSeconds:0.0} 秒（1 枚あたり {stopwatch.Elapsed.TotalMilliseconds / files.Length:0} ms）");
        Console.WriteLine();

        Console.WriteLine(new MediaTextFormatter().FormatOcr(result));
        return 0;
    }
}
