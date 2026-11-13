using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NutLens.App.ViewModels;
using NutLens.App.Views;
using NutLens.Core.Analysis;
using NutLens.Core.Diagnostics;
using NutLens.Core.History;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.App.Services;

/// <summary>
/// 撮影の後「プロンプト確認 → 解析 → 結果」の 3 画面をつなぐ役。
///
/// ViewModel の中からウィンドウを開くと、どの画面がどの順で出るかが追えなくなる。
/// 画面の遷移をこの 1 クラスに集める。
/// </summary>
public interface IAnalysisWorkflow
{
    Task RunAsync(SessionLayout layout, Window owner);
}

/// <inheritdoc />
public sealed class AnalysisWorkflow : IAnalysisWorkflow
{
    private readonly IServiceProvider services;
    private readonly IAnalysisService analysisService;
    private readonly ISettingsStore settingsStore;
    private readonly IUserNotifier notifier;
    private readonly IHistoryStore history;
    private readonly IExceptionDescriber describer;
    private readonly TimeProvider timeProvider;

    public AnalysisWorkflow(
        IServiceProvider services,
        IAnalysisService analysisService,
        ISettingsStore settingsStore,
        IUserNotifier notifier,
        IHistoryStore history,
        IExceptionDescriber describer,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(analysisService);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(describer);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.services = services;
        this.analysisService = analysisService;
        this.settingsStore = settingsStore;
        this.notifier = notifier;
        this.history = history;
        this.describer = describer;
        this.timeProvider = timeProvider;
    }

    public async Task RunAsync(SessionLayout layout, Window owner)
    {
        ArgumentNullException.ThrowIfNull(layout);

        PromptReviewViewModel reviewModel = new(this.analysisService, this.notifier, layout);
        PromptReviewWindow review = new(reviewModel) { Owner = owner };

        if (review.ShowDialog() != true || reviewModel.Analyzer is not AnalyzerFiles analyzer)
        {
            return;
        }

        AnalysisOutcome? outcome = await this
            .RunClaudeAsync(layout, analyzer, reviewModel.Prompt, owner)
            .ConfigureAwait(true);

        if (outcome is null)
        {
            return;
        }

        // F-28 / ADR-0024 決定 2：**結果を受け取った時点で**履歴を書く。
        // 閉じるときに書くと、落ちたときに「利用枠を使って得た結果」だけが消える。
        this.SaveHistory(layout, outcome, reviewModel.Notes);

        ResultViewModel resultModel = new(
            this.analysisService,
            this.services.GetRequiredService<ISessionWorkspace>(),
            this.services.GetRequiredService<IClaudeTranscriptLocator>(),
            this.settingsStore,
            this.notifier,
            this.services.GetRequiredService<IClipboard>(),
            layout,
            analyzer,
            outcome,
            reviewModel.SourceUrl);

        ResultWindow result = new(resultModel) { Owner = owner };
        result.ShowDialog();
    }

    /// <summary>
    /// 履歴を 1 件書く（F-28 / ADR-0024）。
    ///
    /// <b>渡すのは顛末と利用者のメモだけ。</b>セッションのフォルダを渡さないのは、
    /// 渡せるようにすると、文字起こしや OCR をうっかり詰められるようになるため。
    /// どちらも 5.5 では一時データの側にある。
    /// </summary>
    private void SaveHistory(SessionLayout layout, AnalysisOutcome outcome, UserNotes notes)
    {
        try
        {
            this.history.Save(HistoryEntry.From(
                layout.Id,
                this.timeProvider.GetLocalNow(),
                outcome,
                notes));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 履歴を書けなくても結果は見せる。利用枠は既に使っている。
            this.notifier.ShowError(this.describer.Describe(exception));
        }
    }

    /// <summary>解析中の窓を出しつつ claude を走らせる。キャンセルできる（F-25）。</summary>
    private async Task<AnalysisOutcome?> RunClaudeAsync(
        SessionLayout layout,
        AnalyzerFiles analyzer,
        string prompt,
        Window owner)
    {
        using CancellationTokenSource cts = new();

        AnalysisProgressWindow progress = new() { Owner = owner };
        progress.CancelRequested += (_, _) => cts.Cancel();
        progress.Show();

        try
        {
            ClaudeSettings claude = this.settingsStore.Load().Claude;
            AnalysisRequest request = new(layout, analyzer, prompt, claude, Resume: false);

            return await this.analysisService
                .AnalyzeAsync(request, progress: null, cts.Token)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is OperationCanceledException
            or ClaudeNotFoundException
            or ClaudeExecutionException
            or AnalyzerTemplateException)
        {
            // 文言への変換はここに書かない。1 箇所に集めてある（ADR-0026）。
            this.notifier.ShowError(this.describer.Describe(exception));
            return null;
        }
        finally
        {
            progress.Finish();
        }
    }
}

/// <summary>WPF のクリップボード。</summary>
public sealed class WpfClipboard : IClipboard
{
    public void SetText(string text) => Clipboard.SetText(text);
}
