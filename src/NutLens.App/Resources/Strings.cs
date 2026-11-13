using System.Globalization;
using System.Resources;

namespace NutLens.App.Resources;

/// <summary>
/// UI の文字列（D-09）。既定は日本語で、`Strings.en.resx` を足せば英語になる。
///
/// XAML からは <c>{x:Static res:Strings.AppTitle}</c> の形で参照する。
/// <b>XAML に日本語を直接書かない。</b>後からまとめて剥がすのは高くつく。
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("NutLens.App.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string name) =>
        Manager.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static string Format(string name, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(name), arguments);

    public static string AppTitle => Get(nameof(AppTitle));

    public static string TargetSectionHeader => Get(nameof(TargetSectionHeader));

    public static string TargetKindWindow => Get(nameof(TargetKindWindow));

    public static string TargetKindMonitor => Get(nameof(TargetKindMonitor));

    public static string TargetKindRegion => Get(nameof(TargetKindRegion));

    public static string RefreshTargets => Get(nameof(RefreshTargets));

    public static string SelectRegion => Get(nameof(SelectRegion));

    public static string RegionNotSelected => Get(nameof(RegionNotSelected));

    public static string RegionOverlayHint => Get(nameof(RegionOverlayHint));

    public static string FramingHint => Get(nameof(FramingHint));

    public static string SettingsSectionHeader => Get(nameof(SettingsSectionHeader));

    public static string IntervalSeconds => Get(nameof(IntervalSeconds));

    public static string DurationSeconds => Get(nameof(DurationSeconds));

    public static string DuplicateThreshold => Get(nameof(DuplicateThreshold));

    public static string MaxFrames => Get(nameof(MaxFrames));

    public static string LongEdgePixels => Get(nameof(LongEdgePixels));

    public static string RecordAudio => Get(nameof(RecordAudio));

    public static string AudioNotice => Get(nameof(AudioNotice));

    public static string WarningsHeader => Get(nameof(WarningsHeader));

    public static string WarningMediaCancelled => Get(nameof(WarningMediaCancelled));

    public static string WarningAudioFormat(string reason) => Format(nameof(WarningAudioFormat), reason);

    public static string RunSectionHeader => Get(nameof(RunSectionHeader));

    public static string StartCapture => Get(nameof(StartCapture));

    public static string StopCapture => Get(nameof(StopCapture));

    public static string OpenFolder => Get(nameof(OpenFolder));

    public static string StatusIdle => Get(nameof(StatusIdle));

    public static string StatusNoFrames => Get(nameof(StatusNoFrames));

    public static string PrivacyNotice => Get(nameof(PrivacyNotice));

    public static string KeepTemporaryDataWarning => Get(nameof(KeepTemporaryDataWarning));

    public static string ErrorTitle => Get(nameof(ErrorTitle));

    public static string ErrorNoTarget => Get(nameof(ErrorNoTarget));

    public static string ErrorNoRegion => Get(nameof(ErrorNoRegion));

    public static string ErrorCaptureUnavailable => Get(nameof(ErrorCaptureUnavailable));

    public static string ErrorTargetLost => Get(nameof(ErrorTargetLost));

    public static string ErrorCaptureDevice => Get(nameof(ErrorCaptureDevice));

    public static string RegionSelectedFormat(int width, int height, int x, int y) =>
        Format(nameof(RegionSelectedFormat), width, height, x, y);

    public static string StatusCapturingFormat(double elapsedSeconds, int received, int accepted) =>
        Format(nameof(StatusCapturingFormat), elapsedSeconds, received, accepted);

    public static string StatusDoneFormat(int saved, int received, double elapsedSeconds) =>
        Format(nameof(StatusDoneFormat), saved, received, elapsedSeconds);

    public static string StatusStoppedFormat(int saved, int received) =>
        Format(nameof(StatusStoppedFormat), saved, received);

    public static string ErrorSettingsCorruptedFormat(string backupPath) =>
        Format(nameof(ErrorSettingsCorruptedFormat), backupPath);

    public static string ErrorUnexpectedFormat(string message) =>
        Format(nameof(ErrorUnexpectedFormat), message);

    public static string ErrorClaudeNotFoundFormat(string searchedPaths) =>
        Format(nameof(ErrorClaudeNotFoundFormat), searchedPaths);

    public static string ErrorClaudeExecution => Get(nameof(ErrorClaudeExecution));

    public static string ErrorAnalyzerTemplate => Get(nameof(ErrorAnalyzerTemplate));

    // --- 解析（段階 2） ---

    public static string Analyze => Get(nameof(Analyze));

    public static string PromptWindowTitle => Get(nameof(PromptWindowTitle));

    public static string NotesSectionHeader => Get(nameof(NotesSectionHeader));

    public static string NoteSourceUrl => Get(nameof(NoteSourceUrl));

    public static string NoteAuthorName => Get(nameof(NoteAuthorName));

    public static string NoteCaption => Get(nameof(NoteCaption));

    public static string NoteFreeText => Get(nameof(NoteFreeText));

    public static string RebuildPrompt => Get(nameof(RebuildPrompt));

    public static string PromptSectionHeader => Get(nameof(PromptSectionHeader));

    public static string SendNotice => Get(nameof(SendNotice));

    public static string SendToClaude => Get(nameof(SendToClaude));

    public static string Cancel => Get(nameof(Cancel));

    public static string Close => Get(nameof(Close));

    public static string AnalysisWindowTitle => Get(nameof(AnalysisWindowTitle));

    public static string AnalysisRunningHint => Get(nameof(AnalysisRunningHint));

    public static string AnalysisCancelled => Get(nameof(AnalysisCancelled));

    public static string ResultWindowTitle => Get(nameof(ResultWindowTitle));

    public static string ResultTopCandidate => Get(nameof(ResultTopCandidate));

    public static string ResultOtherCandidates => Get(nameof(ResultOtherCandidates));

    public static string ResultNoCandidates => Get(nameof(ResultNoCandidates));

    public static string ResultEvidence => Get(nameof(ResultEvidence));

    public static string ResultMismatches => Get(nameof(ResultMismatches));

    public static string ResultSources => Get(nameof(ResultSources));

    public static string ResultObservedFeatures => Get(nameof(ResultObservedFeatures));

    public static string ResultUnverifiedPoints => Get(nameof(ResultUnverifiedPoints));

    public static string ResultSearchQueries => Get(nameof(ResultSearchQueries));

    public static string ResultCostUnknown => Get(nameof(ResultCostUnknown));

    public static string ResultRawOutput => Get(nameof(ResultRawOutput));

    public static string ResultSaveMarkdown => Get(nameof(ResultSaveMarkdown));

    public static string ReinvestigateHeader => Get(nameof(ReinvestigateHeader));

    public static string ReinvestigateHint => Get(nameof(ReinvestigateHint));

    public static string Reinvestigate => Get(nameof(Reinvestigate));

    public static string ReinvestigateUnavailable => Get(nameof(ReinvestigateUnavailable));

    public static string DataNoticeHeader => Get(nameof(DataNoticeHeader));

    public static string DataNoticeDeleted => Get(nameof(DataNoticeDeleted));

    public static string DataNoticeTranscript => Get(nameof(DataNoticeTranscript));

    public static string DataNoticeTranscriptNotFound => Get(nameof(DataNoticeTranscriptNotFound));

    public static string CopyPath => Get(nameof(CopyPath));

    public static string OpenContainingFolder => Get(nameof(OpenContainingFolder));

    public static string Copied => Get(nameof(Copied));

    /// <summary>右クリックのメニュー。</summary>
    public static string Copy => Get(nameof(Copy));

    // --- このアプリについて（配布。D-06 / D-07 / D-11） ---

    public static string About => Get(nameof(About));

    public static string AboutWindowTitle => Get(nameof(AboutWindowTitle));

    public static string AboutVersionFormat(string version) => Format(nameof(AboutVersionFormat), version);

    public static string AboutSummary => Get(nameof(AboutSummary));

    public static string AboutTabLicense => Get(nameof(AboutTabLicense));

    public static string AboutTabThirdParty => Get(nameof(AboutTabThirdParty));

    public static string AboutTabNotice => Get(nameof(AboutTabNotice));

    public static string AboutUsageNotice => Get(nameof(AboutUsageNotice));

    public static string AboutOpenRepository => Get(nameof(AboutOpenRepository));

    public static string AboutFileMissingFormat(string fileName) =>
        Format(nameof(AboutFileMissingFormat), fileName);

    // --- データの保存先（段階 5。D-05 / ADR-0033） ---

    public static string DataRootHeader => Get(nameof(DataRootHeader));

    public static string DataRootNotice => Get(nameof(DataRootNotice));

    public static string DataRootSummaryFormat(int files, long megabytes, int history) =>
        Format(nameof(DataRootSummaryFormat), files, megabytes, history);

    public static string DataRootChange => Get(nameof(DataRootChange));

    public static string DataRootPickerTitle => Get(nameof(DataRootPickerTitle));

    public static string DataRootConfirmTitle => Get(nameof(DataRootConfirmTitle));

    public static string DataRootConfirmFormat(string current, string summary, string next) =>
        Format(nameof(DataRootConfirmFormat), current, summary, next);

    public static string DataRootRestartNeeded => Get(nameof(DataRootRestartNeeded));

    public static string DataRootNotWritableFormat(string detail) =>
        Format(nameof(DataRootNotWritableFormat), detail);

    public static string DataRootCopyFailedFormat(string detail) =>
        Format(nameof(DataRootCopyFailedFormat), detail);

    public static string DataRootSourceRemainsFormat(string path) =>
        Format(nameof(DataRootSourceRemainsFormat), path);

    // --- 初回セットアップ（段階 5。D-03 / D-07 / ADR-0029） ---

    public static string SetupWindowTitle => Get(nameof(SetupWindowTitle));

    public static string SetupNoticeHeader => Get(nameof(SetupNoticeHeader));

    public static string SetupNotice => Get(nameof(SetupNotice));

    public static string SetupChecksHeader => Get(nameof(SetupChecksHeader));

    public static string SetupCheckClaudeInstalled => Get(nameof(SetupCheckClaudeInstalled));

    public static string SetupCheckClaudeUsable => Get(nameof(SetupCheckClaudeUsable));

    public static string SetupCheckWhisperModel => Get(nameof(SetupCheckWhisperModel));

    public static string SetupCheckOcrLanguage => Get(nameof(SetupCheckOcrLanguage));

    public static string SetupClaudeMissing => Get(nameof(SetupClaudeMissing));

    public static string SetupClaudeFoundFormat(string path, string version) =>
        Format(nameof(SetupClaudeFoundFormat), path, version);

    public static string SetupClaudeUsableNg => Get(nameof(SetupClaudeUsableNg));

    public static string SetupClaudeUsableUnchecked => Get(nameof(SetupClaudeUsableUnchecked));

    /// <summary><b>押したときだけ利用枠を使う</b>ことを、ボタンの文面に書く（ADR-0029 決定 1）。</summary>
    public static string SetupCheckClaudeUsableButton => Get(nameof(SetupCheckClaudeUsableButton));

    public static string SetupModelMissing => Get(nameof(SetupModelMissing));

    public static string SetupModelFoundFormat(string fileName, long megabytes) =>
        Format(nameof(SetupModelFoundFormat), fileName, megabytes);

    public static string SetupOcrMissing => Get(nameof(SetupOcrMissing));

    public static string SetupOpenClaudeDocs => Get(nameof(SetupOpenClaudeDocs));

    public static string SetupOpenLanguageSettings => Get(nameof(SetupOpenLanguageSettings));

    public static string SetupRecheck => Get(nameof(SetupRecheck));

    public static string SetupStart => Get(nameof(SetupStart));

    public static string SetupCancel => Get(nameof(SetupCancel));

    public static string SetupCancelConfirm => Get(nameof(SetupCancelConfirm));

    public static string SetupCancelWhileDownloading => Get(nameof(SetupCancelWhileDownloading));

    // --- モデルのダウンロード（段階 5。D-04 / ADR-0030） ---

    public static string ModelSectionHeader => Get(nameof(ModelSectionHeader));

    public static string ModelNotice => Get(nameof(ModelNotice));

    public static string ModelChoiceFormat(string name, long megabytes) =>
        Format(nameof(ModelChoiceFormat), name, megabytes);

    public static string ModelRecommended => Get(nameof(ModelRecommended));

    public static string ModelSizeFormat(long megabytes) => Format(nameof(ModelSizeFormat), megabytes);

    /// <summary>
    /// モデルごとの説明（ADR-0034 決定 6）。
    /// <b>Core は表示の言葉を持たない</b>ので、名前を鍵にしてここから引く。
    /// </summary>
    public static string ModelDescription(string modelName) =>
        Get("ModelDesc_" + modelName.Replace('-', '_'));

    public static string ModelDownload => Get(nameof(ModelDownload));

    public static string ModelCancel => Get(nameof(ModelCancel));

    public static string ModelProgressFormat(long received, long total) =>
        Format(nameof(ModelProgressFormat), received, total);

    public static string ModelDone => Get(nameof(ModelDone));

    public static string ModelCancelled => Get(nameof(ModelCancelled));

    public static string ModelNetworkFailed => Get(nameof(ModelNetworkFailed));

    public static string ModelCorrupted => Get(nameof(ModelCorrupted));

    // --- 更新（段階 5。D-02 / ADR-0028） ---

    public static string UpdateTitle => Get(nameof(UpdateTitle));

    public static string UpdateBodyFormat(string available, string current) =>
        Format(nameof(UpdateBodyFormat), available, current);

    public static string UpdateRestartNow => Get(nameof(UpdateRestartNow));

    public static string UpdateOnNextStart => Get(nameof(UpdateOnNextStart));

    public static string UpdateSkip => Get(nameof(UpdateSkip));

    public static string UpdateDownloadFailed => Get(nameof(UpdateDownloadFailed));

    public static string UpdateUpToDate => Get(nameof(UpdateUpToDate));

    public static string UpdateFoundFormat(string version) => Format(nameof(UpdateFoundFormat), version);

    public static string UpdateSkippedFormat(string version) => Format(nameof(UpdateSkippedFormat), version);

    public static string UpdateCheckFailed => Get(nameof(UpdateCheckFailed));

    public static string UpdateNeverChecked => Get(nameof(UpdateNeverChecked));

    public static string UpdateLastCheckedFormat(string when, string result) =>
        Format(nameof(UpdateLastCheckedFormat), when, result);

    public static string UpdateCheckOnStartup => Get(nameof(UpdateCheckOnStartup));

    public static string FrameCountFormat(int count) => Format(nameof(FrameCountFormat), count);

    public static string AnalysisRunningFormat(double elapsedSeconds) =>
        Format(nameof(AnalysisRunningFormat), elapsedSeconds);

    public static string ResultConfidenceFormat(double confidence) =>
        Format(nameof(ResultConfidenceFormat), confidence);

    public static string ResultRunInfoFormat(int turns, double seconds, string cost) =>
        Format(nameof(ResultRunInfoFormat), turns, seconds, cost);

    public static string ResultSavedFormat(string path) => Format(nameof(ResultSavedFormat), path);

    // --- Web Detection（段階 3'。F-14 / 5.6） ---

    /// <summary>この見出しを画面に出すこと自体が 5.6 の要求。</summary>
    public static string AdvancedSectionHeader => Get(nameof(AdvancedSectionHeader));

    public static string WebDetectionEnable => Get(nameof(WebDetectionEnable));

    public static string WebDetectionMaxImages => Get(nameof(WebDetectionMaxImages));

    public static string WebDetectionSetKey => Get(nameof(WebDetectionSetKey));

    public static string WebDetectionDeleteKey => Get(nameof(WebDetectionDeleteKey));

    public static string WebDetectionKeyPresent => Get(nameof(WebDetectionKeyPresent));

    public static string WebDetectionKeyMissing => Get(nameof(WebDetectionKeyMissing));

    public static string WebDetectionConsentTitle => Get(nameof(WebDetectionConsentTitle));

    /// <summary>5.6 が求める 3 点。<b>文面を変えたら同意の版を上げる</b>（ADR-0021 決定 4）。</summary>
    public static string WebDetectionConsentBodyFormat(int maxImages) =>
        Format(nameof(WebDetectionConsentBodyFormat), maxImages);

    public static string WebDetectionKeyPromptTitle => Get(nameof(WebDetectionKeyPromptTitle));

    public static string WebDetectionKeyPromptBody => Get(nameof(WebDetectionKeyPromptBody));

    public static string WebDetectionKeySaved => Get(nameof(WebDetectionKeySaved));

    public static string WebDetectionKeyDeleted => Get(nameof(WebDetectionKeyDeleted));

    public static string WebDetectionUsedFormat(int count) =>
        Format(nameof(WebDetectionUsedFormat), count);

    public static string WebDetectionFailedFormat(string reason) =>
        Format(nameof(WebDetectionFailedFormat), reason);

    public static string Save => Get(nameof(Save));

    // --- トレイ・終了（段階 4。6 章 / ADR-0025） ---

    public static string TrayStartCapture => Get(nameof(TrayStartCapture));

    public static string TrayStopCapture => Get(nameof(TrayStopCapture));

    public static string TrayOpenWindow => Get(nameof(TrayOpenWindow));

    public static string TraySettings => Get(nameof(TraySettings));

    public static string TrayExit => Get(nameof(TrayExit));

    public static string TrayTooltipCapturing => Get(nameof(TrayTooltipCapturing));

    public static string ConfirmTitle => Get(nameof(ConfirmTitle));

    public static string ConfirmCloseWithTemporaryData => Get(nameof(ConfirmCloseWithTemporaryData));

    public static string ConfirmCloseWhileCapturing => Get(nameof(ConfirmCloseWhileCapturing));

    public static string ClosedToTrayNotice => Get(nameof(ClosedToTrayNotice));

    // --- ホットキー（段階 4。F-04 / ADR-0023） ---

    public static string HotkeySectionHeader => Get(nameof(HotkeySectionHeader));

    public static string HotkeyEnable => Get(nameof(HotkeyEnable));

    public static string HotkeyGestureLabel => Get(nameof(HotkeyGestureLabel));

    public static string HotkeyHint => Get(nameof(HotkeyHint));

    public static string HotkeyInvalidFormat(string text) => Format(nameof(HotkeyInvalidFormat), text);

    public static string HotkeyRegisterFailedFormat(string gesture) =>
        Format(nameof(HotkeyRegisterFailedFormat), gesture);

    // --- 設定画面（段階 4） ---

    public static string SettingsWindowTitle => Get(nameof(SettingsWindowTitle));

    public static string SettingsTabCapture => Get(nameof(SettingsTabCapture));

    public static string SettingsTabAnalysis => Get(nameof(SettingsTabAnalysis));

    public static string SettingsTabHistory => Get(nameof(SettingsTabHistory));

    /// <summary>この見出しを画面に出すこと自体が 5.6 の要求。</summary>
    public static string SettingsTabAdvanced => Get(nameof(SettingsTabAdvanced));

    public static string SettingsClaudePath => Get(nameof(SettingsClaudePath));

    public static string SettingsMaxTurns => Get(nameof(SettingsMaxTurns));

    public static string SettingsWhisperModel => Get(nameof(SettingsWhisperModel));

    public static string SettingsWhisperEnabled => Get(nameof(SettingsWhisperEnabled));

    public static string SettingsOcrEnabled => Get(nameof(SettingsOcrEnabled));

    public static string SettingsOcrLanguage => Get(nameof(SettingsOcrLanguage));

    public static string SettingsJpegQuality => Get(nameof(SettingsJpegQuality));

    public static string SettingsCaptureCursor => Get(nameof(SettingsCaptureCursor));

    public static string SettingsKeepTemporaryData => Get(nameof(SettingsKeepTemporaryData));

    public static string SettingsKeepTemporaryDataWarning => Get(nameof(SettingsKeepTemporaryDataWarning));

    public static string SettingsSaved => Get(nameof(SettingsSaved));

    // --- 履歴（段階 4。F-28 / ADR-0024） ---

    public static string HistoryEmpty => Get(nameof(HistoryEmpty));

    public static string HistoryNotice => Get(nameof(HistoryNotice));

    public static string HistoryDelete => Get(nameof(HistoryDelete));

    public static string HistoryDeleteAll => Get(nameof(HistoryDeleteAll));

    public static string HistoryRefresh => Get(nameof(HistoryRefresh));

    public static string HistoryConfirmDeleteAll => Get(nameof(HistoryConfirmDeleteAll));

    public static string HistoryCountFormat(int count) => Format(nameof(HistoryCountFormat), count);

    public static string HistoryNoCandidate => Get(nameof(HistoryNoCandidate));
}
