using System;
using NutLens.App.Mvvm;
using NutLens.App.Resources;
using NutLens.Core.Analysis;
using NutLens.Core.Sessions;

namespace NutLens.App.ViewModels;

/// <summary>
/// プロンプトの確認・編集（F-22）と補助情報の入力（F-13）。
///
/// 送る前に必ず目を通す画面。D-07 の「何が送られるか」もここに出す。
/// </summary>
public sealed class PromptReviewViewModel : ObservableObject
{
    private readonly IAnalysisService analysisService;
    private readonly IUserNotifier notifier;
    private readonly SessionLayout layout;

    private string prompt = string.Empty;
    private string? sourceUrl;
    private string? authorName;
    private string? caption;
    private string? freeText;
    private int frameCount;

    public PromptReviewViewModel(
        IAnalysisService analysisService,
        IUserNotifier notifier,
        SessionLayout layout)
    {
        ArgumentNullException.ThrowIfNull(analysisService);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(layout);

        this.analysisService = analysisService;
        this.notifier = notifier;
        this.layout = layout;

        this.RebuildCommand = new RelayCommand(this.Rebuild);
        this.Rebuild();
    }

    public RelayCommand RebuildCommand { get; }

    /// <summary>配置済みの analyzer 一式。送信時に <see cref="AnalysisRequest"/> へ渡す。</summary>
    public AnalyzerFiles? Analyzer { get; private set; }

    public string Prompt
    {
        get => this.prompt;
        set => this.SetProperty(ref this.prompt, value);
    }

    public string? SourceUrl
    {
        get => this.sourceUrl;
        set => this.SetProperty(ref this.sourceUrl, value);
    }

    public string? AuthorName
    {
        get => this.authorName;
        set => this.SetProperty(ref this.authorName, value);
    }

    public string? Caption
    {
        get => this.caption;
        set => this.SetProperty(ref this.caption, value);
    }

    public string? FreeText
    {
        get => this.freeText;
        set => this.SetProperty(ref this.freeText, value);
    }

    public string FrameCountText => Strings.FrameCountFormat(this.frameCount);

    public UserNotes Notes => new(this.SourceUrl, this.AuthorName, this.Caption, this.FreeText);

    /// <summary>メモを変えたらプロンプトを作り直す。編集済みの本文は捨てられる。</summary>
    private void Rebuild()
    {
        try
        {
            PromptPreparation preparation = this.analysisService.PreparePrompt(this.layout, this.Notes);

            this.Analyzer = preparation.Analyzer;
            this.Prompt = preparation.Prompt;
            this.frameCount = preparation.FrameCount;
            this.RaisePropertyChanged(nameof(this.FrameCountText));
        }
        catch (Core.Diagnostics.AnalyzerTemplateException)
        {
            this.notifier.ShowError(Strings.ErrorAnalyzerTemplate);
        }
    }
}
