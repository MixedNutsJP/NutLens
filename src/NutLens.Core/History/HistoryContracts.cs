using System;
using System.Collections.Generic;
using NutLens.Core.Analysis;

namespace NutLens.Core.History;

/// <summary>履歴に残す候補 1 件。<c>MovieCandidate</c> と同じ形だが、別の型にしてある。</summary>
/// <remarks>
/// 解析結果の型をそのまま保存に使うと、解析側の都合（画面に出す派生プロパティなど）が
/// 保存の書式に漏れる。<b>残すものと受け取るものを別の型にする。</b>
/// </remarks>
public sealed record HistoryCandidate(
    string? TitleJa,
    string? TitleOriginal,
    int? Year,
    string Type,
    double Confidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Mismatches,
    IReadOnlyList<string> Sources)
{
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(this.TitleJa) ? this.TitleJa
        : !string.IsNullOrWhiteSpace(this.TitleOriginal) ? this.TitleOriginal
        : "(題名不明)";
}

/// <summary>
/// 履歴 1 件（F-28 / ADR-0024）。
///
/// <b>ここに無いものが、残してはいけないもの。</b>
/// フレームのパス・文字起こし・OCR・プロンプト・ログは入れない。
/// どれも撮影した画像・音声から作ったデータで、5.5 では一時データの側にある。
/// </summary>
public sealed record HistoryEntry(
    int Version,
    string Id,
    DateTimeOffset SavedAt,
    IReadOnlyList<HistoryCandidate> Candidates,
    IReadOnlyList<string> UnverifiedPoints,
    string? SourceUrl,
    string? AuthorName,
    string? Caption,
    string? FreeText,
    int Turns,
    double DurationSeconds,
    decimal? CostUsd)
{
    public const int CurrentVersion = 1;

    public HistoryCandidate? Top => this.Candidates.Count > 0 ? this.Candidates[0] : null;

    /// <summary>
    /// 解析の顛末と利用者のメモから作る。
    ///
    /// <b>この 1 メソッドが「残すもの」と「消すもの」の境目。</b>
    /// 引数に <c>SessionLayout</c> も文字起こしも OCR も取らないのは、
    /// 取れるようにすると、うっかり詰められるようになるため。
    /// </summary>
    public static HistoryEntry From(
        Sessions.SessionId id,
        DateTimeOffset savedAt,
        AnalysisOutcome outcome,
        UserNotes notes)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(notes);

        List<HistoryCandidate> candidates = [];
        List<string> unverified = [];

        if (outcome.Result is AnalysisResult result)
        {
            foreach (MovieCandidate candidate in result.Candidates)
            {
                candidates.Add(new HistoryCandidate(
                    candidate.TitleJa,
                    candidate.TitleOriginal,
                    candidate.Year,
                    candidate.Type,
                    candidate.Confidence,
                    candidate.Evidence,
                    candidate.Mismatches,
                    candidate.Sources));
            }

            unverified.AddRange(result.UnverifiedPoints);
        }

        return new HistoryEntry(
            CurrentVersion,
            id.ToString(),
            savedAt,
            candidates,
            unverified,
            notes.SourceUrl,
            notes.AuthorName,
            notes.Caption,
            notes.FreeText,
            outcome.RunInfo.Turns,
            outcome.RunInfo.Duration.TotalSeconds,
            outcome.RunInfo.CostUsd);
    }
}

/// <summary>
/// 履歴の保存と削除（F-28 / 5.5 / ADR-0024）。
///
/// <b>消す処理をここに閉じる。</b> 一時データを <c>ISessionWorkspace</c> に閉じたのと同じ理由
/// （CLAUDE.md §5：消す処理を他所に書かない）。
/// </summary>
public interface IHistoryStore
{
    /// <summary>新しい順。読めないファイルは<b>消さずに</b>飛ばす。</summary>
    IReadOnlyList<HistoryEntry> List();

    /// <summary>同じ <see cref="HistoryEntry.Id"/> なら上書きする（再調査で増やさない）。</summary>
    void Save(HistoryEntry entry);

    void Delete(string id);

    /// <summary>中身を消す。<b>フォルダ自体は残す。</b></summary>
    void DeleteAll();
}
