using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using NutLens.App.Resources;
using NutLens.App.Services;
using NutLens.App.ViewModels;
using NutLens.Analysis;
using NutLens.App.Views;
using NutLens.Capture.Audio;
using NutLens.Capture.Graphics;
using NutLens.Capture.Imaging;
using NutLens.Capture.Input;
using NutLens.Capture.Security;
using NutLens.Capture.Text;
using NutLens.Core.Analysis;
using NutLens.Core.Capture;
using NutLens.Core.Diagnostics;
using NutLens.Core.Distribution;
using NutLens.Core.History;
using NutLens.Core.Input;
using NutLens.Core.Media;
using NutLens.Core.Hashing;
using NutLens.Core.Imaging;
using NutLens.Core.Sampling;
using NutLens.Core.Security;
using NutLens.Core.Ui;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using NutLens.Core.WebDetection;

namespace NutLens.App;

/// <summary>
/// 合成ルート。<b>具体型を知るのはこのクラスだけ</b>（ADR-0002 決定 4）。
/// ほかの場所は抽象だけを受け取る。
/// </summary>
public partial class App : Application
{
    private ServiceProvider? services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ADR-0025 決定 1：これを変えないと、最後のウィンドウを閉じた瞬間に
        // プロセスが終わり、トレイに常駐できない。
        // 代わりに、**Shutdown() を呼び忘れるとプロセスが残り続ける**。
        // 終了の入口はトレイの「終了」1 つに絞る（決定 5）。
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        this.DispatcherUnhandledException += OnUnhandledException;

        this.services = BuildServices();

        ApplyUiLanguage(this.services.GetRequiredService<ISettingsStore>());

        // 5.5：異常終了などで残った一時データを起動時に消す。
        this.services.GetRequiredService<ISessionWorkspace>().DeleteAll();

        // D-03：初回と、足りないものがあるときだけ出す（ADR-0029 決定 3）。
        // **中止されたら起動を続けない。**
        if (!this.ShowSetupIfNeeded())
        {
            this.Shutdown();
            return;
        }

        MainWindow window = this.services.GetRequiredService<MainWindow>();
        this.MainWindow = window;
        window.Show();

