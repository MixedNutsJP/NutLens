using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NutLens.Core.Distribution;

/// <summary>更新の確認の顛末（ADR-0028）。</summary>
public enum UpdateCheckStatus
{
    /// <summary>更新があった。</summary>
    Available,

    /// <summary>最新だった。<b>画面に出さない</b>（決定 3）。</summary>
    UpToDate,

    /// <summary>
    /// 確認できなかった（オフライン・GitHub に繋がらない・上限）。
    /// <b>画面に出さない</b>（決定 2）。利用者が頼んだ操作ではない。
    /// </summary>
    Failed,

    /// <summary>インストールされていない（ポータブル版・開発中の実行）。確認もしない。</summary>
    NotInstalled,

    /// <summary>設定で切ってある。</summary>
    Disabled,

    /// <summary>見つかったが、利用者が見送った版だった（決定 4）。</summary>
    Skipped,
}

/// <summary>更新の確認の結果。</summary>
public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string? AvailableVersion,
    string? CurrentVersion,
    string? FailureReason)
{
    public static UpdateCheckResult Of(UpdateCheckStatus status, string? reason = null) =>
        new(status, null, null, reason);

    public bool ShouldPrompt => this.Status == UpdateCheckStatus.Available;
}

/// <summary>利用者が更新にどう答えたか（ADR-0028 決定 6）。</summary>
public enum UpdateChoice
{
    /// <summary>今すぐ入れて再起動する。</summary>
    RestartNow,

    /// <summary>落としておき、次の終了時に当てる。</summary>
    OnNextStart,

    /// <summary>この版は見送る。<b>同じ版はもう聞かない。</b></summary>
    Skip,
}

/// <summary>
/// 更新の確認と適用（D-02 / ADR-0028）。実装は <c>NutLens.App</c>（Velopack に触るため）。
///
/// <b>例外を投げない。</b>確認の失敗は <see cref="UpdateCheckStatus.Failed"/> で返す。
/// </summary>
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 同意の後にだけ呼ぶ。<b>同意の前に落とさない</b>（ADR-0028 決定 6）。
    /// 落とせたら true。
    /// </summary>
    Task<bool> DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken);

    /// <summary>落とした更新を当てて再起動する。戻ってこない。</summary>
    void ApplyAndRestart();

    /// <summary>落とした更新を、次にアプリが終わったときに当てる。</summary>
    void ApplyOnNextStart();
}

/// <summary>
/// 確認の状態（D-03）。
///
/// <b>「まだ確かめていない」と「使えない」を分ける。</b>
/// <see cref="SetupCheckKind.ClaudeUsable"/> は利用者が押すまで確かめないので、
/// 一緒にすると「押していないだけ」が「使えない」として画面に出る。
/// </summary>
public enum SetupCheckState
{
    /// <summary>まだ確かめていない。</summary>
    Unknown,

    Ok,

    /// <summary>無い・使えない。<b>撮影は続けられる</b>ものがほとんど。</summary>
    Unavailable,
}

/// <summary>初回セットアップで確かめる 1 項目（D-03 / ADR-0029）。</summary>
/// <param name="Detail">
/// 添える情報。<b>「あります」のような、状態と重複する言葉は入れない。</b>
/// 状態は <see cref="State"/> が示すので、ここには場所・版・大きさなど
/// <b>読む価値のある情報だけ</b>を入れる。無ければ空文字。
/// </param>
public sealed record SetupCheck(
    SetupCheckKind Kind,
    SetupCheckState State,
    string Detail)
{
    public static SetupCheck Unknown(SetupCheckKind kind, string detail = "") =>
        new(kind, SetupCheckState.Unknown, detail);

    public static SetupCheck Missing(SetupCheckKind kind, string detail) =>
        new(kind, SetupCheckState.Unavailable, detail);

    public static SetupCheck Ok(SetupCheckKind kind, string detail = "") =>
        new(kind, SetupCheckState.Ok, detail);

    public bool IsOk => this.State == SetupCheckState.Ok;

    /// <summary>
    /// 足りないことが分かっているか。
    /// <b>まだ確かめていないものは含めない</b>（押していないだけなので）。
    /// </summary>
    public bool IsKnownMissing => this.State == SetupCheckState.Unavailable;

    public bool HasDetail => !string.IsNullOrWhiteSpace(this.Detail);
}

public enum SetupCheckKind
{
    /// <summary>claude コマンドがあるか。<b>利用枠を使わない。</b></summary>
    ClaudeInstalled,

    /// <summary>
    /// claude -p が動くか（ログイン済みか）。
    /// <b>利用者が押したときだけ確かめる</b>（ADR-0029 決定 1）。
    /// </summary>
    ClaudeUsable,

    /// <summary>Whisper のモデルがあるか。</summary>
    WhisperModel,

    /// <summary>OCR の認識言語があるか。</summary>
    OcrLanguage,
}

/// <summary>
/// 足りないものを調べる役（D-03）。
///
/// <b><see cref="SetupCheckKind.ClaudeUsable"/> はここに含めない。</b>
/// あれは利用者の利用枠を使うので、押されたときだけ別に走らせる（ADR-0029 決定 1）。
/// </summary>
public interface ISetupChecker
{
    /// <summary>利用枠を使わない確認だけを行う。</summary>
    IReadOnlyList<SetupCheck> CheckWithoutSpendingQuota();

