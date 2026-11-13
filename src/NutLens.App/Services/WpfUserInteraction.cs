using System;
using System.Collections.Generic;
using System.Windows;
using NutLens.App.Resources;
using NutLens.App.ViewModels;
using NutLens.App.Views;
using NutLens.Capture.Graphics;
using NutLens.Core.Capture;

namespace NutLens.App.Services;

/// <summary>MessageBox による通知。</summary>
public sealed class MessageBoxNotifier : IUserNotifier
{
    public void ShowError(string message) =>
        MessageBox.Show(message, Strings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInformation(string message) =>
        MessageBox.Show(message, Strings.AppTitle, MessageBoxButton.OK, MessageBoxImage.Information);
}

/// <summary>
/// はい／いいえの確認。
/// <b>既定は「いいえ」。</b>消える側・外へ出る側に倒れないようにする。
/// </summary>
public sealed class MessageBoxConfirmation : IConfirmation
{
    public bool Ask(string message, string title) =>
        MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public bool? AskThreeWay(string message, string title) => MessageBox.Show(
        message,
        title,
        MessageBoxButton.YesNoCancel,
        MessageBoxImage.Question,
        MessageBoxResult.Cancel) switch
    {
        MessageBoxResult.Yes => true,
        MessageBoxResult.No => false,
        _ => null,
    };
}

/// <summary>フォルダ選択。.NET 8 以降の WPF にある OpenFolderDialog を使う。</summary>
public sealed class WpfFolderPicker : IFolderPicker
{
    public string? Pick(string title, string? initialDirectory)
    {
        Microsoft.Win32.OpenFolderDialog dialog = new()
        {
            Title = title,
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory)
            && System.IO.Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}

/// <summary>
/// Web Detection の同意と API キーの入力（5.6 / ADR-0021）。
///
/// <b>既定を「いいえ」にする。</b>間違って Enter を叩いただけで
/// 外部送信が有効になることを避ける（既定はオフという要件の趣旨）。
/// </summary>
public sealed class WpfWebDetectionPrompt : IWebDetectionPrompt
{
    public bool AskConsent(int maxImages) =>
        MessageBox.Show(
            Strings.WebDetectionConsentBodyFormat(maxImages),
            Strings.WebDetectionConsentTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public string? AskApiKey()
    {
        ApiKeyWindow window = new() { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true ? window.ApiKey : null;
    }
}

/// <summary>
/// 全画面オーバーレイで矩形を選ばせ、「モニタ＋モニタ相対の矩形」に直す。
///
/// WGC はモニタ単位でしか取れないので、仮想デスクトップ座標のままでは使えない。
/// 矩形の左上がどのモニタに乗っているかを判定し、そのモニタ内の相対座標へ直す（ADR-0003 決定 2）。
/// </summary>
public sealed class OverlayRegionSelector : IRegionSelector
{
    private readonly MonitorCaptureTargetProvider monitors;

    public OverlayRegionSelector(MonitorCaptureTargetProvider monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        this.monitors = monitors;
    }

    public CaptureTarget? SelectRegion()
    {
        RegionOverlayWindow overlay = new();
        overlay.ShowDialog();

        if (overlay.SelectedRegion is not PixelRect absolute)
        {
            return null;
        }

        foreach (CaptureTarget monitor in this.monitors.GetTargets())
        {
            if (!MonitorCaptureTargetProvider.TryGetBounds(monitor.Handle, out PixelRect bounds))
            {
                continue;
            }

            if (!Contains(bounds, absolute.X, absolute.Y))
            {
                continue;
            }

            PixelRect relative = new(
                absolute.X - bounds.X,
                absolute.Y - bounds.Y,
                absolute.Width,
                absolute.Height);

            return monitor with { CropRegion = relative.ClampTo(bounds.Width, bounds.Height) };
        }

        return null;
    }

    private static bool Contains(PixelRect bounds, int x, int y) =>
        x >= bounds.X
        && y >= bounds.Y
        && x < bounds.X + bounds.Width
        && y < bounds.Y + bounds.Height;
}
