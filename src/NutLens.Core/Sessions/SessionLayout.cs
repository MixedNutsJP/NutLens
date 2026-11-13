using System;
using System.Globalization;
using System.IO;

namespace NutLens.Core.Sessions;

/// <summary>
/// 1 セッション分の一時作業フォルダの構造（F-07）。
///
/// この下にあるものはすべて一時データで、解析が終わったら完全に消す（5.5）。
/// ディスクに実際に書くのは「Claude Code に渡す必要があるもの」だけに絞る（ADR-0005）。
/// </summary>
public sealed class SessionLayout
{
    public const string FramesDirectoryName = "frames";
    public const string AudioFileName = "audio.wav";
    public const string TranscriptFileName = "transcript.txt";
    public const string OcrFileName = "ocr.txt";
    public const string WebDetectionFileName = "web_detection.json";
    public const string PromptFileName = "prompt.md";
    public const string ClaudeLogFileName = "claude.log";

    public SessionLayout(string workRoot, SessionId id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workRoot);

        this.Id = id;
        this.Root = Path.Combine(workRoot, id.ToString());
        this.FramesDirectory = Path.Combine(this.Root, FramesDirectoryName);
        this.AudioFile = Path.Combine(this.Root, AudioFileName);
        this.TranscriptFile = Path.Combine(this.Root, TranscriptFileName);
        this.OcrFile = Path.Combine(this.Root, OcrFileName);
        this.WebDetectionFile = Path.Combine(this.Root, WebDetectionFileName);
        this.PromptFile = Path.Combine(this.Root, PromptFileName);
        this.ClaudeLogFile = Path.Combine(this.Root, ClaudeLogFileName);
    }

    public SessionId Id { get; }

    /// <summary>セッションフォルダ。ここが claude -p の作業ディレクトリになる（ADR-0006 決定 6）。</summary>
    public string Root { get; }

    public string FramesDirectory { get; }

    /// <summary>段階 3 で使う。</summary>
    public string AudioFile { get; }

    /// <summary>段階 3 で使う。</summary>
    public string TranscriptFile { get; }

    /// <summary>段階 3 で使う。</summary>
    public string OcrFile { get; }

    /// <summary>段階 3 の後（Web Detection を利用者が有効にした場合）で使う。</summary>
    public string WebDetectionFile { get; }

    /// <summary>段階 2 で使う。</summary>
    public string PromptFile { get; }

    /// <summary>段階 2 で使う。claude の標準出力・標準エラーを書く。</summary>
    public string ClaudeLogFile { get; }

    /// <summary>
    /// フレームのファイル名。例: <c>frame_001_t0002.0s.jpg</c>
    ///
    /// 経過秒をファイル名に入れるのは、段階 2 のプロンプト生成（F-21「フレームの相対パス一覧と撮影時刻」）で
    /// パスだけを並べれば時刻も伝わるようにするため。
    /// </summary>
    public static string FrameFileName(int index, TimeSpan elapsed, string extension)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        ArgumentException.ThrowIfNullOrEmpty(extension);

        return string.Format(
            CultureInfo.InvariantCulture,
            "frame_{0:D3}_t{1:0000.0}s{2}",
            index,
            elapsed.TotalSeconds,
            extension);
    }
}
