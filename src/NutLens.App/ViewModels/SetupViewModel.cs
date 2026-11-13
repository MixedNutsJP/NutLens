using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NutLens.App.Mvvm;
using NutLens.App.Resources;
using NutLens.Core.Diagnostics;
using NutLens.Core.Distribution;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.App.ViewModels;

/// <summary>画面に出す確認 1 件。</summary>
public sealed class SetupCheckViewModel : ObservableObject
{
    private SetupCheck state;

    public SetupCheckViewModel(SetupCheck state)
    {
        this.state = state;
        this.Label = Describe(state.Kind);
    }

    public string Label { get; }

    public SetupCheckKind Kind => this.state.Kind;

    public bool Satisfied => this.state.IsOk;

    public string Detail => this.state.Detail;

    public bool HasDetail => this.state.HasDetail;

    /// <summary>
    /// 状態を示す印。
    ///
    /// <b>○ や □ を使わない。</b>ラジオボタン・チェックボックスに見え、
    /// <b>利用者が押して入力するものだと誤解される</b>（利用者からの指摘。2026-11-14）。
    /// 絵文字にして、入力できる部品ではないことを形で示す。
    ///
    /// 「まだ確かめていない」と「使えない」を分ける。
    /// claude -p は押すまで確かめないので、一緒にすると
    /// <b>押していないだけのものが「使えない」として出る。</b>
    /// </summary>
    public string Mark => this.state.State switch
    {
        SetupCheckState.Ok => "✅",
        SetupCheckState.Unavailable => "⚠️",
        _ => "❓",
    };

    public void Update(SetupCheck value)
    {
        this.state = value;
        this.RaisePropertyChanged(nameof(this.Satisfied));
        this.RaisePropertyChanged(nameof(this.Detail));
        this.RaisePropertyChanged(nameof(this.HasDetail));
        this.RaisePropertyChanged(nameof(this.Mark));
    }

    private static string Describe(SetupCheckKind kind) => kind switch
    {
        SetupCheckKind.ClaudeInstalled => Strings.SetupCheckClaudeInstalled,
        SetupCheckKind.ClaudeUsable => Strings.SetupCheckClaudeUsable,
        SetupCheckKind.WhisperModel => Strings.SetupCheckWhisperModel,
        _ => Strings.SetupCheckOcrLanguage,
    };
}

/// <summary>
/// 一覧に出すモデル 1 件（D-04 / ADR-0034）。
///
/// 説明文は <b>Core ではなくここ</b>で引く。Core は表示の言葉を持たない（ADR-0026 決定 2）。
/// </summary>
public sealed class WhisperModelViewModel
{
    public WhisperModelViewModel(WhisperModelChoice model)
    {
        ArgumentNullException.ThrowIfNull(model);

        this.Model = model;
        this.Size = Strings.ModelSizeFormat(model.ApproximateMegabytes);
        this.Description = Strings.ModelDescription(model.Name);
    }

    public WhisperModelChoice Model { get; }

    public string Name => this.Model.Name;

    public string Size { get; }

    public string Description { get; }

    public bool IsRecommended => this.Model.IsRecommended;

    public string RecommendedLabel => Strings.ModelRecommended;
}

/// <summary>
/// 初回セットアップ（D-03 / D-04 / D-07 / ADR-0029 / ADR-0030 / ADR-0034）。
///
/// <b>確認と案内だけ。</b>Claude Code の導入は自動化しない（D-03）。
/// <b>利用枠を使う確認は、押されたときだけ走らせる</b>（ADR-0029 決定 1）。
/// </summary>
public sealed class SetupViewModel : ObservableObject
{
    private readonly ISetupChecker checker;
    private readonly IModelDownloader downloader;
    private readonly ISettingsStore settingsStore;
    private readonly IAppDataPaths paths;
    private readonly IUserNotifier notifier;

    private CancellationTokenSource? downloading;
    private WhisperModelViewModel? selectedModel;
    private string? downloadStatus;
    private double downloadRatio;
    private bool busy;

    public SetupViewModel(
        ISetupChecker checker,
        IModelDownloader downloader,
        ISettingsStore settingsStore,
        IAppDataPaths paths,
        IUserNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(notifier);

        this.checker = checker;
        this.downloader = downloader;
        this.settingsStore = settingsStore;
        this.paths = paths;
        this.notifier = notifier;

        this.RecheckCommand = new RelayCommand(this.Recheck, () => !this.busy);
        this.CheckClaudeUsableCommand = new AsyncRelayCommand(this.CheckClaudeUsableAsync, () => !this.busy);
        this.DownloadModelCommand = new AsyncRelayCommand(
            this.DownloadModelAsync,
            () => !this.IsDownloading && this.SelectedModel is not null);
        this.CancelDownloadCommand = new RelayCommand(this.CancelDownload, () => this.IsDownloading);

        foreach (WhisperModelChoice model in WhisperModelChoice.All)
        {
            this.Models.Add(new WhisperModelViewModel(model));
        }

        // 既定の選択は**推奨**（ADR-0034 決定 3）。
        this.selectedModel = this.Models.FirstOrDefault(static m => m.IsRecommended) ?? this.Models[0];

        this.Recheck();
    }

    public ObservableCollection<SetupCheckViewModel> Checks { get; } = [];

    public ObservableCollection<WhisperModelViewModel> Models { get; } = [];

    public RelayCommand RecheckCommand { get; }

    public AsyncRelayCommand CheckClaudeUsableCommand { get; }

    public AsyncRelayCommand DownloadModelCommand { get; }

