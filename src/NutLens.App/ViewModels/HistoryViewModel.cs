using System;
using System.Collections.ObjectModel;
using System.Globalization;
using NutLens.App.Mvvm;
using NutLens.App.Resources;
using NutLens.Core.History;

namespace NutLens.App.ViewModels;

/// <summary>一覧に出す履歴 1 件（F-28：日時・第一候補・他の候補・メモ）。</summary>
public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        this.Id = entry.Id;
        this.SavedAt = entry.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        this.TopTitle = entry.Top?.DisplayTitle ?? Strings.HistoryNoCandidate;
        this.Confidence = entry.Top is HistoryCandidate top
            ? Strings.ResultConfidenceFormat(top.Confidence)
            : string.Empty;

        this.OtherTitles = string.Join(
            " / ",
            System.Linq.Enumerable.Select(
                System.Linq.Enumerable.Skip(entry.Candidates, 1),
                static candidate => candidate.DisplayTitle));

        this.Notes = BuildNotes(entry);
    }

    public string Id { get; }

    public string SavedAt { get; }

    public string TopTitle { get; }

    public string Confidence { get; }

    public string OtherTitles { get; }

    public string Notes { get; }

    public bool HasOtherTitles => this.OtherTitles.Length > 0;

    public bool HasNotes => this.Notes.Length > 0;

    private static string BuildNotes(HistoryEntry entry)
    {
        string[] parts =
        [
            entry.SourceUrl ?? string.Empty,
            entry.AuthorName ?? string.Empty,
            entry.Caption ?? string.Empty,
            entry.FreeText ?? string.Empty,
        ];

        return string.Join(" / ", System.Linq.Enumerable.Where(parts, static p => p.Length > 0));
    }
}

/// <summary>
/// 履歴の一覧と削除（F-28 / 5.5 / ADR-0024）。
///
/// 5.5 が「履歴は**設定画面から**個別・一括で消せる」と書いているので、設定画面の中に置く。
/// </summary>
public sealed class HistoryViewModel : ObservableObject
{
    private readonly IHistoryStore store;
    private readonly IConfirmation confirmation;

    private HistoryItemViewModel? selected;

    public HistoryViewModel(IHistoryStore store, IConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(confirmation);

        this.store = store;
        this.confirmation = confirmation;

        this.RefreshCommand = new RelayCommand(this.Refresh);
        this.DeleteCommand = new RelayCommand(this.Delete, () => this.Selected is not null);
        this.DeleteAllCommand = new RelayCommand(this.DeleteAll, () => this.Items.Count > 0);

        this.Refresh();
    }

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand DeleteAllCommand { get; }

    public HistoryItemViewModel? Selected
    {
        get => this.selected;
        set
        {
            if (this.SetProperty(ref this.selected, value))
            {
                this.DeleteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CountText => Strings.HistoryCountFormat(this.Items.Count);

    public bool IsEmpty => this.Items.Count == 0;

    public void Refresh()
    {
        this.Items.Clear();
        foreach (HistoryEntry entry in this.store.List())
        {
            this.Items.Add(new HistoryItemViewModel(entry));
        }

        this.Selected = null;
        this.RaisePropertyChanged(nameof(this.CountText));
        this.RaisePropertyChanged(nameof(this.IsEmpty));
        this.DeleteAllCommand.RaiseCanExecuteChanged();
    }

    private void Delete()
    {
        if (this.Selected is not HistoryItemViewModel item)
        {
            return;
        }

        // 1 件の削除は確認しない。選んで押したという意思表示がはっきりしている。
        this.store.Delete(item.Id);
        this.Refresh();
    }

    private void DeleteAll()
    {
        // 一括は確認する。**元に戻せない**ので、押し間違いの影響が全件に及ぶ。
        if (this.confirmation.Ask(Strings.HistoryConfirmDeleteAll, Strings.ConfirmTitle))
        {
            this.store.DeleteAll();
            this.Refresh();
        }
    }
}
