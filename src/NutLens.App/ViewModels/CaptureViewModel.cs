using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NutLens.App.Mvvm;
using NutLens.App.Resources;
using NutLens.Core.Capture;
using NutLens.Core.Diagnostics;
using NutLens.Core.Media;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using NutLens.Core.WebDetection;

namespace NutLens.App.ViewModels;

/// <summary>撮影対象の選び方。</summary>
public enum TargetSelectionMode
{
    Window,
    Monitor,
    Region,
}

/// <summary>
/// メイン画面の状態。
/// 抽象にしか依存しない（具体型を知るのは合成ルートだけ。ADR-0002 決定 4）。
/// </summary>
public sealed class CaptureViewModel : ObservableObject
{
    private readonly ICaptureTargetProvider windowTargets;
    private readonly ICaptureTargetProvider monitorTargets;
    private readonly ICaptureSessionService captureSession;
    private readonly ISettingsStore settingsStore;
    private readonly IRegionSelector regionSelector;
    private readonly IUserNotifier notifier;
    private readonly IAppDataPaths paths;
    private readonly IAnalysisLauncher analysisLauncher;
    private readonly IMediaProcessingService mediaProcessing;
    private readonly IWebDetectionService webDetection;
    private readonly ISessionWorkspace workspace;
    private readonly IExceptionDescriber describer;

    private SessionLayout? lastSession;
    private CancellationTokenSource? capturing;
    private TargetSelectionMode mode = TargetSelectionMode.Window;
    private CaptureTarget? selectedTarget;
    private CaptureTarget? regionTarget;
    private string status = Strings.StatusIdle;
    private string? lastFramesDirectory;
    private double progressRatio;

    private double intervalSeconds;
    private double durationSeconds;
    private int duplicateThreshold;
    private int maxFrames;
    private int longEdgePixels;
    private bool recordAudio;

    public CaptureViewModel(
        ICaptureTargetProvider windowTargets,
        ICaptureTargetProvider monitorTargets,
        ICaptureSessionService captureSession,
        ISettingsStore settingsStore,
        IRegionSelector regionSelector,
        IUserNotifier notifier,
        IAppDataPaths paths,
        IAnalysisLauncher analysisLauncher,
        IMediaProcessingService mediaProcessing,
        IWebDetectionService webDetection,
        ISessionWorkspace workspace,
        IExceptionDescriber describer)
    {
        ArgumentNullException.ThrowIfNull(mediaProcessing);
        ArgumentNullException.ThrowIfNull(webDetection);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(describer);
        this.mediaProcessing = mediaProcessing;
        this.webDetection = webDetection;
        this.workspace = workspace;
        this.describer = describer;

        ArgumentNullException.ThrowIfNull(windowTargets);
        ArgumentNullException.ThrowIfNull(monitorTargets);
        ArgumentNullException.ThrowIfNull(captureSession);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(regionSelector);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(analysisLauncher);

        this.windowTargets = windowTargets;
        this.monitorTargets = monitorTargets;
        this.captureSession = captureSession;
        this.settingsStore = settingsStore;
        this.regionSelector = regionSelector;
        this.notifier = notifier;
        this.paths = paths;
        this.analysisLauncher = analysisLauncher;

        NutLensSettings settings = this.LoadSettings();
        this.intervalSeconds = settings.Capture.IntervalSeconds;
        this.durationSeconds = settings.Capture.DurationSeconds;
        this.duplicateThreshold = settings.Capture.DuplicateThreshold;
        this.maxFrames = settings.Capture.MaxFrames;
        this.longEdgePixels = settings.Capture.LongEdgePixels;
        this.recordAudio = settings.Capture.RecordAudio;
        this.KeepTemporaryData = settings.KeepTemporaryData;

        this.StartCommand = new AsyncRelayCommand(this.StartAsync, () => !this.IsCapturing);
        this.StopCommand = new RelayCommand(this.Stop, () => this.IsCapturing);
        this.RefreshCommand = new RelayCommand(this.RefreshTargets);
        this.SelectRegionCommand = new RelayCommand(this.SelectRegion, () => !this.IsCapturing);
        this.OpenFolderCommand = new RelayCommand(this.OpenFolder, () => this.lastFramesDirectory is not null);
        this.AnalyzeCommand = new AsyncRelayCommand(this.AnalyzeAsync, () => this.CanAnalyze);

        this.StartCommand.ExecutionFailed += (_, exception) => this.ReportError(exception);
        this.AnalyzeCommand.ExecutionFailed += (_, exception) => this.ReportError(exception);

        this.RefreshTargets();
    }

