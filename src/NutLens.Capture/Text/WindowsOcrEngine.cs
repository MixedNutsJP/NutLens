using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Media;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using CoreOcrResult = NutLens.Core.Media.OcrResult;
using WinRtOcrLine = Windows.Media.Ocr.OcrLine;
using WinRtOcrResult = Windows.Media.Ocr.OcrResult;

namespace NutLens.Capture.Text;

/// <summary>
/// Windows.Media.Ocr でテロップ・字幕を読む（F-12 / ADR-0018）。
///
/// 対象は<b>間引いた後のフレーム</b>。回数が 10 分の 1 になり、
/// Claude が読む画像と OCR が読んだ画像が同じものになる（ADR-0018 決定 1）。
///
/// <b>言語パックが入っている言語しか読めない。</b>
/// 入っていなければ OCR を飛ばし、解析は続ける（同 決定 2）。
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    public const string JapaneseTag = "ja";
    public const string EnglishTag = "en";

    public bool IsAvailable(out string? reason)
    {
        try
        {
            return TrySelectLanguage(null, out _, out reason);
        }
        catch (Exception exception) when (exception is TypeLoadException
            or System.Runtime.InteropServices.COMException
            or NotSupportedException)
        {
            reason = $"OCR を初期化できませんでした: {exception.Message}";
            return false;
        }
    }

    public async Task<CoreOcrResult> RecognizeAsync(
        IReadOnlyList<OcrTarget> frames,
        string? preferredLanguage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frames);

        if (!TrySelectLanguage(preferredLanguage, out Language? language, out string? reason))
        {
            return CoreOcrResult.Skipped(reason ?? "OCR が利用できません。");
        }

        OcrEngine? engine = OcrEngine.TryCreateFromLanguage(language);
        if (engine is null)
        {
            return CoreOcrResult.Skipped($"OCR の認識器を作成できませんでした（言語: {language.LanguageTag}）。");
        }

        OcrTextCollector collector = new();
        int framesWithText = 0;

        foreach (OcrTarget frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(frame.Path))
            {
                continue;
            }

            IReadOnlyList<string> lines = await RecognizeFrameAsync(engine, frame.Path).ConfigureAwait(false);
            if (lines.Count == 0)
            {
                continue;
            }

            framesWithText++;
            foreach (string line in lines)
            {
                collector.TryAdd(frame.Elapsed, line);
            }
        }

        return new CoreOcrResult(
            true,
            language.LanguageTag,
            framesWithText,
            frames.Count,
            collector.ToLines(),
            null);
    }

    /// <summary>
    /// JPEG を復号して <see cref="SoftwareBitmap"/> にし、1 枚ぶん読む。
    /// 失敗しても例外を外へ出さない。1 枚読めないだけで全体を止めない。
    /// </summary>
    private static async Task<IReadOnlyList<string>> RecognizeFrameAsync(OcrEngine engine, string path)
    {
        try
        {
            byte[] bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);

            using InMemoryRandomAccessStream stream = new();
            using (DataWriter writer = new(stream))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync().AsTask().ConfigureAwait(false);
                await writer.FlushAsync().AsTask().ConfigureAwait(false);
                writer.DetachStream();
            }

            stream.Seek(0);

            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask().ConfigureAwait(false);
            using SoftwareBitmap bitmap = await decoder
                .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
                .AsTask()
                .ConfigureAwait(false);

            WinRtOcrResult recognized = await engine.RecognizeAsync(bitmap).AsTask().ConfigureAwait(false);

            List<string> lines = new(recognized.Lines.Count);
            foreach (WinRtOcrLine line in recognized.Lines)
            {
                if (!string.IsNullOrWhiteSpace(line.Text))
                {
                    lines.Add(line.Text);
                }
            }

            return lines;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or System.Runtime.InteropServices.COMException
            or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// 使う言語を選ぶ（ADR-0018 決定 2）。
    /// 指定 → ja → en → 利用できる最初のもの の順。
    /// </summary>
    private static bool TrySelectLanguage(string? preferred, out Language language, out string? reason)
    {
        IReadOnlyList<Language> available = OcrEngine.AvailableRecognizerLanguages;

        if (available.Count == 0)
        {
            language = null!;
            reason = "この環境には OCR の認識言語が 1 つも入っていません。"
                + "Windows の設定から言語機能（文字認識）を追加してください。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(preferred) && TryFind(available, preferred, out language))
        {
            reason = null;
            return true;
        }

        if (TryFind(available, JapaneseTag, out language) || TryFind(available, EnglishTag, out language))
        {
            reason = null;
            return true;
        }

        language = available[0];
        reason = null;
        return true;
    }

    private static bool TryFind(IReadOnlyList<Language> available, string tag, out Language found)
    {
        foreach (Language candidate in available)
        {
            if (candidate.LanguageTag.StartsWith(tag, StringComparison.OrdinalIgnoreCase))
            {
                found = candidate;
                return true;
            }
        }

        found = null!;
        return false;
    }

    /// <summary>利用できる認識言語の一覧。設定画面（段階 4）と確認ツールで使う。</summary>
    public static IReadOnlyList<string> AvailableLanguageTags()
    {
        List<string> tags = [];
        foreach (Language language in OcrEngine.AvailableRecognizerLanguages)
        {
            tags.Add($"{language.LanguageTag} ({language.DisplayName})");
        }

        return tags;
    }
}
