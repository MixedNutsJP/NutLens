using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using NutLens.App.Resources;
using NutLens.App.ViewModels;
using NutLens.Capture.Graphics;

namespace NutLens.App.Views;

public partial class MainWindow : Window
{
    private readonly WindowCaptureTargetProvider windowTargets;
    private readonly CaptureViewModel viewModel;
    private readonly IConfirmation confirmation;
    private readonly Func<Window, bool> openSettings;

    /// <summary>終了のときだけ true。畳むのではなく本当に閉じる。</summary>
    private bool closingForShutdown;

    public MainWindow(
        CaptureViewModel viewModel,
        WindowCaptureTargetProvider windowTargets,
        IConfirmation confirmation,
        Func<Window, bool> openSettings)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(windowTargets);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(openSettings);

        this.viewModel = viewModel;
        this.windowTargets = windowTargets;
        this.confirmation = confirmation;
        this.openSettings = openSettings;

        this.InitializeComponent();
        this.DataContext = viewModel;
    }

    /// <summary>アプリを終了する流れで閉じる。畳まない。</summary>
    public void CloseForShutdown()
    {
        this.closingForShutdown = true;
        this.Close();
    }

    /// <summary>トレイから戻す。</summary>
    public void Restore()
    {
        this.Show();
        this.ShowInTaskbar = true;
        this.WindowState = WindowState.Normal;
        this.Activate();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // 自分自身を撮影対象の一覧から外す。
        // ハンドルはウィンドウが実際に作られてからでないと取れないので、ここで渡す。
        this.windowTargets.ExcludedWindow = new WindowInteropHelper(this).Handle;
        this.viewModel.RefreshTargets();
    }

    /// <summary>
    /// ×はトレイへ畳む（ADR-0025 決定 1）。
    ///
    /// <b>畳むときにも一時データを消す</b>（決定 2）。
    /// 利用者が「閉じた」と思った時点で消えているほうが、5.5 の趣旨に合う。
    /// ただし<b>消すものが無いときは確認を出さない</b>（決定 3）。
    /// 毎回出る確認は読まれなくなり、本当に消える回にも「はい」を押す動作だけが残る。
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (this.closingForShutdown)
        {
            base.OnClosing(e);
            return;
        }

        if (!this.ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        this.viewModel.DiscardTemporaryData();

        e.Cancel = true;
        this.Hide();
        this.ShowInTaskbar = false;

        base.OnClosing(e);
    }

    /// <summary>消すものがあるときだけ尋ねる。無ければ黙って畳む。</summary>
    private bool ConfirmDiscard()
    {
        if (this.viewModel.IsCapturing)
        {
            return this.confirmation.Ask(Strings.ConfirmCloseWhileCapturing, Strings.ConfirmTitle);
        }

        if (this.viewModel.HasTemporaryData)
        {
            return this.confirmation.Ask(Strings.ConfirmCloseWithTemporaryData, Strings.ConfirmTitle);
        }

        return true;
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) => this.openSettings(this);
}