    public RelayCommand CancelDownloadCommand { get; }

    public WhisperModelViewModel? SelectedModel
    {
        get => this.selectedModel;
        set => this.SetProperty(ref this.selectedModel, value);
    }

    public string? DownloadStatus
    {
        get => this.downloadStatus;
        private set => this.SetProperty(ref this.downloadStatus, value);
    }

    public double DownloadRatio
    {
        get => this.downloadRatio;
        private set => this.SetProperty(ref this.downloadRatio, value);
    }

    public bool IsDownloading => this.downloading is not null;

    /// <summary>画面を閉じるときに呼ぶ。「見た」ことを覚える（ADR-0029 決定 3）。</summary>
    public void MarkNoticeSeen()
    {
        try
        {
            NutLensSettings current = this.settingsStore.Load();
            this.settingsStore.Save(current with
            {
                Setup = current.Setup with { CompletedVersion = SetupSettings.CurrentNoticeVersion },
            });
        }
        catch (Exception exception) when (exception is SettingsCorruptedException or IOException)
        {
            // 覚えられなくても次に進める。次の起動でまた出るだけ。
        }
    }

    private void Recheck()
    {
        // 利用枠を使わない確認だけ（ADR-0029 決定 1）。
        IReadOnlyList<SetupCheck> results = this.checker.CheckWithoutSpendingQuota();

        if (this.Checks.Count == 0)
        {
            foreach (SetupCheck result in results)
            {
                this.Checks.Add(new SetupCheckViewModel(result));
            }

            // claude -p の確認は自動で走らせないので、**未確認**の行として置く。
            // 「使えない」にしない。押していないだけなので。
            this.Checks.Insert(1, new SetupCheckViewModel(SetupCheck.Unknown(
                SetupCheckKind.ClaudeUsable,
                Strings.SetupClaudeUsableUnchecked)));

            return;
        }

        foreach (SetupCheck result in results)
        {
            foreach (SetupCheckViewModel row in this.Checks)
            {
                if (row.Kind == result.Kind)
                {
                    row.Update(result);
                }
            }
        }
    }

    private async Task CheckClaudeUsableAsync()
    {
        this.SetBusy(true);
        try
        {
            SetupCheck result = await this.checker
                .CheckClaudeUsableAsync(CancellationToken.None)
                .ConfigureAwait(true);

            foreach (SetupCheckViewModel row in this.Checks)
            {
                if (row.Kind == SetupCheckKind.ClaudeUsable)
                {
                    row.Update(result);
                }
            }
        }
        finally
        {
            this.SetBusy(false);
        }
    }

    private async Task DownloadModelAsync()
    {
        if (this.SelectedModel is not WhisperModelViewModel selected)
        {
            return;
        }

        WhisperModelChoice model = selected.Model;
        string destination = Path.Combine(this.paths.ModelsRoot, model.FileName);

        using CancellationTokenSource cts = new();
        this.downloading = cts;
        this.NotifyDownloadingChanged();
        this.DownloadRatio = 0;

        Progress<ModelDownloadProgress> progress = new(value =>
        {
            this.DownloadRatio = value.Ratio ?? 0;
            this.DownloadStatus = Strings.ModelProgressFormat(
                value.ReceivedBytes / 1024 / 1024,
                (value.TotalBytes ?? model.ApproximateBytes) / 1024 / 1024);
        });

        try
        {
            ModelDownloadResult result = await this.downloader
                .DownloadAsync(model, destination, progress, cts.Token)
                .ConfigureAwait(true);

            this.DownloadStatus = result.Status switch
            {
                ModelDownloadStatus.Completed => Strings.ModelDone,
                ModelDownloadStatus.Cancelled => Strings.ModelCancelled,
                ModelDownloadStatus.Corrupted => Strings.ModelCorrupted,
                _ => Strings.ModelNetworkFailed,
            };

            if (result.Succeeded)
            {
                this.PersistModelPath(destination);
                this.Recheck();
            }
        }
        finally
        {
            this.downloading = null;
            this.NotifyDownloadingChanged();
        }
    }

    private void CancelDownload() => this.downloading?.Cancel();

    /// <summary>
    /// 画面を閉じるときに呼ぶ。落としている途中なら止める。
    /// <b>途中のファイルは残す</b>ので、次回は続きから再開できる（ADR-0030 決定 3）。
    /// </summary>
    public void CancelDownloadIfRunning() => this.downloading?.Cancel();

    /// <summary>落とした先を設定に書く。既定の場所なら書かない（D-05：自動で探せる）。</summary>
    private void PersistModelPath(string destination)
    {
        try
        {
            NutLensSettings current = this.settingsStore.Load();
            string defaultPath = Path.Combine(this.paths.ModelsRoot, WhisperSettings.DefaultModelFileName);

            this.settingsStore.Save(current with
            {
                Whisper = current.Whisper with
                {
                    ModelPath = string.Equals(destination, defaultPath, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : destination,
                },
            });
        }
        catch (Exception exception) when (exception is SettingsCorruptedException or IOException)
        {
            this.notifier.ShowError(exception.Message);
        }
    }

    private void SetBusy(bool value)
    {
        this.busy = value;
        this.RecheckCommand.RaiseCanExecuteChanged();
        this.CheckClaudeUsableCommand.RaiseCanExecuteChanged();
    }

    private void NotifyDownloadingChanged()
    {
        this.RaisePropertyChanged(nameof(this.IsDownloading));
        this.DownloadModelCommand.RaiseCanExecuteChanged();
        this.CancelDownloadCommand.RaiseCanExecuteChanged();
    }
}