    /// <summary>
    /// <c>claude -p</c> をごく短く 1 回だけ投げる。
    /// <b>利用者の利用枠を使う。</b>押されたときだけ呼ぶこと。
    /// </summary>
    Task<SetupCheck> CheckClaudeUsableAsync(CancellationToken cancellationToken);
}

/// <summary><see cref="IClaudeProbe"/> の顛末。</summary>
public sealed record ClaudeProbeResult(bool Succeeded, string Detail);

/// <summary>
/// <c>claude -p</c> がログイン済みで動くかを 1 回だけ確かめる（D-03 の (2)）。
///
/// <b><see cref="Analysis.IAnalysisEngine"/> と分けてある</b>（ADR-0029 影響）。
/// 解析の経路に「お試し」を混ぜると、本番の起動行に確認用の分岐が入り込む。
///
/// <b>呼ぶと利用者の利用枠を使う。</b>押されたときだけ呼ぶこと（決定 1）。
/// </summary>
public interface IClaudeProbe
{
    Task<ClaudeProbeResult> ProbeAsync(string? configuredPath, CancellationToken cancellationToken);
}

/// <summary>
/// 選べる Whisper のモデル（D-04 / ADR-0034）。
///
/// <b>表示の言葉はここに入れない</b>（ADR-0026 決定 2 / ADR-0034 決定 6）。
/// モデルごとの説明文は <c>NutLens.App</c> の <c>Strings.resx</c> が
/// <see cref="Name"/> を鍵にして持つ。
/// </summary>
/// <param name="ApproximateBytes">
/// 2026-11-14 に Hugging Face の <c>Content-Length</c> で<b>実測した</b>値。
/// 推測で書かない。
/// </param>
public sealed record WhisperModelChoice(
    string Name,
    string FileName,
    long ApproximateBytes,
    string? ExpectedSha256)
{
    /// <summary>
    /// 多言語版だけを出す（ADR-0034 決定 2）。
    ///
    /// <b>英語専用（<c>.en</c>）は出さない。</b>日本語を文字起こしできず、
    /// F-11 の「言語は自動判定し、日本語と英語に対応する」を満たせなくなる。
    ///
    /// <b><c>large-v1</c> / <c>large-v2</c> も出さない。</b>
    /// <c>large-v3</c> の旧版で同じ 2951 MB。選ぶ理由がないのに一覧が伸びる。
    ///
    /// どちらも手で置いて <c>whisper.modelPath</c> で指すことはできる（D-05）。
    /// </summary>
    public static IReadOnlyList<WhisperModelChoice> All { get; } =
    [
        // ExpectedSha256 は**まだ実測していない**（ADR-0030 宿題）。
        // null の間は大きさの確認だけを行い、ハッシュの照合は飛ばす。
        new("tiny", "ggml-tiny.bin", 74L * 1024 * 1024, null),
        new("base", "ggml-base.bin", 141L * 1024 * 1024, null),
        new("small", "ggml-small.bin", 465L * 1024 * 1024, null),
        new("medium", "ggml-medium.bin", 1462L * 1024 * 1024, null),
        new("large-v3-turbo", "ggml-large-v3-turbo.bin", 1549L * 1024 * 1024, null),
        new("large-v3", "ggml-large-v3.bin", 2951L * 1024 * 1024, null),
    ];

    /// <summary>
    /// 推奨（ADR-0034 決定 3）。
    ///
    /// <b>これは実測に基づく推奨ではない。</b>
    /// このアプリで動作を確認したのは <c>base</c> だけで（ADR-0017）、
    /// <c>small</c> 以上は一度も動かしていない。
    /// 「日本語の実用域に入る最小」という一般的な傾向に基づく。
    /// </summary>
    public static WhisperModelChoice Recommended { get; } = All[2];

    public bool IsRecommended => ReferenceEquals(this, Recommended);

    public long ApproximateMegabytes => this.ApproximateBytes / 1024 / 1024;
}

/// <summary>ダウンロードの進み具合。</summary>
public sealed record ModelDownloadProgress(long ReceivedBytes, long? TotalBytes)
{
    public double? Ratio => this.TotalBytes is long total && total > 0
        ? Math.Clamp((double)this.ReceivedBytes / total, 0, 1)
        : null;
}

public enum ModelDownloadStatus
{
    Completed,

    /// <summary>利用者が止めた。<b>途中のファイルは残す</b>（再開できるように）。</summary>
    Cancelled,

    Network,

    /// <summary>ハッシュか大きさが合わなかった。途中のファイルは消す。</summary>
    Corrupted,
}

public sealed record ModelDownloadResult(ModelDownloadStatus Status, string? Message)
{
    public bool Succeeded => this.Status == ModelDownloadStatus.Completed;
}

/// <summary>
/// Whisper のモデルを落とす役（D-04 / ADR-0030）。
/// 実装は <c>NutLens.Analysis</c>（<b>外へ出る経路はそこだけ</b>。ADR-0011）。
///
/// <b>例外を投げない。</b>失敗は <see cref="ModelDownloadStatus"/> で返す。
/// </summary>
public interface IModelDownloader
{
    Task<ModelDownloadResult> DownloadAsync(
        WhisperModelChoice model,
        string destinationPath,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);
}
