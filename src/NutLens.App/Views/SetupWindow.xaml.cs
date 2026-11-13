using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using NutLens.App.Resources;
using NutLens.App.ViewModels;

namespace NutLens.App.Views;

/// <summary>初回セットアップ（D-03 / D-04 / D-07 / ADR-0029）。</summary>
public partial class SetupWindow : Window
{
    /// <summary>
    /// 導入の手順は**公式のページに送る**（ADR-0029 決定 5）。
    /// 手順を画面に焼き付けると、古くなったときに古いまま案内し続ける。
    /// </summary>
    private const string ClaudeDocsUrl = "https://docs.claude.com/en/docs/claude-code/setup";

    private const string LanguageSettingsUri = "ms-settings:regionlanguage";

    private readonly SetupViewModel viewModel;
    private readonly IConfirmation confirmation;

    /// <summary>
    /// 初回（説明をまだ読んでいない）か。
    ///
    /// <b>初回だけ、中止＝アプリの終了にする。</b>
    /// D-07 は「初回セットアップに明記する」と言っているので、
    /// 説明を読まずに先へ進める道を作らない。
    /// 2 回目以降（足りないものがあって出したとき）は、閉じればそのまま使える。
    /// </summary>
    public bool IsFirstRun { get; init; }

    public SetupWindow(SetupViewModel viewModel, IConfirmation confirmation)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(confirmation);

        this.viewModel = viewModel;
        this.confirmation = confirmation;
        this.InitializeComponent();
        this.DataContext = viewModel;
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        this.viewModel.MarkNoticeSeen();
        this.DialogResult = true;
    }

    /// <summary>
    /// 中止。<b>×で閉じたときと同じ扱いにする</b>ので、ここでは閉じるだけ。
    /// 確認は <see cref="OnClosing"/> が 1 箇所で行う。
    /// </summary>
    private void OnCancelClick(object sender, RoutedEventArgs e) => this.Close();

    /// <summary>
    /// 中止できるようにする（利用者からの指摘。2026-11-14）。
    ///
    /// <b>×で閉じても「使いはじめる」と同じ扱いになっていた。</b>
    /// 説明を読まずに先へ進めてしまい、D-07 の「初回セットアップに明記する」が
    /// 押しただけで素通りできる状態だった。
    ///
    /// 中止と「使いはじめる」の道を分け、確認はここ 1 箇所に集める
    /// （×・中止ボタン・Esc のどれを通っても同じ確認が出る）。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        // 「使いはじめる」で閉じるときは何も尋ねない。
        if (this.DialogResult == true)
        {
            base.OnClosing(e);
            return;
        }

        bool downloading = this.viewModel.IsDownloading;

        if (this.IsFirstRun || downloading)
        {
            string message = downloading
                ? Strings.SetupCancelWhileDownloading
                : Strings.SetupCancelConfirm;

            if (!this.confirmation.Ask(message, Strings.ConfirmTitle))
            {
                // 画面に留まる。**誤って閉じたときに戻れるようにする。**
                e.Cancel = true;
                return;
            }
        }

        // 落としている途中なら止める。途中のファイルは残すので、次回は続きから。
        this.viewModel.CancelDownloadIfRunning();

        // DialogResult は false のまま。呼び出し側が「中止された」と判断する。
        base.OnClosing(e);
    }

    private void OnOpenClaudeDocsClick(object sender, RoutedEventArgs e) => Open(ClaudeDocsUrl);

    private void OnOpenLanguageSettingsClick(object sender, RoutedEventArgs e) => Open(LanguageSettingsUri);

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or System.IO.FileNotFoundException)
        {
            // 開けなくても画面は使える。
        }
    }
}
