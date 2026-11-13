using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NutLens.Capture.Audio;
using NutLens.Capture.Graphics;
using NutLens.Capture.Imaging;
using NutLens.Core.Capture;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;
using NutLens.Core.Sampling;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.CaptureProbe;

/// <summary>
/// 段階 1 の動作確認ツール。
///
/// 画面を実際にキャプチャして frames/ に書き出し、結果を標準出力に並べる。
/// GUI を手で操作しなくても「撮れているか・重複が落ちているか・間引きが効いているか」を確かめられる。
///
/// 使い方:
///   dotnet run --project tools/NutLens.CaptureProbe -- [秒数] [間隔秒] [最大枚数] [--animate]
///
///   --animate を付けると、1 秒ごとに中身が変わる窓を自分で出して、それを撮る。
///   静止した画面では重複除去が効いて 1 枚しか残らず、間引きの確認ができないため。
/// </summary>
internal static class Program
{
    /// <summary>重複とみなす距離。確認のために外から変えられるようにしている。</summary>
    private static int Threshold { get; set; } = 5;

    /// <summary>確認のために一時データを残すか。</summary>
    private static bool KeepTemporaryData { get; set; }

    [STAThread]
    private static int Main(string[] args)
    {
        bool animate = Array.IndexOf(args, "--animate") >= 0;
        KeepTemporaryData = Array.IndexOf(args, "--keep") >= 0;
        string[] numeric = Array.FindAll(
            args,
            static a => !a.StartsWith("--", StringComparison.Ordinal)
                && double.TryParse(a, CultureInfo.InvariantCulture, out _));

        double duration = ParseDouble(numeric, 0, 8.0);
        double interval = ParseDouble(numeric, 1, 1.0);
        int maxFrames = (int)ParseDouble(numeric, 2, 5);
        Threshold = (int)ParseDouble(numeric, 3, 5);

        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // 音を出さずにループバックを録る（暴走していないかの確認）。
        int idleIndex = Array.IndexOf(args, "--audio-idle");
        if (idleIndex >= 0)
        {
            return AudioSelfTest.RunIdleAsync(ParseDouble(numeric, 0, 3.0)).GetAwaiter().GetResult();
        }

        // 録音の経路を自分で確かめる（スピーカーから音が出る）。
        int selfTestIndex = Array.IndexOf(args, "--audio-selftest");
        if (selfTestIndex >= 0 && selfTestIndex + 1 < args.Length)
        {
            return AudioSelfTest.RunAsync(args[selfTestIndex + 1]).GetAwaiter().GetResult();
        }

        // WAV を 1 つ渡して文字起こしを試す（撮影を伴わない）。
        int transcribeIndex = Array.IndexOf(args, "--transcribe");
        if (transcribeIndex >= 0 && transcribeIndex + 1 < args.Length)
        {
            int modelIndex = Array.IndexOf(args, "--model");
            string? model = modelIndex >= 0 && modelIndex + 1 < args.Length ? args[modelIndex + 1] : null;
            return TranscribeProbe.RunAsync(args[transcribeIndex + 1], model).GetAwaiter().GetResult();
        }

        // 保存済みのフレームに OCR をかける（撮影を伴わない）。
        int ocrIndex = Array.IndexOf(args, "--ocr");
        if (ocrIndex >= 0 && ocrIndex + 1 < args.Length)
        {
            return OcrProbe.RunAsync(args[ocrIndex + 1]).GetAwaiter().GetResult();
        }

        // ホットキーを登録できるかを確かめる（ウィンドウは出ない）。
        int hotkeyIndex = Array.IndexOf(args, "--hotkey-selftest");
        if (hotkeyIndex >= 0)
        {
            return HotkeySelfTest.Run(
                hotkeyIndex + 1 < args.Length && !args[hotkeyIndex + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[hotkeyIndex + 1]
                    : null);
        }

        // 資格情報マネージャーの往復を確かめる（本物の鍵には触らない）。
        if (Array.IndexOf(args, "--secret-selftest") >= 0)
        {
            return SecretStoreSelfTest.Run();
        }

        // 保存済みのフレームで Web Detection を試す（撮影を伴わないが、**課金される**）。
        int webIndex = Array.IndexOf(args, "--web-detect");
        if (webIndex >= 0 && webIndex + 1 < args.Length)
        {
            return WebDetectionProbe
                .RunAsync(args[webIndex + 1], (int)ParseDouble(numeric, 0, 3))
                .GetAwaiter()
                .GetResult();
        }

        // この PC で何が使えるかを並べる（撮影を伴わない）。
        if (Array.IndexOf(args, "--env") >= 0)
        {
            return EnvironmentProbe.Run();
        }

        // 格子の細かさを変えたときの分離具合を測る。
        int gridIndex = Array.IndexOf(args, "--grid");
        if (gridIndex >= 0 && gridIndex + 1 < args.Length)
        {
            return GridProbe.Run(args[gridIndex + 1]);
        }

        // 縮小で dHash がどれだけ動くかを測る。
        int scaleIndex = Array.IndexOf(args, "--scale-sensitivity");
        if (scaleIndex >= 0 && scaleIndex + 1 < args.Length)
        {
            return ScaleProbe.Run(args[scaleIndex + 1]);
        }

        // 保存済みフレームの dHash と距離を測る（ADR-0004 の宿題）。
        int hashIndex = Array.IndexOf(args, "--hashes");
        if (hashIndex >= 0 && hashIndex + 1 < args.Length)
        {
            PixelRect? crop = numeric.Length >= 4
                ? new PixelRect(
                    (int)ParseDouble(numeric, 0, 0),
                    (int)ParseDouble(numeric, 1, 0),
                    (int)ParseDouble(numeric, 2, 0),
                    (int)ParseDouble(numeric, 3, 0))
                : null;

            return HashProbe.Run(args[hashIndex + 1], crop);
        }

        // 段階 2 の通し確認：既にある frames/ を使って claude -p まで走らせる。
        int analyzeIndex = Array.IndexOf(args, "--analyze");
        if (analyzeIndex >= 0 && analyzeIndex + 1 < args.Length)
        {
            int maxTurns = (int)ParseDouble(numeric, 0, 8);
            bool alsoResume = Array.IndexOf(args, "--resume") >= 0;
            return AnalyzeProbe.RunAsync(args[analyzeIndex + 1], maxTurns, alsoResume)
                .GetAwaiter()
                .GetResult();
        }

        Console.WriteLine($"WGC 利用可否: {WgcFrameSourceFactory.IsSupported}");
        if (!WgcFrameSourceFactory.IsSupported)
        {
            Console.Error.WriteLine("この環境では Windows.Graphics.Capture が使えません。");
            return 2;
        }

        return animate
            ? RunAnimated(duration, interval, maxFrames)
            : RunMonitor(duration, interval, maxFrames);
    }

    /// <summary>モニタ全体を撮る。ウィンドウ列挙・モニタ列挙の結果も併せて出す。</summary>
    private static int RunMonitor(double duration, double interval, int maxFrames)
    {
        MonitorCaptureTargetProvider monitors = new();
        IReadOnlyList<CaptureTarget> monitorTargets = monitors.GetTargets();
        Console.WriteLine($"モニタ: {monitorTargets.Count} 台");
        foreach (CaptureTarget monitor in monitorTargets)
        {
            Console.WriteLine($"  - {monitor.Title}");
        }

        WindowCaptureTargetProvider windows = new();
        IReadOnlyList<CaptureTarget> windowTargets = windows.GetTargets();
        Console.WriteLine($"ウィンドウ: {windowTargets.Count} 個（先頭 5 件）");
        for (int i = 0; i < Math.Min(5, windowTargets.Count); i++)
        {
            Console.WriteLine($"  - {windowTargets[i].Title}");
        }

        if (monitorTargets.Count == 0)
        {
            Console.Error.WriteLine("モニタが見つかりませんでした。");
            return 3;
        }

        return CaptureAsync(monitorTargets[0], duration, interval, maxFrames)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>動く窓を自分で出して、それを撮る。重複除去と間引きの実挙動を確かめるため。</summary>
    private static int RunAnimated(double duration, double interval, int maxFrames)
    {
        AnimatedWindow window = new();
        window.Show();

        nint hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        CaptureTarget target = new(CaptureTargetKind.Window, hwnd, window.Title);

        int exitCode = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                exitCode = await CaptureAsync(target, duration, interval, maxFrames).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                exitCode = 1;
            }
            finally
            {
                window.Dispatcher.InvokeShutdown();
            }
        });

