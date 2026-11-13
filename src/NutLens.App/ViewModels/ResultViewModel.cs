using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.App.Mvvm;
using NutLens.App.Resources;
using NutLens.Core.Analysis;
using NutLens.Core.Diagnostics;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using NutLens.Core.WebDetection;

namespace NutLens.App.ViewModels;

/// <summary>画面に出す候補 1 件。</summary>
public sealed class CandidateViewModel
{
    public CandidateViewModel(MovieCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        this.Title = candidate.DisplayTitle;
        this.Subtitle = BuildSubtitle(candidate);
        this.ConfidenceText = Strings.ResultConfidenceFormat(candidate.Confidence);
        this.Confidence = candidate.Confidence;
        this.Evidence = candidate.Evidence;
        this.Mismatches = candidate.Mismatches;
        this.Sources = candidate.Sources;
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string ConfidenceText { get; }

    public double Confidence { get; }

    public IReadOnlyList<string> Evidence { get; }

    public IReadOnlyList<string> Mismatches { get; }

    public IReadOnlyList<string> Sources { get; }

    public bool HasEvidence => this.Evidence.Count > 0;

    public bool HasMismatches => this.Mismatches.Count > 0;

    public bool HasSources => this.Sources.Count > 0;

    private static string BuildSubtitle(MovieCandidate candidate)
    {
        List<string> parts = [];

        if (!string.IsNullOrWhiteSpace(candidate.TitleOriginal)
            && !string.Equals(candidate.TitleOriginal, candidate.DisplayTitle, StringComparison.Ordinal))
        {
            parts.Add(candidate.TitleOriginal);
        }

        if (candidate.Year is int year)
        {
            parts.Add(year.ToString(CultureInfo.CurrentCulture));
        }

        parts.Add(candidate.Type);
        return string.Join(" / ", parts);
    }
}

/// <summary>
/// 結果画面（F-26 / F-27 / ADR-0013）。
///
/// <b>この画面を閉じると一時データを消す</b>（5.5）。再調査ができるのも閉じるまで（F-27）。
/// </summary>
public sealed class ResultViewModel : ObservableObject
{
    private readonly IAnalysisService analysisService;
    private readonly ISessionWorkspace workspace;
    private readonly IClaudeTranscriptLocator transcriptLocator;
    private readonly ISettingsStore settingsStore;
    private readonly IUserNotifier notifier;
    private readonly IClipboard clipboard;
    private readonly ResultMarkdownWriter markdownWriter = new();
    private readonly SessionLayout layout;
    private readonly AnalyzerFiles analyzer;
    private readonly string? sourceUrl;

    private AnalysisOutcome outcome;
    private string? additionalInformation;
    private string? statusMessage;
    private bool busy;

    public ResultViewModel(
        IAnalysisService analysisService,
        ISessionWorkspace workspace,
        IClaudeTranscriptLocator transcriptLocator,
        ISettingsStore settingsStore,
        IUserNotifier notifier,
        IClipboard clipboard,
        SessionLayout layout,
        AnalyzerFiles analyzer,
        AnalysisOutcome outcome,
        string? sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(analysisService);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(transcriptLocator);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(outcome);

        this.analysisService = analysisService;
        this.workspace = workspace;
        this.transcriptLocator = transcriptLocator;
        this.settingsStore = settingsStore;
        this.notifier = notifier;
        this.clipboard = clipboard;
        this.layout = layout;
        this.analyzer = analyzer;
        this.outcome = outcome;
        this.sourceUrl = sourceUrl;

        this.Candidates = [];
        this.ObservedFeatures = [];
        this.UnverifiedPoints = [];
        this.SearchQueries = [];

        this.OpenUrlCommand = new RelayCommand<string>(OpenUrl);
        this.CopyTextCommand = new RelayCommand<string>(this.CopyText);
        this.SaveMarkdownCommand = new RelayCommand(this.SaveMarkdown);
        this.CopyTranscriptPathCommand = new RelayCommand(this.CopyTranscriptPath, () => this.TranscriptPath is not null);
        this.OpenTranscriptFolderCommand = new RelayCommand(this.OpenTranscriptFolder, () => this.TranscriptPath is not null);
        this.ReinvestigateCommand = new AsyncRelayCommand(this.ReinvestigateAsync, () => this.CanReinvestigate);

        this.TranscriptPath = this.transcriptLocator.TryFind(this.layout.Id);

        // 5.6：「Web Detection を使用（n 枚送信）」。失敗したときもその旨を出す。
        // 画面から画面へ引き回さず、一時フォルダの web_detection.json を読む（ADR-0022 決定 4）。
        this.WebDetectionNotice = DescribeWebDetection(this.layout.WebDetectionFile);

        this.Apply(outcome);
    }

