using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Analysis;
using NutLens.Core.Analysis;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.CaptureProbe;

/// <summary>
/// 段階 2 の通し確認。
///
/// 既にある frames/ を使って「analyzer の配置 → プロンプト生成 → claude -p の起動 →
/// 結果 JSON のパース」までを実際に通す。
/// <b>利用者の Claude の利用枠を使う。</b>枚数とターン数を絞って呼ぶこと。
/// </summary>
internal static class AnalyzeProbe
{
    public static async Task<int> RunAsync(string sessionDirectory, int maxTurns, bool alsoResume = false)
    {
        if (!Directory.Exists(sessionDirectory))
        {
            Console.Error.WriteLine($"セッションフォルダが見つかりません: {sessionDirectory}");
            return 5;
        }

        string? workRoot = Path.GetDirectoryName(sessionDirectory);
        string? appRoot = workRoot is null ? null : Path.GetDirectoryName(workRoot);
        string folderName = Path.GetFileName(sessionDirectory);

        if (workRoot is null || appRoot is null || !SessionId.TryParse(folderName, out SessionId sessionId))
        {
            Console.Error.WriteLine($"セッションフォルダの名前が UUID ではありません: {folderName}");
            return 5;
        }

        SessionLayout layout = new(workRoot, sessionId);
        AppDataPaths paths = new(appRoot);

        Console.WriteLine($"セッション       : {sessionId}");
        Console.WriteLine($"作業ディレクトリ : {layout.Root}");

        // claude を探す（D-05）。
        ClaudeCliLocator locator = new();
        ClaudeExecutable executable;
        try
        {
            executable = locator.Locate(null);
        }
        catch (Core.Diagnostics.ClaudeNotFoundException exception)
        {
            Console.Error.WriteLine(exception.Message);
            foreach (string path in exception.SearchedPaths)
            {
                Console.Error.WriteLine($"  探した場所: {path}");
            }

            return 6;
        }

        Console.WriteLine($"claude           : {executable.Path}");
        Console.WriteLine($"版               : {executable.Version}");
        Console.WriteLine($"cmd 経由         : {executable.RequiresCmdShell}");
        Console.WriteLine();

        AnalysisService service = new(
            new AnalyzerTemplateDeployer(paths, AppContext.BaseDirectory),
            new ClaudeCliAnalysisEngine(locator),
            new AnalysisResultParser(),
            new FileSystemFrameLister(),
            TimeProvider.System);

        UserNotes notes = new(
            SourceUrl: "https://example.com/probe",
            FreeText: "これは NutLens の動作確認です。実際の映画の映像ではありません。");

        PromptPreparation preparation = service.PreparePrompt(layout, notes);

        Console.WriteLine($"フレーム         : {preparation.FrameCount} 枚");
        Console.WriteLine($"プロンプト長     : {preparation.Prompt.Length} 文字");
        Console.WriteLine($"スキーマ         : {preparation.Analyzer.SchemaRelativePath}");
        Console.WriteLine();
        Console.WriteLine("--- 配置された analyzer ---");
        foreach (string path in Directory.GetFiles(layout.Root, "*", SearchOption.AllDirectories))
        {
            Console.WriteLine("  " + path.Replace(layout.Root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal));
        }

        Console.WriteLine();
        Console.WriteLine($"--- claude -p を起動します（最大 {maxTurns} ターン） ---");

        AnalysisOutcome outcome = await service.AnalyzeAsync(
            new AnalysisRequest(
                layout,
                preparation.Analyzer,
                preparation.Prompt,
                new ClaudeSettings { MaxTurns = maxTurns },
                Resume: false),
            new Progress<AnalysisProgress>(p => Console.WriteLine($"  {p.Message}")),
            CancellationToken.None);

        Console.WriteLine();
        Console.WriteLine($"成否             : {(outcome.Succeeded ? "成功" : "失敗")}");
        Console.WriteLine($"ターン数         : {outcome.RunInfo.Turns}");
        Console.WriteLine($"所要             : {outcome.RunInfo.Duration.TotalSeconds:0.0} 秒");
        Console.WriteLine($"費用             : {(outcome.RunInfo.CostUsd is decimal c ? $"${c:0.0000}" : "不明")}");
        Console.WriteLine($"停止理由         : {outcome.RunInfo.StopReason ?? "(なし)"}");
        Console.WriteLine($"拒否されたツール : {outcome.RunInfo.PermissionDenialCount} 件");
        Console.WriteLine($"セッション照合   : {(outcome.RunInfo.SessionId == sessionId ? "一致" : $"不一致 → {outcome.RunInfo.SessionId}")}");

        if (!outcome.Succeeded)
        {
            Console.Error.WriteLine($"エラー: {outcome.ErrorMessage}");
            Console.Error.WriteLine("--- 生の出力（先頭 800 文字）---");
            Console.Error.WriteLine(outcome.RawOutput.Length > 800
                ? outcome.RawOutput[..800]
                : outcome.RawOutput);
            return 7;
        }

        AnalysisResult result = outcome.Result!;
        Console.WriteLine();
        Console.WriteLine($"候補             : {result.Candidates.Count} 件");
        foreach (MovieCandidate candidate in result.Candidates)
        {
            Console.WriteLine($"  - {candidate.DisplayTitle} ({candidate.Type}, {candidate.Year?.ToString() ?? "年不明"}) 確信度 {candidate.Confidence:0.00}");
            foreach (string evidence in candidate.Evidence)
            {
                Console.WriteLine($"      根拠: {evidence}");
            }
        }

        PrintList("観察された特徴", result.ObservedFeatures);
        PrintList("確認できなかった点", result.UnverifiedPoints);
        PrintList("使われた検索語", result.SearchQueriesUsed);

        Console.WriteLine();
        Console.WriteLine("--- Markdown の先頭 ---");
        string markdown = new ResultMarkdownWriter().Write(outcome, notes.SourceUrl, DateTimeOffset.Now);
        Console.WriteLine(markdown.Length > 600 ? markdown[..600] : markdown);

        Console.WriteLine();
        string? transcript = new ClaudeTranscriptLocator().TryFind(sessionId);
        Console.WriteLine(transcript is null
            ? "会話履歴         : 見つかりませんでした"
            : $"会話履歴         : {transcript}（{new FileInfo(transcript).Length / 1024} KB）");

        Console.WriteLine($"claude.log       : {(File.Exists(layout.ClaudeLogFile) ? $"{new FileInfo(layout.ClaudeLogFile).Length / 1024} KB" : "ありません")}");
        Console.WriteLine($"prompt.md        : {(File.Exists(layout.PromptFile) ? "あり" : "ありません")}");

        if (!alsoResume)
        {
            return 0;
        }

        // F-27：追加情報を付けて --resume で続きを聞く。
        Console.WriteLine();
        Console.WriteLine("--- 再調査（--resume）を試します ---");

        const string Additional = "追加情報：この画面は NutLens の動作確認用の窓です。"
            + "1 枚目の数字と 2 枚目の数字を、observed_features に「前回と同じ数字を覚えているか」の確認として入れてください。";

        AnalysisOutcome resumed = await service.AnalyzeAsync(
            new AnalysisRequest(
                layout,
                preparation.Analyzer,
                ResumePromptForProbe(Additional),
                new ClaudeSettings { MaxTurns = maxTurns },
                Resume: true),
            new Progress<AnalysisProgress>(p => Console.WriteLine($"  {p.Message}")),
            CancellationToken.None);

        Console.WriteLine($"成否             : {(resumed.Succeeded ? "成功" : "失敗")}");
        Console.WriteLine($"ターン数         : {resumed.RunInfo.Turns}");
        Console.WriteLine($"セッション照合   : {(resumed.RunInfo.SessionId == sessionId ? "一致（会話が継続している）" : $"不一致 → {resumed.RunInfo.SessionId}")}");

        if (!resumed.Succeeded)
        {
            Console.Error.WriteLine($"エラー: {resumed.ErrorMessage}");
            return 8;
        }

        PrintList("再調査後の観察", resumed.Result!.ObservedFeatures);
        return 0;
    }

    /// <summary>ResultViewModel と同じ形の再調査プロンプト。</summary>
    private static string ResumePromptForProbe(string additionalInformation) =>
        $"""
        先ほどの候補は外れていました。次の追加情報を踏まえて、もう一度特定してください。

        ## 追加情報

        {additionalInformation.Trim()}

        前回と同じスキーマに従った JSON だけを返してください。
        """;

    private static void PrintList(string heading, IReadOnlyList<string> items)
    {
        Console.WriteLine($"{heading}: {items.Count} 件");
        foreach (string item in items)
        {
            Console.WriteLine($"  - {item}");
        }
    }
}
