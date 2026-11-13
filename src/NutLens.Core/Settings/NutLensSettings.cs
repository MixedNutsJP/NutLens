using System;

namespace NutLens.Core.Settings;

/// <summary>
/// 撮影の条件（F-02 / F-03 / F-05 / F-06）。
/// 既定値はここ 1 箇所に集める（D-05：環境に依存する値を固定で書かない）。
/// </summary>
public sealed record CaptureSettings
{
    public const double MinIntervalSeconds = 0.5;
    public const double MaxIntervalSeconds = 10.0;
    public const double MinDurationSeconds = 1.0;
    public const double MaxDurationSeconds = 3600.0;
    public const int MinMaxFrames = 1;
    public const int MaxMaxFrames = 100;
    public const int MinLongEdgePixels = 320;
    public const int MaxLongEdgePixels = 4096;
    public const int MinJpegQuality = 30;
    public const int MaxJpegQuality = 100;
    public const int MinFramesInMemory = 20;
    public const int MaxFramesInMemory = 2000;

    /// <summary>撮影間隔（秒）。F-02。</summary>
    public double IntervalSeconds { get; init; } = 2.0;

    /// <summary>撮影時間（秒）。撮影開始からの経過秒数。F-03。</summary>
    public double DurationSeconds { get; init; } = 60.0;

    /// <summary>
    /// 重複とみなすハミング距離の上限。F-05。
    /// <b>576 bit のうち何ビットまでを「同じ」とみなすか</b>（ADR-0015 で 64 bit から広げた）。
    /// </summary>
    public int DuplicateThreshold { get; init; } = Hashing.DHashDuplicateFrameFilter.DefaultThreshold;

    /// <summary>保存する最大枚数。超えたら時間的に均等に間引く。F-06。</summary>
    public int MaxFrames { get; init; } = 20;

    /// <summary>縮小後の長辺の上限（px）。F-06。</summary>
    public int LongEdgePixels { get; init; } = 1280;

    /// <summary>JPEG 品質。下げすぎるとテロップの OCR 精度に響く（ADR-0008）。</summary>
    public int JpegQuality { get; init; } = 85;

    /// <summary>
    /// 撮影中にメモリへ保持する最大枚数（ADR-0005 決定 3）。
    /// 超えたらその場で半分に間引く。上限が無いと長時間撮影で確保に失敗する。
    /// </summary>
    public int MaxFramesInMemoryLimit { get; init; } = 200;

    /// <summary>マウスカーソルを写すか。既定は写さない。</summary>
    public bool CaptureCursor { get; init; }

    /// <summary>
    /// システム音声を録るか（F-11 / ADR-0016 決定 5）。
    /// <b>ループバックはスピーカーに出ている音すべてを拾う。</b>
    /// 通知音や他のアプリの音も入るので、切れるようにしてある。
    /// </summary>
    public bool RecordAudio { get; init; } = true;

    public TimeSpan Interval => TimeSpan.FromSeconds(this.IntervalSeconds);

    public TimeSpan Duration => TimeSpan.FromSeconds(this.DurationSeconds);

    /// <summary>範囲外の値を丸める。設定ファイルを手で書き換えられても壊れないようにするため。</summary>
    public CaptureSettings Normalize()
    {
        // MaxFrames を先に丸める。メモリ上限の下限が MaxFrames に依存しており、
        // 丸める前の値（設定ファイルに 100000 などと書かれた場合）をそのまま使うと
        // Math.Clamp の min が max を超えて例外になる。
        int maxFrames = Math.Clamp(this.MaxFrames, MinMaxFrames, MaxMaxFrames);
        int memoryLowerBound = Math.Max(MinFramesInMemory, maxFrames);

        return this with
        {
            IntervalSeconds = Math.Clamp(this.IntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds),
            DurationSeconds = Math.Clamp(this.DurationSeconds, MinDurationSeconds, MaxDurationSeconds),
            DuplicateThreshold = Math.Clamp(
                this.DuplicateThreshold,
                Hashing.DHashDuplicateFrameFilter.MinThreshold,
                Hashing.DHashDuplicateFrameFilter.MaxThreshold),
            MaxFrames = maxFrames,
            LongEdgePixels = Math.Clamp(this.LongEdgePixels, MinLongEdgePixels, MaxLongEdgePixels),
            JpegQuality = Math.Clamp(this.JpegQuality, MinJpegQuality, MaxJpegQuality),
            MaxFramesInMemoryLimit = Math.Clamp(
                this.MaxFramesInMemoryLimit,
                memoryLowerBound,
                MaxFramesInMemory),
        };
    }
}

/// <summary>
/// Claude Code の起動条件（5.4）。器だけ先に置く。使うのは段階 2。
/// </summary>
public sealed record ClaudeSettings
{
    public const int MinMaxTurns = 1;
    public const int MaxMaxTurns = 200;

