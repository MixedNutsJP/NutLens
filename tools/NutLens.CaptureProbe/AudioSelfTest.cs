using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NutLens.Capture.Audio;
using NutLens.Core.Media;
using NutLens.Core.Sessions;

namespace NutLens.CaptureProbe;

/// <summary>
/// 録音の経路を自分で確かめる。
///
/// スピーカーへ短い音声を流しながら WASAPI ループバックで録り、
/// 録れた WAV をそのまま文字起こしにかける。
/// <b>動画の再生は要らないが、スピーカーから音が出る。</b>
///
/// 確かめるのは ADR-0016（録音）と ADR-0017（文字起こし）の繋ぎ目。
/// </summary>
internal static class AudioSelfTest
{
    /// <summary>
    /// 音を再生せずに、指定の秒数だけループバックを録る。
    ///
    /// <b>スピーカーから音は出ない。</b>
    /// 「無音でも書き出しが暴走しないか」を確かめるためのもの。
    /// 実際、<c>BufferedWaveProvider.ReadFully</c> が既定 true だったときは、
    /// 無音でも 1 秒あたり数百 MB の WAV が生成された。
    /// </summary>
    public static async Task<int> RunIdleAsync(double seconds)
    {
        string recorded = Path.Combine(
            Path.GetTempPath(),
            $"nutlens-idle-{Guid.NewGuid():N}.wav");

        Console.WriteLine($"保存先 : {recorded}");
        Console.WriteLine($"時間   : {seconds:0.0} 秒（音は再生しません）");
        Console.WriteLine();

        LoopbackAudioRecorder recorder = new();
        if (!recorder.TryStart(recorded))
        {
            Console.Error.WriteLine($"録音を開始できませんでした: {recorder.FailureReason}");
            await recorder.DisposeAsync().ConfigureAwait(false);
            return 6;
        }

        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);

        long bytes = await recorder.StopAsync().ConfigureAwait(false);
        string? failure = recorder.FailureReason;
        await recorder.DisposeAsync().ConfigureAwait(false);

        // 16 kHz・モノラル・16 bit なら 1 秒あたり 32 000 バイト。
        long expected = (long)(seconds * LoopbackAudioRecorder.SampleRate
            * LoopbackAudioRecorder.Channels
            * (LoopbackAudioRecorder.BitsPerSample / 8));

        Console.WriteLine($"書けた : {bytes / 1024} KB");
        Console.WriteLine($"目安   : {expected / 1024} KB（{seconds:0.0} 秒ぶん）");

        if (failure is not null)
        {
            Console.Error.WriteLine($"打ち切り: {failure}");
        }

        bool sane = bytes <= expected * 2;
        Console.WriteLine(sane
            ? "判定   : 妥当（暴走していない）"
            : "判定   : **異常**。時間に対して書けた量が多すぎる");

        if (bytes > 0 && File.Exists(recorded))
        {
            using WaveFileReader reader = new(recorded);
            Console.WriteLine(
                $"形式   : {reader.WaveFormat.SampleRate} Hz / {reader.WaveFormat.Channels} ch / "
                + $"{reader.WaveFormat.BitsPerSample} bit（{reader.TotalTime.TotalSeconds:0.0} 秒）");
        }

        TryDelete(recorded);
        return sane ? 0 : 9;
    }

    public static async Task<int> RunAsync(string sourceWavPath)
    {
        if (!File.Exists(sourceWavPath))
        {
            Console.Error.WriteLine($"再生する WAV が見つかりません: {sourceWavPath}");
            return 5;
        }

        string recorded = Path.Combine(
            Path.GetTempPath(),
            $"nutlens-loopback-{Guid.NewGuid():N}.wav");

        Console.WriteLine($"再生する音声 : {sourceWavPath}");
        Console.WriteLine($"録音の保存先 : {recorded}");
        Console.WriteLine();
        Console.WriteLine("スピーカーから音が出ます。");
        Console.WriteLine();

        LoopbackAudioRecorder recorder = new();
        if (!recorder.TryStart(recorded))
        {
            Console.Error.WriteLine($"録音を開始できませんでした: {recorder.FailureReason}");
            await recorder.DisposeAsync().ConfigureAwait(false);
            return 6;
        }

        Console.WriteLine("録音を開始しました。再生します…");

        Stopwatch stopwatch = Stopwatch.StartNew();
        Play(sourceWavPath);

        // 末尾が切れないよう、再生の後に少し待ってから止める。
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        long bytes = await recorder.StopAsync().ConfigureAwait(false);
        await recorder.DisposeAsync().ConfigureAwait(false);
        stopwatch.Stop();

        Console.WriteLine($"録音しました : {bytes / 1024} KB / {stopwatch.Elapsed.TotalSeconds:0.0} 秒");

        if (bytes == 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("何も録れませんでした。既定の再生デバイスが音を出していない可能性があります。");
            return 7;
        }

        // 16 kHz・モノラル・16 bit で書けているかを確かめる（ADR-0016 決定 1）。
        using (WaveFileReader reader = new(recorded))
        {
            Console.WriteLine(
                $"形式         : {reader.WaveFormat.SampleRate} Hz / "
                + $"{reader.WaveFormat.Channels} ch / {reader.WaveFormat.BitsPerSample} bit "
                + $"（{reader.TotalTime.TotalSeconds:0.0} 秒）");
        }

        Console.WriteLine();
        Console.WriteLine("--- 録れた音声を文字起こしします ---");

        AppDataPaths paths = new();
        WhisperTranscriber transcriber = new(paths);

        TranscriptionResult result = await transcriber
            .TranscribeAsync(recorded, transcriber.DefaultModelPath, progress: null, CancellationToken.None)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"文字起こしに失敗: {result.SkipReason}");
            return 8;
        }

        Console.WriteLine(new MediaTextFormatter().FormatTranscript(result, "ggml-base.bin"));

        TryDelete(recorded);
        return 0;
    }

    private static void Play(string path)
    {
        using WaveFileReader reader = new(path);
        using WaveOut output = new();

        output.Init(reader);
        output.Play();

        while (output.PlaybackState == PlaybackState.Playing)
        {
            Thread.Sleep(100);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
