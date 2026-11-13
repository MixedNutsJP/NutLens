using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using NutLens.App.Resources;
using NutLens.Core.Ui;

namespace NutLens.App.Services;

/// <summary>
/// タスクトレイの常駐アイコン（6 章 / ADR-0009 / ADR-0025）。
///
/// <b>トレイに触るのはこのクラスだけ。</b>
/// ライブラリを差し替えるときに、変更がここで収まるようにする（ADR-0009 決定 2）。
///
/// メニューは 4 つだけ。長いメニューは、目的の項目を探すことになる。
/// </summary>
public sealed class TrayIconService : ITrayIconService
{
    private readonly ImageSource idleIcon = Load("tray-idle.ico");
    private readonly ImageSource capturingIcon = Load("tray-capturing.ico");

    private readonly TaskbarIcon icon;
    private readonly MenuItem toggleItem;

    public TrayIconService()
    {
        this.toggleItem = NewItem(Strings.TrayStartCapture, (_, _) => this.ToggleCaptureRequested?.Invoke(this, EventArgs.Empty));

        ContextMenu menu = new();
        menu.Items.Add(this.toggleItem);
        menu.Items.Add(NewItem(Strings.TrayOpenWindow, (_, _) => this.OpenWindowRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(NewItem(Strings.TraySettings, (_, _) => this.OpenSettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new Separator());
        menu.Items.Add(NewItem(Strings.TrayExit, (_, _) => this.ExitRequested?.Invoke(this, EventArgs.Empty)));

        this.icon = new TaskbarIcon
        {
            ToolTipText = Strings.AppTitle,
            IconSource = this.idleIcon,
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };

        // ダブルクリックは「ウィンドウを開く」。利用者がいちばん期待する動き。
        this.icon.TrayMouseDoubleClick += (_, _) => this.OpenWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ToggleCaptureRequested;

    public event EventHandler? OpenWindowRequested;

    public event EventHandler? OpenSettingsRequested;

    public event EventHandler? ExitRequested;

    public void Show() => this.icon.ForceCreate();

    public void SetCapturing(bool capturing)
    {
        this.icon.IconSource = capturing ? this.capturingIcon : this.idleIcon;
        this.icon.ToolTipText = capturing ? Strings.TrayTooltipCapturing : Strings.AppTitle;
        this.toggleItem.Header = capturing ? Strings.TrayStopCapture : Strings.TrayStartCapture;
    }

    public void Dispose() => this.icon.Dispose();

    private static MenuItem NewItem(string header, RoutedEventHandler onClick)
    {
        MenuItem item = new() { Header = header };
        item.Click += onClick;
        return item;
    }

    private static ImageSource Load(string fileName)
    {
        // Resource としてアセンブリに埋め込んである（.csproj の Resource 項目）。
        BitmapImage image = new(new Uri($"pack://application:,,,/Resources/{fileName}", UriKind.Absolute));
        image.Freeze();
        return image;
    }
}
