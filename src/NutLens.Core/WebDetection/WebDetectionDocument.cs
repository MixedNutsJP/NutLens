using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NutLens.Core.WebDetection;

/// <summary>
/// <c>web_detection.json</c> の書式（ADR-0022 決定 3）。
///
/// <b>Google の応答をそのまま書かない。</b>載せるのは 5.6 が挙げた 3 つと、
/// 送った枚数・フレーム名だけ。
/// 生の応答には使わない画像 URL が数十件並び、プロンプトが膨らむ。
///
/// <b>要求の中身（base64 の画像、端点の URL）は書かない。</b>
/// 端点の URL にはクエリ文字列として API キーが入っている（ADR-0020 決定 4）。
/// </summary>
public sealed record WebDetectionDocument(
    string Status,
    int SentImageCount,
    string? Message,
    IReadOnlyList<WebDetectionFrameResult> Frames)
{
    /// <summary>利用者が開いて読むファイルは UTF-8（BOM つき）で書く。</summary>
    private static readonly Encoding TextEncoding =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public const string StatusOk = "ok";
    public const string StatusNetwork = "network";
    public const string StatusAuth = "auth";
    public const string StatusQuota = "quota";
    public const string StatusInvalid = "invalid";

    public static string ToStatusText(WebDetectionStatus status) => status switch
    {
        WebDetectionStatus.Ok => StatusOk,
        WebDetectionStatus.Network => StatusNetwork,
        WebDetectionStatus.Auth => StatusAuth,
        WebDetectionStatus.Quota => StatusQuota,
        _ => StatusInvalid,
    };

    public static WebDetectionStatus FromStatusText(string? text) => text switch
    {
        StatusOk => WebDetectionStatus.Ok,
        StatusNetwork => WebDetectionStatus.Network,
        StatusAuth => WebDetectionStatus.Auth,
        StatusQuota => WebDetectionStatus.Quota,
        _ => WebDetectionStatus.Invalid,
    };

    public static WebDetectionDocument From(WebDetectionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new WebDetectionDocument(
            ToStatusText(outcome.Status),
            outcome.SentImageCount,
            outcome.Message,
            outcome.Frames);
    }

    public WebDetectionOutcome ToOutcome() => new(
        FromStatusText(this.Status),
        this.SentImageCount,
        this.Frames ?? [],
        this.Message);

    /// <summary>
    /// 書く。<b>成功でも失敗でも書く</b>（ADR-0022 決定 4）。
    /// 結果画面が「使っていない」と「試したが失敗した」を区別できるようにするため。
    /// </summary>
    public static void Write(string path, WebDetectionOutcome outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(outcome);

        string json = JsonSerializer.Serialize(
            From(outcome),
            WebDetectionJsonContext.Default.WebDetectionDocument);

        File.WriteAllText(path, json, TextEncoding);
    }

    /// <summary>
    /// 読む。ファイルが無い・壊れている場合は null。
    /// <b>例外にしない。</b>参考情報が読めないだけで結果画面を落とさない。
    /// </summary>
    public static WebDetectionOutcome? TryRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            WebDetectionDocument? document = JsonSerializer.Deserialize(
                json,
                WebDetectionJsonContext.Default.WebDetectionDocument);

            return document?.ToOutcome();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// プロンプトへ差し込む本文を返す。<b><c>status</c> が <c>ok</c> のときだけ。</b>
    /// それ以外は null（プロンプトには「使っていません」が入る。ADR-0022 決定 6）。
    ///
    /// 解釈をここに閉じるのは、<c>PromptComposer</c> を置換だけの役のまま保つため。
    /// あれに JSON の解釈を足すと、テンプレートの不具合と応答の不具合が同じ場所で起きる。
    /// </summary>
    public static string? TryReadReference(string path)
    {
        WebDetectionOutcome? outcome = TryRead(path);
        if (outcome is null || !outcome.Succeeded || outcome.Frames.Count == 0)
        {
            return null;
        }

        return Format(outcome);
    }

    /// <summary>
    /// 人が読める形に整える。JSON をそのままプロンプトに貼ると、
    /// 括弧と引用符がプロンプトの大半を占める。
    /// </summary>
    public static string Format(WebDetectionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        StringBuilder builder = new();
        builder.AppendLine(string.Format(
            CultureInfo.InvariantCulture,
            "（外部の画像検索の結果。{0} 枚を送信。**誤りを含む可能性がある**）",
            outcome.SentImageCount));

        foreach (WebDetectionFrameResult frame in outcome.Frames)
        {
            builder.AppendLine();
            builder.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "## `{0}`（撮影開始から {1:0.0} 秒）",
                frame.File,
                frame.ElapsedSeconds));

            AppendLabels(builder, frame.BestGuessLabels);
            AppendEntities(builder, frame.WebEntities);
            AppendPages(builder, frame.PagesWithMatchingImages);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendLabels(StringBuilder builder, IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
        {
            return;
        }

        builder.Append("- 推測ラベル: ").AppendLine(string.Join(" / ", labels));
    }

    private static void AppendEntities(StringBuilder builder, IReadOnlyList<WebEntity> entities)
    {
        if (entities.Count == 0)
        {
            return;
        }

        builder.AppendLine("- 推測された実体:");
        foreach (WebEntity entity in entities)
        {
            builder.Append("  - ").Append(entity.Description);
            if (entity.Score is double score)
            {
                builder.Append(string.Format(CultureInfo.InvariantCulture, "（{0:0.00}）", score));
            }

            builder.AppendLine();
        }
    }

    private static void AppendPages(StringBuilder builder, IReadOnlyList<WebPageMatch> pages)
    {
        if (pages.Count == 0)
        {
            return;
        }

        builder.AppendLine("- 同じ画像が載っているページ:");
        foreach (WebPageMatch page in pages)
        {
            builder.Append("  - ").Append(page.Title).Append(" — ").AppendLine(page.Url);
        }
    }
}

/// <summary>
/// ソース生成の文脈。<c>JsonDocument</c> のまま持ち回らない（CLAUDE.md §4.1）。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WebDetectionDocument))]
internal sealed partial class WebDetectionJsonContext : JsonSerializerContext
{
}