    public ObservableCollection<CaptureTarget> Targets { get; } = [];

    /// <summary>
    /// 「できなかったこと」の一覧（録音の失敗、文字起こしを飛ばした、など）。
    /// <b>警告色にしない。</b>止める必要はないが、知らないと結果の理由が分からない種類のもの。
    /// </summary>
    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>撮影の開始・停止。トレイのアイコンを切り替えるために使う（ADR-0025 決定 6）。</summary>
    public event EventHandler? CapturingChanged;

    /// <summary>
    /// 撮影したが、まだ解析していない一時データが残っているか。
    /// ウィンドウを閉じるときの確認に使う（ADR-0025 決定 3）。
    /// </summary>
    public bool HasTemporaryData => this.lastSession is not null;

    /// <summary>
    /// ホットキーとトレイから呼ぶ（F-04 / ADR-0023 決定 5）。
    /// <b>開始と停止は同じ操作。</b>2 つ覚えさせない。
    /// </summary>
    public void ToggleCapture()
    {
        if (this.IsCapturing)
        {
            this.Stop();
            return;
        }

        if (this.StartCommand.CanExecute(null))
        {
            this.StartCommand.Execute(null);
        }
    }

    /// <summary>
    /// 一時データを捨てる（5.5。ウィンドウを閉じたとき＝ADR-0025 決定 2）。
    /// 撮影中なら先に止める。
    /// </summary>
    public void DiscardTemporaryData()
    {
        this.Stop();

        if (this.lastSession is SessionLayout session)
        {
            this.workspace.Delete(session.Id);
            this.lastSession = null;
            this.lastFramesDirectory = null;
            this.Warnings.Clear();
            this.Status = Strings.StatusIdle;
            this.NotifyCapturingChanged();
        }
    }

    /// <summary>録音するか（F-11 / ADR-0016 決定 5）。</summary>
    public bool RecordAudio
    {
        get => this.recordAudio;
        set => this.SetProperty(ref this.recordAudio, value);
    }

    public AsyncRelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand SelectRegionCommand { get; }

    public RelayCommand OpenFolderCommand { get; }

    public AsyncRelayCommand AnalyzeCommand { get; }

    public bool KeepTemporaryData { get; }

    /// <summary>撮れたフレームがあるときだけ解析できる。</summary>
    public bool CanAnalyze => !this.IsCapturing && this.lastSession is not null;

    public TargetSelectionMode Mode
    {
        get => this.mode;
        set
        {
            if (this.SetProperty(ref this.mode, value))
            {
                this.RaisePropertyChanged(nameof(this.IsRegionMode));
                this.RaisePropertyChanged(nameof(this.IsListMode));
                this.RefreshTargets();
            }
        }
    }

    public bool IsRegionMode => this.Mode == TargetSelectionMode.Region;

    public bool IsListMode => !this.IsRegionMode;

    public CaptureTarget? SelectedTarget
    {
        get => this.selectedTarget;
        set => this.SetProperty(ref this.selectedTarget, value);
    }

    public string RegionDescription => this.regionTarget?.CropRegion is PixelRect region
        ? Strings.RegionSelectedFormat(region.Width, region.Height, region.X, region.Y)
        : Strings.RegionNotSelected;

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

    public bool IsCapturing => this.capturing is not null;

    public string Status
    {
        get => this.status;
        private set => this.SetProperty(ref this.status, value);
    }

    /// <summary>進捗バー用の 0〜1。撮影時間に対する経過の割合。</summary>
    public double ProgressRatio
    {
        get => this.progressRatio;
        private set => this.SetProperty(ref this.progressRatio, value);
    }

