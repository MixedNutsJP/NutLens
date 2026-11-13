using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NutLens.Analysis;

// Google Cloud Vision の images:annotate の要求と応答（ADR-0019 決定 4）。
//
// JsonDocument のまま持ち回らない（CLAUDE.md §4.1：object で受け流さない）。
// 型にしておくと、Google 側の書式変更が「逆直列化の失敗」として表れる。
// 木を手で歩くと、同じ変更が「画面に何も出ない」形で表れ、原因に辿り着けない。

/// <summary>要求の最上位。<c>requests</c> は配列なので、複数枚を 1 往復で送れる。</summary>
internal sealed record VisionAnnotateRequest(IReadOnlyList<VisionImageRequest> Requests);

internal sealed record VisionImageRequest(
    VisionImage Image,
    IReadOnlyList<VisionFeature> Features);

/// <param name="Content">JPEG の base64。<b>これをログにも応答の記録にも書かない。</b></param>
internal sealed record VisionImage(string Content);

/// <summary>
/// 要求する機能。
/// <b><c>WEB_DETECTION</c> 以外を入れない。</b>特に <c>FACE_DETECTION</c> は
/// 顔の検出そのもので、要件が明示的に禁じている（ADR-0019 決定 2）。
/// </summary>
internal sealed record VisionFeature(string Type, int MaxResults);

/// <summary>応答の最上位。失敗のときは <c>responses</c> が無く <c>error</c> だけが返る。</summary>
internal sealed record VisionAnnotateResponse(
    IReadOnlyList<VisionImageResponse>? Responses,
    VisionError? Error);

/// <summary>画像 1 枚分の応答。要求と同じ順で返る。</summary>
internal sealed record VisionImageResponse(
    VisionWebDetection? WebDetection,
    VisionError? Error);

/// <summary>
/// 使うのは 3 つだけ。
/// <c>fullMatchingImages</c> / <c>partialMatchingImages</c> / <c>visuallySimilarImages</c> は
/// <b>型にすら起こさない</b>（ADR-0019 決定 5）。画像 URL が数十件並ぶだけで作品名に繋がらない。
/// </summary>
internal sealed record VisionWebDetection(
    IReadOnlyList<VisionWebEntity>? WebEntities,
    IReadOnlyList<VisionWebPage>? PagesWithMatchingImages,
    IReadOnlyList<VisionBestGuessLabel>? BestGuessLabels);

internal sealed record VisionWebEntity(string? EntityId, string? Description, double? Score);

internal sealed record VisionWebPage(string? Url, string? PageTitle);

internal sealed record VisionBestGuessLabel(string? Label, string? LanguageCode);

/// <summary>Google 標準のエラー。<c>status</c> は <c>RESOURCE_EXHAUSTED</c> など。</summary>
internal sealed record VisionError(int? Code, string? Message, string? Status);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VisionAnnotateRequest))]
[JsonSerializable(typeof(VisionAnnotateResponse))]
internal sealed partial class VisionJsonContext : JsonSerializerContext
{
}