    /// <summary>
    /// claude コマンドのフルパス。null なら自動で探す（D-05）。
    /// 探す順は概要設計 §9.1 を参照。
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>ADR-0006 決定 5。隠しオプションなので、拒否されたら外して再実行する。</summary>
    public int MaxTurns { get; init; } = 30;

    public ClaudeSettings Normalize() => this with
    {
        MaxTurns = Math.Clamp(this.MaxTurns, MinMaxTurns, MaxMaxTurns),
    };
}

/// <summary>文字起こし（F-11 / ADR-0017）。モデルは同梱しない（D-04）。</summary>
public sealed record WhisperSettings
{
    public const string DefaultModelFileName = "ggml-base.bin";
    public const string ModelsDirectoryName = "models";

    public bool Enabled { get; init; } = true;

    /// <summary>
    /// モデルのフルパス。null なら <c>%LOCALAPPDATA%\NutLens\models\ggml-base.bin</c>。
    /// 環境に依存する値を固定で書かない（D-05）。
    /// </summary>
    public string? ModelPath { get; init; }
}

/// <summary>OCR（F-12 / ADR-0018）。</summary>
public sealed record OcrSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 使いたい認識言語のタグ（例：<c>ja</c>）。
    /// null なら ja → en → 利用できる最初のもの の順で選ぶ。
    /// </summary>
    public string? PreferredLanguage { get; init; }
}

/// <summary>自動更新（D-02 / ADR-0028）。</summary>
public sealed record UpdateSettings
{
    /// <summary>
    /// 起動時に確認するか。<b>既定 true。</b>
    /// 切れるようにしてあるのは、GitHub へ出ていく通信だから
    /// （D-08 のテレメトリとは別の話。ADR-0028 決定 7）。
    /// </summary>
    public bool CheckOnStartup { get; init; } = true;

    /// <summary>
    /// 利用者が見送った版。<b>この版はもう聞かない</b>（ADR-0028 決定 4）。
    /// 毎回聞き直すと「いいえ」を押す動作だけが習慣になる。
    /// </summary>
    public string? SkippedVersion { get; init; }

    /// <summary>
    /// 最後に確認した日時。確認の失敗は画面に出さないが、<b>忘れもしない</b>
    /// （「このアプリについて」で見られる。ADR-0028 決定 2）。
    /// </summary>
    public DateTimeOffset? LastCheckedAt { get; init; }

    /// <summary>最後の確認の結果（画面に出す短い文）。</summary>
    public string? LastCheckResult { get; init; }
}

/// <summary>初回セットアップ（D-03 / D-07 / ADR-0029）。</summary>
public sealed record SetupSettings
{
    /// <summary>
    /// 説明文の版。<b>内容を変えたら上げる。</b>上げると、もう一度画面を出す
    /// （ADR-0021 の同意と同じ考え方）。
    /// </summary>
    public const int CurrentNoticeVersion = 1;

    /// <summary>どの版の説明まで見たか。0 ＝ まだ見ていない。</summary>
    public int CompletedVersion { get; init; }

    public bool HasSeenCurrentNotice => this.CompletedVersion >= CurrentNoticeVersion;
}

/// <summary>
/// グローバルホットキー（F-04 / ADR-0023）。
///
/// <b>人が読める 1 行で持つ。</b>列挙型のビットを数値で書くと利用者が読めず、
/// 手で直せない（D-05 の「設定で変えられる」が実質失われる）。
/// </summary>
public sealed record HotkeySettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 例：<c>Ctrl+Shift+F9</c>。読めない文字列なら既定へ戻す。
    /// 受け付けるキーは A〜Z・0〜9・F1〜F24（Input.HotkeyGesture）。
    /// </summary>
    public string Gesture { get; init; } = Input.HotkeyGesture.DefaultText;

    /// <summary>
    /// 読めない文字列を既定へ戻す。
    /// <b>ここで直さないと、起動のたびに「登録できません」が出続ける。</b>
    /// </summary>
    public HotkeySettings Normalize() =>
        Input.HotkeyGesture.TryParse(this.Gesture, out Input.HotkeyGesture gesture)
            ? this with { Gesture = gesture.ToString() }
            : this with { Gesture = Input.HotkeyGesture.DefaultText };
}

/// <summary>
/// Web Detection（F-14 / 5.6 / ADR-0021）。<b>既定はオフ。</b>
///
/// <b>API キーはここに入らない。</b>資格情報マネージャーに預ける（ADR-0020）。
/// settings.json は利用者が開き、不具合報告に貼る前提のファイル。
/// </summary>
public sealed record WebDetectionSettings
{
    /// <summary>
    /// 同意文の版。<b>同意ダイアログだけがこれを書く</b>（ADR-0021 決定 2）。
    /// 文面を変えたら上げる。上げた版では、既に同意していた利用者もオフへ戻る。
    /// </summary>
    public const int CurrentConsentVersion = 1;

    public const int MinMaxImages = 1;
    public const int MaxMaxImages = 10;
    public const double MinTimeoutSeconds = 5.0;
    public const double MaxTimeoutSeconds = 120.0;