    public void RefreshTargets()
    {
        if (this.IsRegionMode)
        {
            return;
        }

        ICaptureTargetProvider provider = this.Mode == TargetSelectionMode.Window
            ? this.windowTargets
            : this.monitorTargets;

        CaptureTarget? previous = this.SelectedTarget;
        this.Targets.Clear();
        foreach (CaptureTarget target in provider.GetTargets())
        {
            this.Targets.Add(target);
        }

        // 一覧を更新しても選択が飛ばないようにする（ハンドルで突き合わせる）。
        this.SelectedTarget = this.Targets.Count == 0
            ? null
            : this.Targets.FirstOrDefaultByHandle(previous?.Handle) ?? this.Targets[0];
    }

    private void SelectRegion()
    {
        CaptureTarget? selected = this.regionSelector.SelectRegion();
        if (selected is null)
        {
            return;
        }

        this.regionTarget = selected;
        this.RaisePropertyChanged(nameof(this.RegionDescription));
    }

    private async Task StartAsync()
    {
        CaptureTarget? target = this.IsRegionMode ? this.regionTarget : this.SelectedTarget;
        if (target is null)
        {
            this.notifier.ShowError(this.IsRegionMode ? Strings.ErrorNoRegion : Strings.ErrorNoTarget);
            return;
        }

        CaptureSettings settings = this.BuildSettings();
        this.PersistSettings(settings);

        using CancellationTokenSource cts = new();
        this.capturing = cts;
        this.NotifyCapturingChanged();

        try
        {
            Progress<CaptureProgress> progress = new(this.OnProgress);
            CaptureResult result = await this.captureSession
                .RunAsync(new CaptureRequest(target, settings), progress, cts.Token)
                .ConfigureAwait(true);

            this.lastFramesDirectory = result.FrameFiles.Count > 0 ? result.FramesDirectory : null;
            this.lastSession = result.FrameFiles.Count > 0
                ? new SessionLayout(this.paths.WorkRoot, result.SessionId)
                : null;

            this.Status = result.FrameFiles.Count == 0
                ? Strings.StatusNoFrames
                : result.StoppedManually
                    ? Strings.StatusStoppedFormat(result.FrameFiles.Count, result.ReceivedFrames)
                    : Strings.StatusDoneFormat(
                        result.FrameFiles.Count,
                        result.ReceivedFrames,
                        result.Duration.TotalSeconds);

            this.Warnings.Clear();
            if (result.AudioFailureReason is string audioFailure)
            {
                this.Warnings.Add(Strings.WarningAudioFormat(audioFailure));
            }

            // 段階 3：撮影が終わってから文字起こしと OCR を行う（詳細設計 04 §2）。
            if (this.lastSession is SessionLayout captured)
            {
                await this.ProcessMediaAsync(captured, result, cts.Token).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            this.ReportError(exception);
        }
        finally
        {
            this.capturing = null;
            this.ProgressRatio = 0;
            this.NotifyCapturingChanged();
        }
    }

    private void Stop() => this.capturing?.Cancel();

    /// <summary>
    /// 撮影の後に文字起こしと OCR を行う（段階 3）。
    /// <b>ここで失敗しても撮影の結果は捨てない。</b>補助情報が無いまま解析へ進める。
    /// </summary>
    private async Task ProcessMediaAsync(
        SessionLayout layout,
        CaptureResult result,
        CancellationToken cancellationToken)
    {
        NutLensSettings settings;
        try
        {
            settings = this.settingsStore.Load();
        }
        catch (SettingsCorruptedException)
        {
            settings = new NutLensSettings();
        }

        Progress<MediaProcessingProgress> progress = new(value =>
        {
            this.Status = value.Message;
            this.ProgressRatio = Math.Clamp(value.Ratio, 0, 1);
        });

        try
        {
            MediaProcessingResult processed = await this.mediaProcessing
                .ProcessAsync(layout, result.FrameFiles, settings, progress, cancellationToken)
                .ConfigureAwait(true);

            foreach (string warning in processed.Warnings)
            {
                this.Warnings.Add(warning);
            }

            // 段階 3'：Web Detection（F-14 / ADR-0022 決定 2）。
            // 既定では 3 つの条件が揃わないので、何も送らずに戻る。
            List<string> webWarnings = [];
            await this.webDetection
                .RunAsync(layout, settings, webWarnings, cancellationToken)
                .ConfigureAwait(true);

            foreach (string warning in webWarnings)
            {
                this.Warnings.Add(warning);
            }

            this.Status = Strings.StatusDoneFormat(
                result.FrameFiles.Count,
                result.ReceivedFrames,
                result.Duration.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            this.Warnings.Add(Strings.WarningMediaCancelled);
        }
    }

    /// <summary>
    /// 解析の画面へ渡す（段階 2）。
    /// 作業フォルダが既に消えている場合（アプリ終了や次の撮影の後）は何もしない。
    /// </summary>
    private async Task AnalyzeAsync()
    {
        if (this.lastSession is not SessionLayout session)
        {
            return;
        }

        await this.analysisLauncher.LaunchAsync(session).ConfigureAwait(true);

        // 結果画面を閉じると一時データが消えるので、解析し直しはできない（5.5 / F-27）。
        this.lastSession = null;
        this.lastFramesDirectory = null;
        this.NotifyCapturingChanged();
    }

    private void OnProgress(CaptureProgress progress)
    {
        this.Status = Strings.StatusCapturingFormat(
            progress.Elapsed.TotalSeconds,
            progress.ReceivedFrames,
            progress.AcceptedFrames);

        this.ProgressRatio = this.DurationSeconds <= 0
            ? 0
            : Math.Clamp(progress.Elapsed.TotalSeconds / this.DurationSeconds, 0, 1);
    }

    private void OpenFolder()
    {
        if (this.lastFramesDirectory is null)
        {
            return;
        }

        ProcessStartInfo info = new()
        {
            FileName = this.lastFramesDirectory,
            UseShellExecute = true,
        };

        Process.Start(info)?.Dispose();
    }

    private CaptureSettings BuildSettings() => new CaptureSettings
    {
        IntervalSeconds = this.IntervalSeconds,
        DurationSeconds = this.DurationSeconds,
        DuplicateThreshold = this.DuplicateThreshold,
        MaxFrames = this.MaxFrames,
        LongEdgePixels = this.LongEdgePixels,
        RecordAudio = this.RecordAudio,
    }.Normalize();

    private NutLensSettings LoadSettings()
    {
        try
        {
            return this.settingsStore.Load();
        }
        catch (SettingsCorruptedException exception)
        {
            // 設定が壊れていても起動は妨げない（概要設計 §10.1）。
            this.notifier.ShowError(Strings.ErrorSettingsCorruptedFormat(exception.BackupPath));
            return new NutLensSettings();
        }
    }

    private void PersistSettings(CaptureSettings capture)
    {
        try
        {
            NutLensSettings current = this.settingsStore.Load();
            this.settingsStore.Save(current with { Capture = capture });
        }
        catch (SettingsCorruptedException)
        {
            // 保存に失敗しても撮影は続ける。
        }
    }

    private void NotifyCapturingChanged()
    {
        this.RaisePropertyChanged(nameof(this.IsCapturing));
        this.RaisePropertyChanged(nameof(this.CanAnalyze));
        this.RaisePropertyChanged(nameof(this.HasTemporaryData));
        this.CapturingChanged?.Invoke(this, EventArgs.Empty);
        this.StartCommand.RaiseCanExecuteChanged();
        this.StopCommand.RaiseCanExecuteChanged();
        this.SelectRegionCommand.RaiseCanExecuteChanged();
        this.OpenFolderCommand.RaiseCanExecuteChanged();
        this.AnalyzeCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 例外を「原因が分かるメッセージ」に変換する（6 章）。
    /// <b>変換そのものは持たない。</b>1 箇所に集めてある（ADR-0026）。
    /// </summary>
    private void ReportError(Exception exception)
    {
        string message = this.describer.Describe(exception);

        this.Status = message;
        this.notifier.ShowError(message);
    }
}

internal static class TargetCollectionExtensions
{
    /// <summary>ハンドルが一致する対象を探す。無ければ null。</summary>
    public static CaptureTarget? FirstOrDefaultByHandle(
        this IReadOnlyList<CaptureTarget> targets,
        nint? handle)
    {
        if (handle is not nint value)
        {
            return null;
        }

        foreach (CaptureTarget target in targets)
        {
            if (target.Handle == value)
            {
                return target;
            }
        }

        return null;
    }
}
