using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using NutLens.Core.Sessions;

namespace NutLens.Core.Analysis;

/// <summary>
/// `claude -p --output-format json` の標準出力を読む（ADR-0012）。
///
/// 結果は <c>structured_output</c>（オブジェクト）と <c>result</c>（同じ内容の文字列）の
/// 両方に入る（実測）。前者を使い、後者を予備にする。
/// 片方しか見ない実装は、そのフィールドが将来の版で消えたときに全面的に止まる。
/// </summary>
public sealed class AnalysisResultParser : IAnalysisResultParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public AnalysisOutcome Parse(SessionId sessionId, ClaudeRunOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        string raw = output.StandardOutput ?? string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return Failure(
                sessionId,
                output,
                raw,
                output.ExitCode == 0
                    ? "Claude Code が何も出力しませんでした。"
                    : $"Claude Code が異常終了しました（終了コード {output.ExitCode}）。");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw);
        }
        catch (JsonException exception)
        {
            return Failure(
                sessionId,
                output,
                raw,
                $"Claude Code の出力を JSON として読めませんでした: {exception.Message}");
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            AnalysisRunInfo runInfo = ReadRunInfo(sessionId, root, output.Duration);

            string? failure = DetectFailure(root, output.ExitCode);
            if (failure is not null)
            {
                return new AnalysisOutcome(false, null, runInfo, raw, failure);
            }

            if (!TryReadResult(root, out AnalysisResult? result, out string? readError))
            {
                return new AnalysisOutcome(false, null, runInfo, raw, readError);
            }

            return new AnalysisOutcome(true, result, runInfo, raw, null);
        }
    }

    /// <summary>
    /// 失敗の判定は 3 つを見る（ADR-0012 決定 2）。
    /// 終了コードだけ・<c>is_error</c> だけでは取りこぼす組み合わせがある。
    /// </summary>
    private static string? DetectFailure(JsonElement root, int exitCode)
    {
        if (exitCode != 0)
        {
            return $"Claude Code が異常終了しました（終了コード {exitCode}）。";
        }

        if (root.TryGetProperty("is_error", out JsonElement isError)
            && isError.ValueKind == JsonValueKind.True)
        {
            return "Claude Code がエラーを返しました。";
        }

        if (root.TryGetProperty("subtype", out JsonElement subtype)
            && subtype.ValueKind == JsonValueKind.String
            && !string.Equals(subtype.GetString(), "success", StringComparison.Ordinal))
        {
            string value = subtype.GetString() ?? string.Empty;
            return string.Equals(value, "error_max_turns", StringComparison.Ordinal)
                ? "解析が最大ターン数に達して打ち切られました。設定でターン数を増やすか、フレーム枚数を減らしてください。"
                : $"Claude Code が success 以外で終了しました（subtype: {value}）。";
        }

        return null;
    }

    private static bool TryReadResult(
        JsonElement root,
        out AnalysisResult? result,
        out string? error)
    {
        string? firstError = null;

        // 第一：structured_output（オブジェクトのまま入っている）
        if (root.TryGetProperty("structured_output", out JsonElement structured)
            && structured.ValueKind == JsonValueKind.Object)
        {
            if (TryDeserialize(structured.GetRawText(), out result, out firstError))
            {
                error = null;
                return true;
            }
        }

        // 予備：result（同じ内容の JSON 文字列）
        if (root.TryGetProperty("result", out JsonElement resultText)
            && resultText.ValueKind == JsonValueKind.String)
        {
            string? text = resultText.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (TryDeserialize(text, out result, out string? secondError))
                {
                    error = null;
                    return true;
                }

                firstError ??= secondError;
            }
        }

        result = null;
        error = firstError ?? "Claude Code の出力に解析結果が入っていませんでした。";
        return false;
    }

    private static bool TryDeserialize(string json, out AnalysisResult? result, out string? error)
    {
        try
        {
            AnalysisResultDto? dto = JsonSerializer.Deserialize<AnalysisResultDto>(json, SerializerOptions);
            if (dto is null)
            {
                result = null;
                error = "解析結果が空でした。";
                return false;
            }

            result = dto.ToDomain();
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            result = null;
            error = $"解析結果の形式が想定と違いました: {exception.Message}";
            return false;
        }
    }

    private static AnalysisRunInfo ReadRunInfo(SessionId sessionId, JsonElement root, TimeSpan duration)
    {
        // 出力側の session_id を正とする（ADR-0007 決定 2）。
        SessionId reported = sessionId;
        if (root.TryGetProperty("session_id", out JsonElement sessionElement)
            && sessionElement.ValueKind == JsonValueKind.String
            && SessionId.TryParse(sessionElement.GetString(), out SessionId parsed))
        {
            reported = parsed;
        }

        int turns = root.TryGetProperty("num_turns", out JsonElement turnsElement)
            && turnsElement.TryGetInt32(out int turnsValue)
                ? turnsValue
                : 0;

        decimal? cost = root.TryGetProperty("total_cost_usd", out JsonElement costElement)
            && costElement.TryGetDecimal(out decimal costValue)
                ? costValue
                : null;

        string? stopReason = root.TryGetProperty("stop_reason", out JsonElement stopElement)
            && stopElement.ValueKind == JsonValueKind.String
                ? stopElement.GetString()
                : null;

        int denials = root.TryGetProperty("permission_denials", out JsonElement denialsElement)
            && denialsElement.ValueKind == JsonValueKind.Array
                ? denialsElement.GetArrayLength()
                : 0;

        return new AnalysisRunInfo(reported, turns, cost, stopReason, denials, duration);
    }

    private static AnalysisOutcome Failure(
        SessionId sessionId,
        ClaudeRunOutput output,
        string raw,
        string message) =>
        new(
            false,
            null,
            new AnalysisRunInfo(sessionId, 0, null, null, 0, output.Duration),
            raw,
            message);

    /// <summary>
    /// 逆直列化の受け皿。スキーマに無いフィールドが来ても落ちないよう、すべて省略可にしている。
    /// <c>object</c> や <c>dynamic</c> では受けない（ADR-0010）。
    /// </summary>
    private sealed record AnalysisResultDto
    {
        [JsonPropertyName("candidates")]
        public List<CandidateDto>? Candidates { get; init; }

        [JsonPropertyName("observed_features")]
        public List<string>? ObservedFeatures { get; init; }

        [JsonPropertyName("unverified_points")]
        public List<string>? UnverifiedPoints { get; init; }

        [JsonPropertyName("search_queries_used")]
        public List<string>? SearchQueriesUsed { get; init; }

        public AnalysisResult ToDomain() => new(
            ToCandidates(this.Candidates),
            this.ObservedFeatures ?? [],
            this.UnverifiedPoints ?? [],
            this.SearchQueriesUsed ?? []);

        private static IReadOnlyList<MovieCandidate> ToCandidates(List<CandidateDto>? candidates)
        {
            if (candidates is null || candidates.Count == 0)
            {
                return [];
            }

            List<MovieCandidate> result = new(candidates.Count);
            foreach (CandidateDto candidate in candidates)
            {
                result.Add(candidate.ToDomain());
            }

            return result;
        }
    }

    private sealed record CandidateDto
    {
        [JsonPropertyName("title_ja")]
        public string? TitleJa { get; init; }

        [JsonPropertyName("title_original")]
        public string? TitleOriginal { get; init; }

        [JsonPropertyName("year")]
        public int? Year { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; init; }

        [JsonPropertyName("evidence")]
        public List<string>? Evidence { get; init; }

        [JsonPropertyName("mismatches")]
        public List<string>? Mismatches { get; init; }

        [JsonPropertyName("sources")]
        public List<string>? Sources { get; init; }

        public MovieCandidate ToDomain() => new(
            this.TitleJa,
            this.TitleOriginal,
            this.Year,
            string.IsNullOrWhiteSpace(this.Type) ? "unknown" : this.Type,
            Math.Clamp(this.Confidence ?? 0.0, 0.0, 1.0),
            this.Evidence ?? [],
            this.Mismatches ?? [],
            this.Sources ?? []);
    }
}