        Dispatcher.Run();
        return exitCode;
    }

    private static async Task<int> CaptureAsync(
        CaptureTarget target,
        double duration,
        double interval,
        int maxFrames)
    {
        // 作業フォルダは本番と分ける。確認で利用者のデータを触らないようにするため。
        string root = Path.Combine(Path.GetTempPath(), "NutLensProbe");
        AppDataPaths paths = new(root);
        FileSystemSessionWorkspace workspace = new(paths);

        CaptureSettings settings = new CaptureSettings
        {
            IntervalSeconds = interval,
            DurationSeconds = duration,
            MaxFrames = maxFrames,
            LongEdgePixels = 1280,
            DuplicateThreshold = Threshold,
        }.Normalize();

        CaptureSessionService service = new(
            new WgcFrameSourceFactory(new InMemorySettingsStore()),
            new WicFrameEncoder(),
            new EvenTimeFrameThinner(),
            workspace,
            new DHashDuplicateFrameFilterFactory(),
            new LoopbackAudioRecorderFactory(),
            TimeProvider.System);

        Console.WriteLine();
        Console.WriteLine($"対象: {target.Kind} / {target.Title}");
        Console.WriteLine(
            $"撮影開始: 間隔 {settings.IntervalSeconds} 秒 / 時間 {settings.DurationSeconds} 秒 / "
            + $"上限 {settings.MaxFrames} 枚 / 重複しきい値 {settings.DuplicateThreshold}");

        Progress<CaptureProgress> progress = new(static p =>
            Console.WriteLine(
                $"  経過 {p.Elapsed.TotalSeconds,5:0.0} 秒 / 受信 {p.ReceivedFrames,4} 枚 / 採用 {p.AcceptedFrames,3} 枚"));

        CaptureResult result = await service
            .RunAsync(new CaptureRequest(target, settings), progress, CancellationToken.None)
            .ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"受信: {result.ReceivedFrames} 枚");
        Console.WriteLine($"保存: {result.FrameFiles.Count} 枚");
        Console.WriteLine($"経過: {result.Duration.TotalSeconds:0.0} 秒");
        Console.WriteLine($"保存先: {result.FramesDirectory}");
        Console.WriteLine();

        List<PerceptualHash> hashes = [];
        foreach (string file in result.FrameFiles)
        {
            FileInfo info = new(file);
            CapturedFrame decoded = ImageFiles.Read(file);
            PerceptualHash hash = DHash.Compute(decoded);

            int nearest = -1;
            foreach (PerceptualHash previous in hashes)
            {
                int distance = hash.DistanceTo(previous);
                if (nearest < 0 || distance < nearest)
                {
                    nearest = distance;
                }
            }

            hashes.Add(hash);
            Console.WriteLine(
                $"  {info.Name,-28} {info.Length / 1024,5} KB  {decoded.Width} x {decoded.Height}"
                + $"  前フレーム群との最小距離={(nearest < 0 ? "-" : nearest.ToString(CultureInfo.InvariantCulture))}");
        }

        if (result.FrameFiles.Count == 0)
        {
            Console.Error.WriteLine("フレームが 1 枚も取れませんでした。");
            return 4;
        }

        Console.WriteLine();
        if (KeepTemporaryData)
        {
            Console.WriteLine("--keep が指定されたので一時データを残します。");
            return 0;
        }

        Console.WriteLine("一時データを削除します（要件 5.5）。");
        workspace.DeleteAll();
        Console.WriteLine($"削除後の work\\ の中身: {Directory.GetDirectories(paths.WorkRoot).Length} 件");

        return 0;
    }

    /// <summary>保存した JPEG を BGRA に戻す。dHash を後から確かめるために使う。</summary>
    private static CapturedFrame ReadFrame(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapFrame decoded = BitmapFrame.Create(
            stream,
            BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad);

        FormatConvertedBitmap converted = new(decoded, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        return new CapturedFrame(pixels, converted.PixelWidth, converted.PixelHeight, stride, TimeSpan.Zero);
    }

    private static double ParseDouble(string[] args, int index, double fallback) =>
        index < args.Length && double.TryParse(args[index], CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;
}

/// <summary>確認ツール用。settings.json を触らずに既定値だけ返す。</summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    private NutLensSettings current = new();

    public NutLensSettings Load() => this.current;

    public void Save(NutLensSettings settings) => this.current = settings;
}
