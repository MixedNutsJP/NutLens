using System;
using System.Collections.Generic;
using System.IO;
using NutLens.Capture.Audio;
using NutLens.Capture.Graphics;
using NutLens.Capture.Security;
using NutLens.Capture.Text;
using NutLens.Core.Media;
using NutLens.Core.Security;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using NutLens.Core.WebDetection;

namespace NutLens.CaptureProbe;

/// <summary>
/// この PC で何が使えるかを並べる。
/// <b>撮影を伴わないので、利用者に動画を再生してもらわなくても実行できる。</b>
/// </summary>
internal static class EnvironmentProbe
{
    public static int Run()
    {
        Console.WriteLine("=== 画面キャプチャ（段階 1）===");
        Console.WriteLine($"Windows.Graphics.Capture : {(WgcFrameSourceFactory.IsSupported ? "使える" : "使えない")}");
        Console.WriteLine($"モニタ                   : {new MonitorCaptureTargetProvider().GetTargets().Count} 台");
        Console.WriteLine($"撮影できるウィンドウ     : {new WindowCaptureTargetProvider().GetTargets().Count} 個");

        Console.WriteLine();
        Console.WriteLine("=== OCR（段階 3 / ADR-0018）===");
        try
        {
            IReadOnlyList<string> tags = WindowsOcrEngine.AvailableLanguageTags();
            Console.WriteLine($"利用できる認識言語       : {tags.Count} 件");
            foreach (string tag in tags)
            {
                Console.WriteLine($"  - {tag}");
            }

            WindowsOcrEngine engine = new();
            Console.WriteLine(engine.IsAvailable(out string? reason)
                ? "OCR                      : 使える"
                : $"OCR                      : 使えない（{reason}）");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"OCR                      : 確認に失敗（{exception.GetType().Name}: {exception.Message}）");
        }

        Console.WriteLine();
        Console.WriteLine("=== 文字起こし（段階 3 / ADR-0017）===");
        AppDataPaths paths = new();
        WhisperTranscriber transcriber = new(paths);
        string model = transcriber.DefaultModelPath;
        Console.WriteLine($"既定のモデルの場所       : {model}");
        Console.WriteLine(File.Exists(model)
            ? $"モデル                   : あり（{new FileInfo(model).Length / 1024 / 1024} MB）"
            : "モデル                   : **ありません**（段階 5 の初回セットアップで導入。D-04）");

        Console.WriteLine();
        Console.WriteLine("=== 録音（段階 3 / ADR-0016）===");
        LoopbackAudioRecorder recorder = new();
        string probePath = Path.Combine(Path.GetTempPath(), $"nutlens-audio-probe-{Guid.NewGuid():N}.wav");
        bool started = recorder.TryStart(probePath);
        Console.WriteLine(started
            ? "WASAPI ループバック      : 開始できる"
            : $"WASAPI ループバック      : 開始できない（{recorder.FailureReason}）");

        if (started)
        {
            long bytes = recorder.StopAsync().GetAwaiter().GetResult();
            Console.WriteLine($"ごく短時間の録音         : {bytes} バイト（無音なら 0 のことがある）");
        }

        recorder.DisposeAsync().AsTask().GetAwaiter().GetResult();
        TryDelete(probePath);

        Console.WriteLine();
        Console.WriteLine("=== Web Detection（段階 3' / ADR-0021）===");
        PrintWebDetection(paths);

        Console.WriteLine();
        Console.WriteLine("=== 設定 ===");
        Console.WriteLine($"設定ファイル             : {paths.SettingsFile}");
        Console.WriteLine($"既定の重複しきい値       : {new CaptureSettings().DuplicateThreshold}（{NutLens.Core.Hashing.DHash.BitCount} bit 中）");

        return 0;
    }

    /// <summary>
    /// 送信の 3 条件がどう揃っているかを並べる（ADR-0021 決定 1）。
    /// <b>「有効にしたのに動かない」を原因不明にしない。</b>
    /// </summary>
    private static void PrintWebDetection(AppDataPaths paths)
    {
        WebDetectionSettings settings;
        try
        {
            settings = new JsonSettingsStore(paths).Load().WebDetection;
        }
        catch (NutLens.Core.Diagnostics.SettingsCorruptedException)
        {
            settings = new WebDetectionSettings();
        }

        bool hasKey;
        try
        {
            hasKey = !string.IsNullOrWhiteSpace(
                new CredentialManagerSecretStore().TryRead(SecretNames.GoogleCloudVisionApiKey));
        }
        catch (InvalidOperationException)
        {
            hasKey = false;
        }

        Console.WriteLine($"設定で有効               : {(settings.Enabled ? "はい" : "いいえ（既定）")}");
        Console.WriteLine(
            $"同意の版                 : {settings.ConsentVersion}"
            + $"（今の版 {WebDetectionSettings.CurrentConsentVersion}）");
        Console.WriteLine($"API キー                 : {(hasKey ? "あり" : "なし")}");
        Console.WriteLine($"送る枚数                 : {settings.MaxImages}");
        Console.WriteLine(WebDetectionGate.CanSend(settings, hasKey)
            ? "判定                     : **送られる**"
            : $"判定                     : 送られない（{WebDetectionGate.DescribeSkip(settings, hasKey)}）");
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