    /// <summary>
    /// 使うか。<b>既定は false</b>（5.6）。
    /// これだけ true にしても送らない（<see cref="WebDetection.WebDetectionGate"/>）。
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// 同意した版。既定 0 ＝ 未同意。
    /// 手で <c>settings.json</c> に書いても、API キーが揃わないので送信は成立しない。
    /// </summary>
    public int ConsentVersion { get; init; }

    /// <summary>1 回の解析で送る上限枚数。5.6 の初期値は 3。</summary>
    public int MaxImages { get; init; } = 3;

    /// <summary>
    /// 応答を待つ上限（秒）。
    /// <b>撮影が終わったのに画面が進まない状態を作らない</b>ための打ち切り（ADR-0022 決定 7）。
    /// </summary>
    public double TimeoutSeconds { get; init; } = 30.0;

    public TimeSpan Timeout => TimeSpan.FromSeconds(this.TimeoutSeconds);

    public WebDetectionSettings Normalize() => this with
    {
        MaxImages = Math.Clamp(this.MaxImages, MinMaxImages, MaxMaxImages),
        TimeoutSeconds = Math.Clamp(this.TimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds),
    };
}

/// <summary>%LOCALAPPDATA%\NutLens\settings.json の中身（6 章）。</summary>
public sealed record NutLensSettings
{
    /// <summary>
    /// 設定ファイルの書式の版。
    /// 2 で <see cref="CaptureSettings.DuplicateThreshold"/> の意味が変わった
    /// （64 bit 中の値 → 576 bit 中の値。ADR-0015）。
    /// </summary>
    public const int CurrentVersion = 2;

    /// <summary>設定ファイルの書式の版。</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>UI の言語。既定は日本語（D-09）。</summary>
    public string UiLanguage { get; init; } = "ja";

    /// <summary>
    /// 一時データを残すデバッグ用の設定（5.5）。
    /// <b>既定は false。</b>true にすると解析後も画像・音声が消えずに残る。
    /// </summary>
    public bool KeepTemporaryData { get; init; }

    public CaptureSettings Capture { get; init; } = new();

    public ClaudeSettings Claude { get; init; } = new();

    public WhisperSettings Whisper { get; init; } = new();

    public OcrSettings Ocr { get; init; } = new();

    /// <summary>
    /// Web Detection（F-14 / 5.6）。<b>既定はオフ。</b>
    /// 版は上げていない。既存の項目の意味が変わらないため（ADR-0021 決定 6）。
    /// </summary>
    public WebDetectionSettings WebDetection { get; init; } = new();

    /// <summary>グローバルホットキー（F-04 / ADR-0023）。</summary>
    public HotkeySettings Hotkey { get; init; } = new();

    /// <summary>自動更新（D-02 / ADR-0028）。</summary>
    public UpdateSettings Update { get; init; } = new();

    /// <summary>初回セットアップ（D-03 / ADR-0029）。</summary>
    public SetupSettings Setup { get; init; } = new();

    /// <summary>
    /// 古い版の設定ファイルを今の版に直す。読み込み時にだけ呼ぶ。
    ///
    /// 版 1 の <c>duplicateThreshold</c> は 64 bit 中の値で、既定は 5 だった。
    /// 576 bit になった今、5 は極端に厳しい設定として効く。
    /// <b>そのまま読むと「更新したら重複が除かれなくなった」という形で表れる</b>ので、既定値へ戻す。
    /// </summary>
    public NutLensSettings Migrate()
    {
        if (this.Version >= CurrentVersion)
        {
            return this;
        }

        return this with
        {
            Version = CurrentVersion,
            Capture = (this.Capture ?? new CaptureSettings()) with
            {
                DuplicateThreshold = Hashing.DHashDuplicateFrameFilter.DefaultThreshold,
            },
        };
    }

    public NutLensSettings Normalize()
    {
        // 設定ファイルは利用者が手で書き換えられる。
        // "capture": null のように書かれると、プロパティ初期化子を素通りして null が入る。
        // ここで受け止めないと、落ちるのは設定を読んだ瞬間（＝アプリの起動時）になる。
        CaptureSettings capture = this.Capture ?? new CaptureSettings();
        ClaudeSettings claude = this.Claude ?? new ClaudeSettings();

        return this with
        {
            Version = CurrentVersion,
            UiLanguage = string.IsNullOrWhiteSpace(this.UiLanguage) ? "ja" : this.UiLanguage,
            Capture = capture.Normalize(),
            Claude = claude.Normalize(),
            Whisper = this.Whisper ?? new WhisperSettings(),
            Ocr = this.Ocr ?? new OcrSettings(),
            WebDetection = (this.WebDetection ?? new WebDetectionSettings()).Normalize(),
            Hotkey = (this.Hotkey ?? new HotkeySettings()).Normalize(),
            Update = this.Update ?? new UpdateSettings(),
            Setup = this.Setup ?? new SetupSettings(),
        };
    }
}