    public ObservableCollection<CandidateViewModel> Candidates { get; }

    public ObservableCollection<string> ObservedFeatures { get; }

    public ObservableCollection<string> UnverifiedPoints { get; }

    public ObservableCollection<string> SearchQueries { get; }

    public RelayCommand<string> OpenUrlCommand { get; }

    /// <summary>参考 URL の右クリックから使う。</summary>
    public RelayCommand<string> CopyTextCommand { get; }

    public RelayCommand SaveMarkdownCommand { get; }

    public RelayCommand CopyTranscriptPathCommand { get; }

    public RelayCommand OpenTranscriptFolderCommand { get; }

    public AsyncRelayCommand ReinvestigateCommand { get; }

    /// <summary>Claude Code の会話履歴。見つからなければ null（推測のパスは出さない。ADR-0013 決定 3）。</summary>
    public string? TranscriptPath { get; private set; }

    public bool HasTranscript => this.TranscriptPath is not null;

    /// <summary>Web Detection を使ったか（5.6）。使っていなければ null。</summary>
    public string? WebDetectionNotice { get; }

    public bool HasWebDetectionNotice => this.WebDetectionNotice is not null;

    /// <summary>
    /// <c>web_detection.json</c> を読んで 1 行にする。
    /// ファイルが無ければ null＝「使っていない」。
    /// <b>「使っていない」と「試したが失敗した」を区別する</b>（5.6）。
    /// </summary>
    private static string? DescribeWebDetection(string path)
    {
        WebDetectionOutcome? web = WebDetectionDocument.TryRead(path);
        if (web is null)
        {
            return null;
        }

        return web.Succeeded
            ? Strings.WebDetectionUsedFormat(web.SentImageCount)
            : Strings.WebDetectionFailedFormat(web.Message ?? string.Empty);
    }

    public bool Succeeded => this.outcome.Succeeded;

    public bool Failed => !this.outcome.Succeeded;

    public string? ErrorMessage => this.outcome.ErrorMessage;

    public string RawOutput => this.outcome.RawOutput;

    public bool HasCandidates => this.Candidates.Count > 0;

    public bool HasNoCandidates => this.outcome.Succeeded && this.Candidates.Count == 0;

    public string RunInfoText => Strings.ResultRunInfoFormat(
        this.outcome.RunInfo.Turns,
        this.outcome.RunInfo.Duration.TotalSeconds,
        this.outcome.RunInfo.CostUsd is decimal cost
            ? cost.ToString("C4", CultureInfo.GetCultureInfo("en-US"))
            : Strings.ResultCostUnknown);

    /// <summary>F-27：再調査できるのは一時データが残っている間だけ。</summary>
    public bool CanReinvestigate => !this.busy && this.workspace.Exists(this.layout.Id);

    public string? AdditionalInformation
    {
        get => this.additionalInformation;
        set => this.SetProperty(ref this.additionalInformation, value);
    }

    public string? StatusMessage
    {
        get => this.statusMessage;
        private set => this.SetProperty(ref this.statusMessage, value);
    }

    /// <summary>画面を閉じるときに呼ぶ。一時データを完全に消す（5.5）。</summary>
    public void OnClosed()
    {
        bool keep;
        try
        {
            keep = this.settingsStore.Load().KeepTemporaryData;
        }
        catch (SettingsCorruptedException)
        {
            keep = false;
        }

        if (!keep)
        {
            this.workspace.Delete(this.layout.Id);
        }
    }

    private void Apply(AnalysisOutcome value)
    {
        this.outcome = value;

        this.Candidates.Clear();
        this.ObservedFeatures.Clear();
        this.UnverifiedPoints.Clear();
        this.SearchQueries.Clear();

        if (value.Result is AnalysisResult result)
        {
            foreach (MovieCandidate candidate in result.Candidates)
            {
                this.Candidates.Add(new CandidateViewModel(candidate));
            }

            AddRange(this.ObservedFeatures, result.ObservedFeatures);
            AddRange(this.UnverifiedPoints, result.UnverifiedPoints);
            AddRange(this.SearchQueries, result.SearchQueriesUsed);
        }

        this.TranscriptPath = this.transcriptLocator.TryFind(this.layout.Id);

        this.RaisePropertyChanged(nameof(this.Succeeded));
        this.RaisePropertyChanged(nameof(this.Failed));
        this.RaisePropertyChanged(nameof(this.ErrorMessage));
        this.RaisePropertyChanged(nameof(this.RawOutput));
        this.RaisePropertyChanged(nameof(this.HasCandidates));
        this.RaisePropertyChanged(nameof(this.HasNoCandidates));
        this.RaisePropertyChanged(nameof(this.RunInfoText));
        this.RaisePropertyChanged(nameof(this.TranscriptPath));
        this.RaisePropertyChanged(nameof(this.HasTranscript));
        this.RaisePropertyChanged(nameof(this.CanReinvestigate));
        this.CopyTranscriptPathCommand.RaiseCanExecuteChanged();
        this.OpenTranscriptFolderCommand.RaiseCanExecuteChanged();
        this.ReinvestigateCommand.RaiseCanExecuteChanged();
    }

