using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NutLens.Core.Analysis;

/// <summary>
/// 解析結果を Markdown にする（F-26「Markdown でも保存する」）。
///
/// 画面に出すものと同じ内容を、画面の都合抜きで並べる。純ロジックなので Core に置く。
/// </summary>
public sealed class ResultMarkdownWriter
{
    public string Write(AnalysisOutcome outcome, string? sourceUrl, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        StringBuilder builder = new();
        builder.AppendLine("# NutLens 解析結果");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 日時: {timestamp:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- セッション ID: `{outcome.RunInfo.SessionId}`");

        if (!string.IsNullOrWhiteSpace(sourceUrl))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- 動画の URL: {sourceUrl}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture,
            $"- ターン数: {outcome.RunInfo.Turns} / 所要: {outcome.RunInfo.Duration.TotalSeconds:0} 秒");

        if (outcome.RunInfo.CostUsd is decimal cost)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- 費用: ${cost:0.0000}");
        }

        builder.AppendLine();

        if (!outcome.Succeeded || outcome.Result is null)
        {
            builder.AppendLine("## 解析に失敗しました");
            builder.AppendLine();
            builder.AppendLine(outcome.ErrorMessage ?? "（理由は記録されていません）");
            builder.AppendLine();
            builder.AppendLine("### Claude Code の生の出力");
            builder.AppendLine();
            builder.AppendLine("```");
            builder.AppendLine(outcome.RawOutput);
            builder.AppendLine("```");
            return builder.ToString();
        }

        AnalysisResult result = outcome.Result;

        builder.AppendLine("## 候補");
        builder.AppendLine();

        if (result.Candidates.Count == 0)
        {
            builder.AppendLine("候補は立ちませんでした。");
            builder.AppendLine();
        }
        else
        {
            for (int i = 0; i < result.Candidates.Count; i++)
            {
                AppendCandidate(builder, i + 1, result.Candidates[i]);
            }
        }

        AppendList(builder, "観察された特徴", result.ObservedFeatures);
        AppendList(builder, "確認できなかった点", result.UnverifiedPoints);
        AppendList(builder, "使われた検索語", result.SearchQueriesUsed);

        return builder.ToString();
    }

    private static void AppendCandidate(StringBuilder builder, int ordinal, MovieCandidate candidate)
    {
        builder.Append(CultureInfo.InvariantCulture, $"### {ordinal}. {candidate.DisplayTitle}");

        if (!string.IsNullOrWhiteSpace(candidate.TitleOriginal)
            && !string.Equals(candidate.TitleOriginal, candidate.DisplayTitle, StringComparison.Ordinal))
        {
            builder.Append(CultureInfo.InvariantCulture, $"（{candidate.TitleOriginal}）");
        }

        builder.AppendLine();
        builder.AppendLine();

        builder.AppendLine(CultureInfo.InvariantCulture,
            $"- 種別: {candidate.Type} / 公開年: {(candidate.Year is int year ? year.ToString(CultureInfo.InvariantCulture) : "不明")}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- 確信度: {candidate.Confidence:0.00}");
        builder.AppendLine();

        AppendList(builder, "根拠", candidate.Evidence, level: 4);
        AppendList(builder, "合わなかった点", candidate.Mismatches, level: 4);
        AppendList(builder, "参考 URL", candidate.Sources, level: 4);
    }

    private static void AppendList(
        StringBuilder builder,
        string heading,
        IReadOnlyList<string> items,
        int level = 2)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"{new string('#', level)} {heading}");
        builder.AppendLine();

        foreach (string item in items)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {item}");
        }

        builder.AppendLine();
    }
}
