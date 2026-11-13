using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Sessions;

namespace NutLens.Core.Analysis;

/// <summary>利用者が入力する補助情報（F-13）。</summary>
public sealed record UserNotes(
    string? SourceUrl = null,
    string? AuthorName = null,
    string? Caption = null,
    string? FreeText = null)
{
    public static UserNotes Empty { get; } = new();

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(this.SourceUrl)
        && string.IsNullOrWhiteSpace(this.AuthorName)
        && string.IsNullOrWhiteSpace(this.Caption)
        && string.IsNullOrWhiteSpace(this.FreeText);
}

/// <summary>
/// プロンプトに載せるフレーム 1 枚分。パスは作業ディレクトリからの相対。
///
/// <see cref="Imaging.ITimestamped"/> を実装しているのは、Web Detection へ送る
/// 代表フレームを <see cref="Sampling.IFrameThinner"/> で選ぶため（ADR-0022 決定 1）。
/// </summary>
public sealed record FrameReference(string RelativePath, TimeSpan Elapsed) : Imaging.ITimestamped;

/// <summary>プロンプトに差し込む材料（F-21）。</summary>
public sealed record PromptInputs(
    IReadOnlyList<FrameReference> Frames,
    UserNotes Notes,
    string SchemaRelativePath,
    string? Transcript = null,
    string? OcrText = null,
    string? WebDetectionJson = null);

/// <summary>テンプレートからプロンプトを組み立てる役。</summary>
public interface IPromptComposer
{
    string Compose(PromptInputs inputs);
}

/// <summary>`claude` の実行ファイル。`.cmd` は cmd.exe 経由でしか起動できない。</summary>
public sealed record ClaudeExecutable(string Path, bool RequiresCmdShell, string Version);

/// <summary>`claude` を探して動作を確かめる役（D-05 / 5.4）。</summary>
public interface IClaudeCliLocator
{
    /// <summary>見つからなければ <see cref="Diagnostics.ClaudeNotFoundException"/>。</summary>
    ClaudeExecutable Locate(string? configuredPath);
}

/// <summary>claude を 1 回起動するための指示。</summary>
public sealed record ClaudeInvocation(
    string WorkingDirectory,
    string Prompt,
    string SchemaJson,
    SessionId SessionId,
    int MaxTurns,
    string LogFilePath,
    bool Resume,
    string? ExecutablePath);

/// <summary>claude の生の実行結果。パース前。</summary>
public sealed record ClaudeRunOutput(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);

/// <summary>claude を起動する役。実装は NutLens.Analysis（ADR-0011）。</summary>
public interface IAnalysisEngine
{
    Task<ClaudeRunOutput> RunAsync(ClaudeInvocation invocation, CancellationToken cancellationToken);
}

/// <summary>候補 1 件（schema/result.schema.json と 1 対 1）。</summary>
public sealed record MovieCandidate(
    string? TitleJa,
    string? TitleOriginal,
    int? Year,
    string Type,
    double Confidence,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Mismatches,
    IReadOnlyList<string> Sources)
{
    /// <summary>画面に出す見出し。邦題が無い作品があるので、原題で代替する。</summary>
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(this.TitleJa) ? this.TitleJa
        : !string.IsNullOrWhiteSpace(this.TitleOriginal) ? this.TitleOriginal
        : "(題名不明)";
}

/// <summary>解析結果（F-26）。</summary>
public sealed record AnalysisResult(
    IReadOnlyList<MovieCandidate> Candidates,
    IReadOnlyList<string> ObservedFeatures,
    IReadOnlyList<string> UnverifiedPoints,
    IReadOnlyList<string> SearchQueriesUsed);

/// <summary>画面に出す実行の情報（ADR-0012 決定 3）。</summary>
public sealed record AnalysisRunInfo(
    SessionId SessionId,
    int Turns,
    decimal? CostUsd,
    string? StopReason,
    int PermissionDenialCount,
    TimeSpan Duration);

/// <summary>
/// 1 回の解析の顛末。
/// <b>失敗でも生の出力を持ち続ける。</b>「JSON が壊れている」とだけ言われても利用者は何もできない。
/// </summary>
public sealed record AnalysisOutcome(
    bool Succeeded,
    AnalysisResult? Result,
    AnalysisRunInfo RunInfo,
    string RawOutput,
    string? ErrorMessage);

/// <summary>標準出力を <see cref="AnalysisOutcome"/> に変換する役（ADR-0012）。</summary>
public interface IAnalysisResultParser
{
    AnalysisOutcome Parse(SessionId sessionId, ClaudeRunOutput output);
}

/// <summary>セッションフォルダに配置した analyzer 一式の場所（F-23 / ADR-0014）。</summary>
public sealed record AnalyzerFiles(
    string ClaudeMarkdownPath,
    string PromptTemplatePath,
    string SchemaPath,
    string SchemaRelativePath,
    string SkillPath);

/// <summary>analyzer/ をセッションフォルダへ複製する役。</summary>
public interface IAnalyzerTemplateDeployer
{
    AnalyzerFiles Deploy(SessionLayout layout);
}

/// <summary>Claude Code の会話履歴を<b>探す</b>役（ADR-0013 決定 3。組み立てない）。</summary>
public interface IClaudeTranscriptLocator
{
    /// <summary>見つからなければ null。推測のパスを返さない。</summary>
    string? TryFind(SessionId sessionId);
}

public sealed record AnalysisProgress(string Message, TimeSpan Elapsed);