    private async Task ReinvestigateAsync()
    {
        if (string.IsNullOrWhiteSpace(this.AdditionalInformation))
        {
            return;
        }

        if (!this.workspace.Exists(this.layout.Id))
        {
            this.notifier.ShowError(Strings.ReinvestigateUnavailable);
            return;
        }

        this.busy = true;
        this.ReinvestigateCommand.RaiseCanExecuteChanged();
        this.StatusMessage = Strings.AnalysisRunningHint;

        try
        {
            ClaudeSettings claude = this.settingsStore.Load().Claude;

            // 再調査は追加情報だけを送る。会話は --resume で引き継がれる（ADR-0007）。
            AnalysisRequest request = new(
                this.layout,
                this.analyzer,
                BuildResumePrompt(this.AdditionalInformation),
                claude,
                Resume: true);

            AnalysisOutcome result = await this.analysisService
                .AnalyzeAsync(request, progress: null, CancellationToken.None)
                .ConfigureAwait(true);

            this.Apply(result);
            this.StatusMessage = null;
            this.AdditionalInformation = null;
        }
        catch (NutLensException exception)
        {
            this.StatusMessage = null;
            this.notifier.ShowError(exception.Message);
        }
        finally
        {
            this.busy = false;
            this.ReinvestigateCommand.RaiseCanExecuteChanged();
        }
    }

    internal static string BuildResumePrompt(string additionalInformation) =>
        $"""
        先ほどの候補は外れていました。次の追加情報を踏まえて、もう一度特定してください。

        ## 追加情報

        {additionalInformation.Trim()}

        前回と同じスキーマに従った JSON だけを返してください。
        """;

    private void SaveMarkdown()
    {
        string path = Path.Combine(
            Path.GetDirectoryName(this.layout.Root) ?? Path.GetTempPath(),
            $"nutlens-{this.layout.Id}.md");

        try
        {
            // セッションフォルダの中に書くと、画面を閉じたときに一緒に消える。
            // 保存は「残したい」操作なので、一時データの外に置く。
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(documents))
            {
                path = Path.Combine(documents, $"nutlens-{this.layout.Id}.md");
            }

            File.WriteAllText(
                path,
                this.markdownWriter.Write(this.outcome, this.sourceUrl, DateTimeOffset.Now));

            this.StatusMessage = Strings.ResultSavedFormat(path);
        }
        catch (IOException exception)
        {
            this.notifier.ShowError(Strings.ErrorUnexpectedFormat(exception.Message));
        }
        catch (UnauthorizedAccessException exception)
        {
            this.notifier.ShowError(Strings.ErrorUnexpectedFormat(exception.Message));
        }
    }

    /// <summary>
    /// 任意の文字列をクリップボードへ（参考 URL の右クリックから）。
    /// 候補の題名や根拠は読み取り専用の TextBox なので、そのまま選択してコピーできる。
    /// <b>URL だけはクリックで開く必要があるので（F-26）、Hyperlink のまま右クリックで補う。</b>
    /// </summary>
    private void CopyText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        this.clipboard.SetText(text);
        this.StatusMessage = Strings.Copied;
    }

    private void CopyTranscriptPath()
    {
        if (this.TranscriptPath is string path)
        {
            this.clipboard.SetText(path);
            this.StatusMessage = Strings.Copied;
        }
    }

    private void OpenTranscriptFolder()
    {
        if (this.TranscriptPath is not string path)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory))
        {
            StartShell(directory);
        }
    }

    private static void OpenUrl(string? url)
    {
        // 想定外のスキームを既定のアプリに渡さない。
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        StartShell(uri.AbsoluteUri);
    }

    private static void StartShell(string target)
    {
        ProcessStartInfo info = new() { FileName = target, UseShellExecute = true };
        Process.Start(info)?.Dispose();
    }

    private static void AddRange(ObservableCollection<string> target, IReadOnlyList<string> source)
    {
        foreach (string item in source)
        {
            target.Add(item);
        }
    }
}

/// <summary>クリップボード。ViewModel が WPF に直接触らないための窓口。</summary>
public interface IClipboard
{
    void SetText(string text);
}
