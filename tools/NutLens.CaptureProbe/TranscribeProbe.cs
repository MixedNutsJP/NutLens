using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Capture.Audio;
using NutLens.Core.Media;
using NutLens.Core.Sessions;

namespace NutLens.CaptureProbe;

/// <summary>
/// WAV を 1 つ渡して文字起こしを試す。
///
/// <b>撮影を伴わない。</b>「Whisper.net が .NET 10 で動くか」（ADR-0017 の宿題）を、
/// 手元の WAV だけで確かめられる。
/// </summary>
internal static class TranscribeProbe
{
    public static async Task<int> RunAsync(string wavPath, string? modelPath)
    {
        if (!File.Exists(wavPath))
        {
            Console.Error.WriteLine($"WAV が見つかりません: {wavPath}");
            return 5;
        }

        AppDataPaths paths = new();
        WhisperTranscriber transcriber = new(paths);
        string model = string.IsNullOrWhiteSpace(modelPath) ? transcriber.DefaultModelPath : modelPath;

        Console.WriteLine($"WAV    : {wavPath}（{new FileInfo(wavPath).Length / 1024} KB）");
        Console.WriteLine($"モデル : {model}");
        Console.WriteLine(File.Exists(model) ? "モデル : あり" : "モデル : **ありません**");
        Console.WriteLine();

        Stopwatch stopwatch = Stopwatch.StartNew();
        TranscriptionResult result = await transcriber
            .TranscribeAsync(
                wavPath,
                model,
                new Progress<double>(value => Console.Write($"\r  進捗 {value * 100,5:0}%")),
                CancellationToken.None)
            .ConfigureAwait(false);
        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"成否   : {(result.Succeeded ? "成功" : "失敗")}");
        Console.WriteLine($"所要   : {stopwatch.Elapsed.TotalSeconds:0.0} 秒");

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"理由   : {result.SkipReason}");
            return 7;
        }

        Console.WriteLine($"言語   : {result.Language}");
        Console.WriteLine($"区間   : {result.Segments.Count} 件");
        Console.WriteLine();
        Console.WriteLine(new MediaTextFormatter().FormatTranscript(result, Path.GetFileName(model)));

        return 0;
    }
}