        this.StartHotkey();
        this.StartTray(window);
        this.StartUpdateCheck(window);
    }

    /// <summary>
    /// 初回セットアップ（D-03 / D-07 / ADR-0029 決定 3）。
    ///
    /// 初回は必ず出す。2 回目以降は**足りないものがあるときだけ**。
    /// <b>揃っているのに毎回出さない。</b>読まれなくなる。
    /// </summary>
    /// <returns>起動を続けてよければ true。<b>中止されたら false。</b></returns>
    private bool ShowSetupIfNeeded()
    {
        if (this.services is not ServiceProvider provider)
        {
            return true;
        }

        SetupSettings setup;
        try
        {
            setup = provider.GetRequiredService<ISettingsStore>().Load().Setup;
        }
        catch (SettingsCorruptedException)
        {
            setup = new SetupSettings();
        }

        bool firstRun = !setup.HasSeenCurrentNotice;
        bool somethingMissing = false;

        foreach (SetupCheck check in provider.GetRequiredService<ISetupChecker>().CheckWithoutSpendingQuota())
        {
            // OCR は無くても困りにくいので、これだけでは画面を出さない。
            // **まだ確かめていないものも数えない**（押していないだけなので）。
            if (check.IsKnownMissing && check.Kind != SetupCheckKind.OcrLanguage)
            {
                somethingMissing = true;
            }
        }

        if (!firstRun && !somethingMissing)
        {
            return true;
        }

        SetupWindow setupWindow = new(
            provider.GetRequiredService<SetupViewModel>(),
            provider.GetRequiredService<IConfirmation>())
        {
            IsFirstRun = firstRun,
        };

        bool started = setupWindow.ShowDialog() == true;

        // **初回に中止したら、そこで終わる。**
        // D-07 は「初回セットアップに明記する」と言っているので、
        // 説明を読まずに先へ進める道を作らない。
        // 2 回目以降（足りないものがあって出したとき）は、閉じてもそのまま使える。
        return started || !firstRun;
    }

    /// <summary>
    /// 起動時に背景で更新を確認する（D-02 / ADR-0028）。
    ///
    /// <b>失敗しても画面に出さない</b>（決定 2）。
    /// <b>撮影中・解析中は出さない</b>（決定 5）。見つけた更新は持っておき、待機中に出す。
    /// </summary>
    private void StartUpdateCheck(MainWindow window)
    {
        if (this.services is not ServiceProvider provider)
        {
            return;
        }

        IUpdateService updates = provider.GetRequiredService<IUpdateService>();
        CaptureViewModel capture = provider.GetRequiredService<CaptureViewModel>();

        _ = Task.Run(async () =>
        {
            UpdateCheckResult result = await updates.CheckAsync(CancellationToken.None).ConfigureAwait(false);

            if (!result.ShouldPrompt)
            {
                return;
            }

            await this.Dispatcher.InvokeAsync(() =>
                provider.GetRequiredService<UpdatePrompt>().Offer(result, capture, window));
        });
    }

    /// <summary>
    /// ホットキーを登録する（F-04 / ADR-0023 決定 4）。
    /// <b>登録できなくても起動は続ける。</b>ボタンで撮影できる。
    /// </summary>
    private void StartHotkey()
    {
        if (this.services is not ServiceProvider provider)
        {
            return;
        }

        CaptureViewModel capture = provider.GetRequiredService<CaptureViewModel>();
        IHotkeyService hotkeys = provider.GetRequiredService<IHotkeyService>();
        hotkeys.Pressed += (_, _) => this.Dispatcher.Invoke(capture.ToggleCapture);

        HotkeySettings settings;
        try
        {
            settings = provider.GetRequiredService<ISettingsStore>().Load().Hotkey;
        }
        catch (SettingsCorruptedException)
        {
            settings = new HotkeySettings();
        }

        if (!settings.Enabled || !HotkeyGesture.TryParse(settings.Gesture, out HotkeyGesture gesture))
        {
            return;
        }

        if (!hotkeys.TryRegister(gesture))
        {
            provider.GetRequiredService<IUserNotifier>()
                .ShowError(Strings.HotkeyRegisterFailedFormat(gesture.ToString()));
        }
    }

    /// <summary>トレイに常駐する（6 章 / ADR-0025）。</summary>
    private void StartTray(MainWindow window)
    {
        if (this.services is not ServiceProvider provider)
        {
            return;
        }

        CaptureViewModel capture = provider.GetRequiredService<CaptureViewModel>();
        ITrayIconService tray = provider.GetRequiredService<ITrayIconService>();

        tray.ToggleCaptureRequested += (_, _) => this.Dispatcher.Invoke(capture.ToggleCapture);
        tray.OpenWindowRequested += (_, _) => this.Dispatcher.Invoke(window.Restore);
        tray.OpenSettingsRequested += (_, _) => this.Dispatcher.Invoke(() => OpenSettings(provider, OwnerOf(window)));
        tray.ExitRequested += (_, _) => this.Dispatcher.Invoke(() => this.ExitFromTray(window));

        // 畳んだままホットキーで撮影が始まると、画面を見ずに撮影が始まる。
        // 動いていることがアイコンで分かるようにする（ADR-0025 決定 6）。
        capture.CapturingChanged += (_, _) => tray.SetCapturing(capture.IsCapturing);

        tray.Show();
    }

    /// <summary>
    /// 終了する。<b>ここが唯一の終了の入口</b>（ADR-0025 決定 5）。
    /// 一時データの削除は <see cref="OnExit"/> に 1 本化したまま。
    /// </summary>
    private void ExitFromTray(MainWindow window)
    {
        window.CloseForShutdown();
        this.Shutdown();
    }

    /// <summary>畳まれている窓は所有者にできない（WPF が例外を投げる）。</summary>
    private static Window? OwnerOf(MainWindow window) => window.IsVisible ? window : null;

    /// <summary>設定画面を開く。トレイからもメイン画面からも同じ経路を通る。</summary>
    private static bool OpenSettings(IServiceProvider provider, Window? owner)
    {
        SettingsWindow settings = new(provider.GetRequiredService<SettingsViewModel>())
        {
            Owner = owner,
        };

        return settings.ShowDialog() == true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 5.5：アプリを終了したときに一時データを消す。
        if (this.services is not null)
        {
            TryCleanupTemporaryData(this.services);
            this.services.Dispose();
            this.services = null;
        }

        base.OnExit(e);
    }

    private static ServiceProvider BuildServices()
    {
        ServiceCollection services = new();

        // --- 置き場と設定 ---
        // 保存先は「場所の指定」→ %LOCALAPPDATA% の 2 段で決める（D-05 / ADR-0033 決定 1）。
        // 固定で書かない。
        services.AddSingleton<IAppDataPaths>(
            _ => new AppDataPaths(DataRoot.Resolve(Program.BootstrapDirectory())));
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISessionWorkspace, FileSystemSessionWorkspace>();

        // --- 純ロジック ---
        services.AddSingleton<IDuplicateFrameFilterFactory, DHashDuplicateFrameFilterFactory>();
        services.AddSingleton<IFrameThinner, EvenTimeFrameThinner>();
        services.AddSingleton(TimeProvider.System);

        // --- 外界に触る実装（NutLens.Capture） ---
        services.AddSingleton<IFrameEncoder, WicFrameEncoder>();
        services.AddSingleton<IFrameSourceFactory, WgcFrameSourceFactory>();
        services.AddSingleton<WindowCaptureTargetProvider>();
        services.AddSingleton<MonitorCaptureTargetProvider>();

        // --- 音声・OCR（段階 3） ---
        services.AddSingleton<IAudioRecorderFactory, LoopbackAudioRecorderFactory>();
        services.AddSingleton<ITranscriber, WhisperTranscriber>();
        services.AddSingleton<IOcrEngine, WindowsOcrEngine>();
        services.AddSingleton<IMediaProcessingService, MediaProcessingService>();

        // --- 取り仕切り役 ---
        services.AddSingleton<ICaptureSessionService, CaptureSessionService>();

        // --- 解析（段階 2。外部プロセスを起動するのは NutLens.Analysis だけ。ADR-0011） ---
        services.AddSingleton<IClaudeCliLocator, ClaudeCliLocator>();
        services.AddSingleton<IAnalysisEngine, ClaudeCliAnalysisEngine>();
        services.AddSingleton<IAnalysisResultParser, AnalysisResultParser>();
        services.AddSingleton<IFrameFileLister, FileSystemFrameLister>();
        services.AddSingleton<IClaudeTranscriptLocator, ClaudeTranscriptLocator>();
        services.AddSingleton<IAnalyzerTemplateDeployer>(provider => new AnalyzerTemplateDeployer(
            provider.GetRequiredService<IAppDataPaths>(),
            AppContext.BaseDirectory));
        services.AddSingleton<IAnalysisService, AnalysisService>();

        // --- Web Detection（段階 3'。外部へ出るのは NutLens.Analysis だけ。ADR-0011 / 0019） ---
        services.AddSingleton<ISecretStore, CredentialManagerSecretStore>();
        services.AddSingleton<IWebDetectionClient, VisionWebDetectionClient>();
        services.AddSingleton<IWebDetectionService, WebDetectionService>();

        // --- 履歴（段階 4。F-28 / ADR-0024） ---
        services.AddSingleton<IHistoryStore, JsonHistoryStore>();

        // --- 常駐と入力（段階 4。ADR-0023 / ADR-0025） ---
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<ITrayIconService, TrayIconService>();

        // --- 配布（段階 5。ADR-0027〜0031） ---
        // claude -p のお試しは解析の経路と分けてある（ADR-0029 影響）。
        services.AddSingleton<IClaudeProbe, ClaudeUsabilityProbe>();
        services.AddSingleton<ISetupChecker, SetupChecker>();
        // モデルのダウンロードも、外へ出る経路なので NutLens.Analysis（ADR-0011）。
        services.AddSingleton<IModelDownloader, WhisperModelDownloader>();
        services.AddSingleton<IUpdateService, VelopackUpdateService>();
        services.AddSingleton<UpdatePrompt>();
        services.AddSingleton<SetupViewModel>();

        // --- UI ---
        services.AddSingleton<IUserNotifier, MessageBoxNotifier>();
        services.AddSingleton<IConfirmation, MessageBoxConfirmation>();
        services.AddSingleton<IFolderPicker, WpfFolderPicker>();
        services.AddSingleton<IExceptionDescriber, ExceptionMessageMapper>();
        services.AddSingleton<IRegionSelector, OverlayRegionSelector>();
        services.AddSingleton<IClipboard, WpfClipboard>();
        services.AddSingleton<IAnalysisWorkflow, AnalysisWorkflow>();
        services.AddSingleton<IAnalysisLauncher, WindowAnalysisLauncher>();
        services.AddSingleton<IWebDetectionPrompt, WpfWebDetectionPrompt>();
        services.AddSingleton<WebDetectionViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton(provider => new SettingsViewModel(
            provider.GetRequiredService<ISettingsStore>(),
            provider.GetRequiredService<IHotkeyService>(),
            provider.GetRequiredService<IUserNotifier>(),
            provider.GetRequiredService<IConfirmation>(),
            provider.GetRequiredService<WebDetectionViewModel>(),
            provider.GetRequiredService<HistoryViewModel>())
        {
            FolderPicker = provider.GetRequiredService<IFolderPicker>(),
            BootstrapDirectory = Program.BootstrapDirectory(),
        });
        services.AddSingleton(provider => new CaptureViewModel(
            provider.GetRequiredService<WindowCaptureTargetProvider>(),
            provider.GetRequiredService<MonitorCaptureTargetProvider>(),
            provider.GetRequiredService<ICaptureSessionService>(),
            provider.GetRequiredService<ISettingsStore>(),
            provider.GetRequiredService<IRegionSelector>(),
            provider.GetRequiredService<IUserNotifier>(),
            provider.GetRequiredService<IAppDataPaths>(),
            provider.GetRequiredService<IAnalysisLauncher>(),
            provider.GetRequiredService<IMediaProcessingService>(),
            provider.GetRequiredService<IWebDetectionService>(),
            provider.GetRequiredService<ISessionWorkspace>(),
            provider.GetRequiredService<IExceptionDescriber>()));
        services.AddSingleton(provider => new MainWindow(
            provider.GetRequiredService<CaptureViewModel>(),
            provider.GetRequiredService<WindowCaptureTargetProvider>(),
            provider.GetRequiredService<IConfirmation>(),
            owner => OpenSettings(provider, owner)));

        return services.BuildServiceProvider();
    }

    /// <summary>D-09：設定で UI の言語を切り替える。既定は日本語。</summary>
    private static void ApplyUiLanguage(ISettingsStore settingsStore)
    {
        string language;
        try
        {
            language = settingsStore.Load().UiLanguage;
        }
        catch (Core.Diagnostics.SettingsCorruptedException)
        {
            // 設定が壊れていても起動は妨げない。通知は ViewModel 側で行う。
            language = "ja";
        }

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // 未知の言語指定は無視して既定（日本語）のままにする。
        }
    }

    private static void TryCleanupTemporaryData(IServiceProvider provider)
    {
        ISettingsStore settingsStore = provider.GetRequiredService<ISettingsStore>();

        bool keep;
        try
        {
            keep = settingsStore.Load().KeepTemporaryData;
        }
        catch (Core.Diagnostics.SettingsCorruptedException)
        {
            keep = false;
        }

        if (!keep)
        {
            provider.GetRequiredService<ISessionWorkspace>().DeleteAll();
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // 何が起きたか分からないまま落ちるのが一番困る（非機能要件）。
        MessageBox.Show(
            Strings.ErrorUnexpectedFormat(e.Exception.Message),
            Strings.ErrorTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}
