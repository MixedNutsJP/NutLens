using System;
using System.Threading;
using System.Windows;
using NutLens.App.Resources;
using NutLens.App.ViewModels;
using NutLens.Core.Distribution;

namespace NutLens.App.Services;

/// <summary>
/// 更新が見つかったときの問いかけ（D-02 / ADR-0028 決定 5・6）。
///
/// <b>撮影中・解析中は出さない。</b>
/// 撮影の最中にダイアログを前面に出すと、<b>その瞬間の画面がフレームに写り込む。</b>
/// 待機中になった最初の機会に出す。
/// </summary>
public sealed class UpdatePrompt
{
    private readonly IUpdateService updates;
    private readonly IUserNotifier notifier;

    private UpdateCheckResult? waiting;
    private CaptureViewModel? watching;
    private Window? owner;

    public UpdatePrompt(IUpdateService updates, IUserNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(notifier);

        this.updates = updates;
        this.notifier = notifier;
    }

    /// <summary>見つかった更新を提示する。撮影中なら、終わるまで待つ。</summary>
    public void Offer(UpdateCheckResult result, CaptureViewModel capture, Window owner)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(capture);

        this.owner = owner;

        if (!capture.IsCapturing)
        {
            this.Show(result);
            return;
        }

        // 撮影が終わるのを待つ。終わったら 1 回だけ出す。
        this.waiting = result;
        this.watching = capture;
        capture.CapturingChanged += this.OnCapturingChanged;
    }

    private void OnCapturingChanged(object? sender, EventArgs e)
    {
        if (this.watching is not CaptureViewModel capture || capture.IsCapturing)
        {
            return;
        }

        capture.CapturingChanged -= this.OnCapturingChanged;
        this.watching = null;

        if (this.waiting is UpdateCheckResult result)
        {
            this.waiting = null;
            this.Show(result);
        }
    }

    private void Show(UpdateCheckResult result)
    {
        UpdateChoice choice = this.Ask(result);

        if (choice == UpdateChoice.Skip)
        {
            // この版はもう聞かない（決定 4）。
            if (this.updates is VelopackUpdateService velopack && result.AvailableVersion is string version)
            {
                velopack.RememberSkipped(version);
            }

            return;
        }

        // **同意の後にだけ落とす**（決定 6）。
        bool downloaded = this.updates
            .DownloadAsync(progress: null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (!downloaded)
        {
            this.notifier.ShowError(Strings.UpdateDownloadFailed);
            return;
        }

        if (choice == UpdateChoice.RestartNow)
        {
            this.updates.ApplyAndRestart();
            return;
        }

        this.updates.ApplyOnNextStart();
    }

    /// <summary>
    /// 3 択を出す。
    /// <b>「いいえ」が既定。</b>間違って Enter を叩いただけで再起動しないようにする。
    /// </summary>
    private UpdateChoice Ask(UpdateCheckResult result)
    {
        string body = Strings.UpdateBodyFormat(
            result.AvailableVersion ?? "?",
            result.CurrentVersion ?? "?");

        // MessageBox は 3 択に「はい／いいえ／キャンセル」しか持たない。
        // 文面で何がどれかを示す（専用の窓を作るほどの画面ではない）。
        MessageBoxResult answer = MessageBox.Show(
            $"{body}\n\n"
            + $"［はい］{Strings.UpdateRestartNow}\n"
            + $"［いいえ］{Strings.UpdateOnNextStart}\n"
            + $"［キャンセル］{Strings.UpdateSkip}",
            Strings.UpdateTitle,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        return answer switch
        {
            MessageBoxResult.Yes => UpdateChoice.RestartNow,
            MessageBoxResult.No => UpdateChoice.OnNextStart,
            _ => UpdateChoice.Skip,
        };
    }
}
