using System;
using NutLens.Core.Analysis;
using NutLens.Core.Sessions;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class AnalysisResultParserTests
{
    private static readonly SessionId Session = SessionId.Parse("11111111-2222-3333-4444-555555555555");

    private readonly AnalysisResultParser parser = new();

    private static ClaudeRunOutput Output(string stdout, int exitCode = 0) =>
        new(exitCode, stdout, string.Empty, TimeSpan.FromSeconds(3));

    /// <summary>実測した出力の形（ADR-0012 背景）を縮めたもの。</summary>
    private const string Envelope = """
        {
          "type": "result",
          "subtype": "success",
          "is_error": false,
          "session_id": "11111111-2222-3333-4444-555555555555",
          "num_turns": 7,
          "stop_reason": "tool_use",
          "total_cost_usd": 0.0479236,
          "permission_denials": [],
          "result": "{\"candidates\":[],\"observed_features\":[\"result 側\"],\"unverified_points\":[],\"search_queries_used\":[]}",
          "structured_output": {
            "candidates": [
              {
                "title_ja": "インサイド",
                "title_original": "Inside",
                "year": 2023,
                "type": "movie",
                "confidence": 0.85,
                "evidence": ["ペントハウスに閉じ込められる窃盗犯"],
                "mismatches": [],
                "sources": ["https://www.imdb.com/title/tt14781036/"]
              }
            ],
            "observed_features": ["実写", "豪邸"],
            "unverified_points": ["仲間が逃げる描写はソースで確認できず"],
            "search_queries_used": ["art thief trapped penthouse movie"]
          }
        }
        """;

    [Fact]
    public void structured_output_から結果を読む()
    {
        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Envelope));

        Assert.True(outcome.Succeeded);
        Assert.Null(outcome.ErrorMessage);

        AnalysisResult result = Assert.IsType<AnalysisResult>(outcome.Result);
        MovieCandidate candidate = Assert.Single(result.Candidates);
        Assert.Equal("インサイド", candidate.TitleJa);
        Assert.Equal("Inside", candidate.TitleOriginal);
        Assert.Equal(2023, candidate.Year);
        Assert.Equal("movie", candidate.Type);
        Assert.Equal(0.85, candidate.Confidence);
        Assert.Single(candidate.Evidence);
        Assert.Empty(candidate.Mismatches);
        Assert.Equal("https://www.imdb.com/title/tt14781036/", Assert.Single(candidate.Sources));
        Assert.Equal(2, result.ObservedFeatures.Count);

        // result 文字列ではなく structured_output を使っていること。
        Assert.DoesNotContain("result 側", result.ObservedFeatures);
    }

    [Fact]
    public void 実行の情報を取り出す()
    {
        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Envelope));

        Assert.Equal(7, outcome.RunInfo.Turns);
        Assert.Equal(0.0479236m, outcome.RunInfo.CostUsd);
        Assert.Equal("tool_use", outcome.RunInfo.StopReason);
        Assert.Equal(0, outcome.RunInfo.PermissionDenialCount);
        Assert.Equal(Session, outcome.RunInfo.SessionId);
    }

    [Fact]
    public void structured_output_が無ければ_result_文字列から読む()
    {
        // 予備の経路（ADR-0012 決定 1）。片方が将来の版で消えても止まらないこと。
        const string Json = """
            {
              "subtype": "success",
              "is_error": false,
              "result": "{\"candidates\":[],\"observed_features\":[\"予備の経路\"],\"unverified_points\":[],\"search_queries_used\":[]}"
            }
            """;

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.True(outcome.Succeeded);
        Assert.Equal("予備の経路", Assert.Single(outcome.Result!.ObservedFeatures));
    }

    [Fact]
    public void 候補が空でも成功として扱う()
    {
        // 「分からない」は正しい答え。失敗にしない（analyzer/CLAUDE.md 守ること 3）。
        const string Json = """
            {"subtype":"success","is_error":false,
             "structured_output":{"candidates":[],"observed_features":[],"unverified_points":["手がかり不足"],"search_queries_used":[]}}
            """;

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Result!.Candidates);
        Assert.Single(outcome.Result.UnverifiedPoints);
    }

    [Fact]
    public void is_error_が真なら失敗()
    {
        const string Json = """{"subtype":"success","is_error":true,"structured_output":{"candidates":[]}}""";

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.ErrorMessage);
    }

    [Fact]
    public void subtype_が_success_以外なら失敗()
    {
        const string Json = """{"subtype":"error_max_turns","is_error":false}""";

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.False(outcome.Succeeded);
        Assert.Contains("最大ターン数", outcome.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void 終了コードが０以外なら失敗()
    {
        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Envelope, exitCode: 1));

        Assert.False(outcome.Succeeded);
        Assert.Contains("終了コード 1", outcome.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void 壊れたJSONでも生の出力が残る()
    {
        // 「JSON が壊れている」とだけ言われても利用者は何もできない（ADR-0012 決定 2）。
        const string Broken = "これは JSON ではありません";

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Broken));

        Assert.False(outcome.Succeeded);
        Assert.Equal(Broken, outcome.RawOutput);
        Assert.NotNull(outcome.ErrorMessage);
    }

    [Fact]
    public void 出力が空なら失敗()
    {
        AnalysisOutcome outcome = this.parser.Parse(Session, Output(string.Empty));

        Assert.False(outcome.Succeeded);
        Assert.NotNull(outcome.ErrorMessage);
    }

    [Fact]
    public void 未知のフィールドがあっても落ちない()
    {
        const string Json = """
            {"subtype":"success","is_error":false,"future_field":{"x":1},
             "structured_output":{"candidates":[],"observed_features":[],"unverified_points":[],
                                  "search_queries_used":[],"brand_new":"値"}}
            """;

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.True(outcome.Succeeded);
    }

    [Fact]
    public void 候補の欠けた項目は既定値で埋まる()
    {
        // スキーマ上は省略可の項目がある（邦題・年）。null のまま画面へ流さない。
        const string Json = """
            {"subtype":"success","is_error":false,
             "structured_output":{"candidates":[{"title_original":"Inside","type":"short","confidence":0.4}],
                                  "observed_features":[],"unverified_points":[],"search_queries_used":[]}}
            """;

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        MovieCandidate candidate = Assert.Single(outcome.Result!.Candidates);
        Assert.Null(candidate.TitleJa);
        Assert.Null(candidate.Year);
        Assert.Empty(candidate.Evidence);
        Assert.Empty(candidate.Mismatches);
        Assert.Empty(candidate.Sources);
        Assert.Equal("Inside", candidate.DisplayTitle);
    }

    [Fact]
    public void 種別が無ければ_unknown_になる()
    {
        const string Json = """
            {"subtype":"success","is_error":false,
             "structured_output":{"candidates":[{"title_ja":"なにか","confidence":0.1}],
                                  "observed_features":[],"unverified_points":[],"search_queries_used":[]}}
            """;

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.Equal("unknown", Assert.Single(outcome.Result!.Candidates).Type);
    }

    [Fact]
    public void 確信度は０から１に丸められる()
    {
        const string Json = """
            {"subtype":"success","is_error":false,
             "structured_output":{"candidates":[{"title_ja":"A","type":"movie","confidence":5},
                                                {"title_ja":"B","type":"movie","confidence":-2}],
                                  "observed_features":[],"unverified_points":[],"search_queries_used":[]}}
            """;

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.Equal(1.0, outcome.Result!.Candidates[0].Confidence);
        Assert.Equal(0.0, outcome.Result.Candidates[1].Confidence);
    }

    [Fact]
    public void 出力側のセッションIDを正とする()
    {
        // 食い違ったら出力側を採用する（ADR-0007 決定 2）。
        const string Json = """
            {"subtype":"success","is_error":false,
             "session_id":"99999999-8888-7777-6666-555555555555",
             "structured_output":{"candidates":[],"observed_features":[],"unverified_points":[],"search_queries_used":[]}}
            """;

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.Equal(SessionId.Parse("99999999-8888-7777-6666-555555555555"), outcome.RunInfo.SessionId);
    }

    [Fact]
    public void 拒否されたツールの件数を数える()
    {
        // 絞り込み（ADR-0006）が意図どおりかの点検材料になる。
        const string Json = """
            {"subtype":"success","is_error":false,
             "permission_denials":[{"tool":"Bash"},{"tool":"Write"}],
             "structured_output":{"candidates":[],"observed_features":[],"unverified_points":[],"search_queries_used":[]}}
            """;

        AnalysisOutcome outcome = this.parser.Parse(Session, Output(Json));

        Assert.Equal(2, outcome.RunInfo.PermissionDenialCount);
    }
}
