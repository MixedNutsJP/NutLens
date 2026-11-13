using System;
using NutLens.App.Mvvm;
using NutLens.App.Resources;
using NutLens.Core.Diagnostics;
using NutLens.Core.Input;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.App.ViewModels;

/// <summary>
/// 設定画面（詳細設計 06 §6）。
///
/// <b>「保存」で明示的に保存する。</b>閉じるボタンでは保存しない。
/// 例外は「値の編集」ではなく「操作」であるもの
/// （API キーの設定・削除、Web Detection の有効化、履歴の削除）。
/// </summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore store;
    private readonly IHotkeyService hotkeys;
    private readonly IUserNotifier notifier;
    private readonly IConfirmation confirmation;

    private double intervalSeconds;
    private double durationSeconds;
    private int duplicateThreshold;
    private int maxFrames;
    private int longEdgePixels;
    private int jpegQuality;
    private bool captureCursor;
    private bool recordAudio;

    private string? claudePath;
    private int maxTurns;
    private bool whisperEnabled;
    private string? whisperModelPath;
    private bool ocrEnabled;
    private string? ocrLanguage;

    private string dataRootPath = string.Empty;
    private string dataRootSummary = string.Empty;
    private bool hotkeyEnabled;
    private string hotkeyGesture = HotkeyGesture.DefaultText;
    private bool keepTemporaryData;

    public SettingsViewModel(
        ISettingsStore store,
        IHotkeyService hotkeys,
        IUserNotifier notifier,
        IConfirmation confirmation,
        WebDetectionViewModel webDetection,
        HistoryViewModel history)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(hotkeys);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(webDetection);
        ArgumentNullException.ThrowIfNull(history);

        this.store = store;
        this.hotkeys = hotkeys;
        this.notifier = notifier;
        this.confirmation = confirmation;
        this.WebDetection = webDetection;
        this.History = history;

        this.SaveCommand = new RelayCommand(this.Save);
        this.ChangeDataRootCommand = new RelayCommand(this.ChangeDataRoot);

        this.Load();
        this.RefreshDataRoot();
    }

    /// <summary>
    /// 保存先を選ばせる役。<b>設定画面がフォルダ選択の窓を直に開かないため</b>の窓口
    /// （ADR-0002 決定 4：ViewModel は抽象だけを受け取る）。
    /// </summary>
    public IFolderPicker? FolderPicker { get; init; }

    /// <summary>「場所の指定」を探すフォルダ。合成ルートが渡す（ADR-0033 決定 2）。</summary>
    public string BootstrapDirectory { get; init; } = string.Empty;

    /// <summary>段階 3' で作ったもの。メイン画面からここへ移した（ADR-0021 決定 5）。</summary>
    public WebDetectionViewModel WebDetection { get; }

    public HistoryViewModel History { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand ChangeDataRootCommand { get; }

    /// <summary>保存できたか。画面はこれを見て閉じる。</summary>
    public bool Saved { get; private set; }

    /// <summary>
    /// いまの保存先。<b>常時表示する。</b>
    /// 解決が 2 段あるので、画面を見れば「いまどこを見ているか」が分かるようにする
    /// （ADR-0033 決定 1）。
    /// </summary>
    public string DataRootPath
    {
        get => this.dataRootPath;
        private set => this.SetProperty(ref this.dataRootPath, value);
    }

    public string DataRootSummary
    {
        get => this.dataRootSummary;
        private set => this.SetProperty(ref this.dataRootSummary, value);
    }

    /// <summary>
    /// 保存先を変える（ADR-0033 決定 3・4）。
    ///
    /// <b>コピー → 照合 → 指定を書く → 元を消す</b>の順。
    /// 先に消すと、指定を書く直前で落ちたときに
    /// データは移ったのに指定は古いまま、という状態になる。
    /// </summary>
    private void ChangeDataRoot()
    {
        if (this.FolderPicker is not IFolderPicker picker
            || string.IsNullOrWhiteSpace(this.BootstrapDirectory))
        {
            return;
        }

        if (picker.Pick(Strings.DataRootPickerTitle, this.DataRootPath) is not string chosen)
        {
            return;
        }

        DataRootSummary summary = DataRoot.Describe(this.DataRootPath);

        // はい＝移す／いいえ＝移さない／キャンセル＝変更しない。
        // **「移さない」を残す。** 大きなモデルを手で移したい利用者がいる。
        bool? move = this.confirmation.AskThreeWay(
            Strings.DataRootConfirmFormat(
                this.DataRootPath,
                Strings.DataRootSummaryFormat(summary.FileCount, summary.TotalMegabytes, summary.HistoryCount),
                chosen),
            Strings.DataRootConfirmTitle);

        if (move is null)
        {
            return;
        }

        DataRootMoveResult result = DataRoot.Switch(
            this.BootstrapDirectory,
            this.DataRootPath,
            chosen,
            move.Value);

        switch (result.Status)
        {
            case DataRootMoveStatus.DestinationNotWritable:
                this.notifier.ShowError(Strings.DataRootNotWritableFormat(result.Detail ?? string.Empty));
                return;

            case DataRootMoveStatus.CopyFailed:
                this.notifier.ShowError(Strings.DataRootCopyFailedFormat(result.Detail ?? string.Empty));
                return;

            case DataRootMoveStatus.MovedButSourceRemains:
                this.notifier.ShowInformation(
                    Strings.DataRootSourceRemainsFormat(result.Detail ?? string.Empty));
                break;

            default:
                this.notifier.ShowInformation(Strings.DataRootRestartNeeded);
                break;
        }

        this.RefreshDataRoot();
    }

    private void RefreshDataRoot()
    {
        this.DataRootPath = DataRoot.Resolve(this.BootstrapDirectory);

        DataRootSummary summary = DataRoot.Describe(this.DataRootPath);
        this.DataRootSummary = Strings.DataRootSummaryFormat(
            summary.FileCount,
            summary.TotalMegabytes,
            summary.HistoryCount);
    }

    public double IntervalSeconds
    {
        get => this.intervalSeconds;
        set => this.SetProperty(ref this.intervalSeconds, value);
    }

    public double DurationSeconds
    {
        get => this.durationSeconds;
        set => this.SetProperty(ref this.durationSeconds, value);
    }

    public int DuplicateThreshold
    {
        get => this.duplicateThreshold;
        set => this.SetProperty(ref this.duplicateThreshold, value);
    }

    public int MaxFrames
    {
        get => this.maxFrames;
        set => this.SetProperty(ref this.maxFrames, value);
    }

    public int LongEdgePixels
    {
        get => this.longEdgePixels;
        set => this.SetProperty(ref this.longEdgePixels, value);
    }

    public int JpegQuality
    {
        get => this.jpegQuality;
        set => this.SetProperty(ref this.jpegQuality, value);
    }

    public bool CaptureCursor
    {
        get => this.captureCursor;
        set => this.SetProperty(ref this.captureCursor, value);
    }

    public bool RecordAudio
    {
        get => this.recordAudio;
        set => this.SetProperty(ref this.recordAudio, value);
    }

    public string? ClaudePath
    {
        get => this.claudePath;
        set => this.SetProperty(ref this.claudePath, value);
    }

    public int MaxTurns
    {
        get => this.maxTurns;
        set => this.SetProperty(ref this.maxTurns, value);
    }

    public bool WhisperEnabled
    {
        get => this.whisperEnabled;
        set => this.SetProperty(ref this.whisperEnabled, value);
    }

    public string? WhisperModelPath
    {
        get => this.whisperModelPath;
        set => this.SetProperty(ref this.whisperModelPath, value);
    }

    public bool OcrEnabled
    {
        get => this.ocrEnabled;
        set => this.SetProperty(ref this.ocrEnabled, value);
    }

    public string? OcrLanguage
    {
        get => this.ocrLanguage;
        set => this.SetProperty(ref this.ocrLanguage, value);
    }

    public bool HotkeyEnabled
    {
        get => this.hotkeyEnabled;
        set => this.SetProperty(ref this.hotkeyEnabled, value);
    }

    public string HotkeyGestureText
    {
        get => this.hotkeyGesture;
        set => this.SetProperty(ref this.hotkeyGesture, value);
    }

    public bool KeepTemporaryData
    {
        get => this.keepTemporaryData;
        set => this.SetProperty(ref this.keepTemporaryData, value);
    }

    private void Load()
    {
        NutLensSettings settings;
        try
        {
            settings = this.store.Load();
        }
        catch (SettingsCorruptedException)
        {
            settings = new NutLensSettings();
        }

        this.intervalSeconds = settings.Capture.IntervalSeconds;
        this.durationSeconds = settings.Capture.DurationSeconds;
        this.duplicateThreshold = settings.Capture.DuplicateThreshold;
        this.maxFrames = settings.Capture.MaxFrames;
        this.longEdgePixels = settings.Capture.LongEdgePixels;
        this.jpegQuality = settings.Capture.JpegQuality;
        this.captureCursor = settings.Capture.CaptureCursor;
        this.recordAudio = settings.Capture.RecordAudio;

        this.claudePath = settings.Claude.ExecutablePath;
        this.maxTurns = settings.Claude.MaxTurns;
        this.whisperEnabled = settings.Whisper.Enabled;
        this.whisperModelPath = settings.Whisper.ModelPath;
        this.ocrEnabled = settings.Ocr.Enabled;
        this.ocrLanguage = settings.Ocr.PreferredLanguage;

        this.hotkeyEnabled = settings.Hotkey.Enabled;
        this.hotkeyGesture = settings.Hotkey.Gesture;
        this.keepTemporaryData = settings.KeepTemporaryData;
    }

    /// <summary>
    /// 保存する。
    ///
    /// <b>ホットキーは「登録してから保存」の順を守る</b>（ADR-0023 決定 6）。
    /// 逆にすると、効かないキーが設定ファイルに残り、次の起動から黙って効かなくなる。
    /// </summary>
    private void Save()
    {
        this.Saved = false;

        if (!HotkeyGesture.TryParse(this.hotkeyGesture, out HotkeyGesture gesture))
        {
            this.notifier.ShowError(Strings.HotkeyInvalidFormat(this.hotkeyGesture));
            return;
        }

        if (this.hotkeyEnabled)
        {
            if (!this.hotkeys.TryRegister(gesture))
            {
                // 登録できなかったので保存しない。古い登録はそのまま残る。
                this.notifier.ShowError(Strings.HotkeyRegisterFailedFormat(gesture.ToString()));
                return;
            }
        }
        else
        {
            this.hotkeys.Unregister();
        }

        NutLensSettings current;
        try
        {
            current = this.store.Load();
        }
        catch (SettingsCorruptedException)
        {
            current = new NutLensSettings();
        }

        this.store.Save(current with
        {
            KeepTemporaryData = this.keepTemporaryData,
            Capture = current.Capture with
            {
                IntervalSeconds = this.intervalSeconds,
                DurationSeconds = this.durationSeconds,
                DuplicateThreshold = this.duplicateThreshold,
                MaxFrames = this.maxFrames,
                LongEdgePixels = this.longEdgePixels,
                JpegQuality = this.jpegQuality,
                CaptureCursor = this.captureCursor,
                RecordAudio = this.recordAudio,
            },
            Claude = current.Claude with
            {
                ExecutablePath = Blank(this.claudePath),
                MaxTurns = this.maxTurns,
            },
            Whisper = current.Whisper with
            {
                Enabled = this.whisperEnabled,
                ModelPath = Blank(this.whisperModelPath),
            },
            Ocr = current.Ocr with
            {
                Enabled = this.ocrEnabled,
                PreferredLanguage = Blank(this.ocrLanguage),
            },
            Hotkey = current.Hotkey with
            {
                Enabled = this.hotkeyEnabled,
                Gesture = gesture.ToString(),
            },
        });

        this.Saved = true;
        this.notifier.ShowInformation(Strings.SettingsSaved);
    }

    /// <summary>空欄は null にする。「自動で探す」と「空文字のパス」を区別するため（D-05）。</summary>
    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
