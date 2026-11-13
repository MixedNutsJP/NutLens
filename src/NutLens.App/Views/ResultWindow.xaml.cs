using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NutLens.App.ViewModels;

namespace NutLens.App.Views;

/// <summary>解析結果（F-26 / F-27 / ADR-0013）。</summary>
public partial class ResultWindow : Window
{
    private readonly ResultViewModel viewModel;

    public ResultWindow(ResultViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        this.viewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = viewModel;

        // 候補の題名や根拠を「選択してコピーできる TextBox」にした副作用への手当て。
        // TextBox は内側に ScrollViewer を持つので、**ホイールを飲んで
        // 結果画面がスクロールしなくなる**ことがある。
        // 飲まれたぶんを親へ投げ直す。
        this.AddHandler(UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel), true);
    }

    protected override void OnClosed(EventArgs e)
    {
        // 5.5：結果画面を閉じたときに一時データを完全に消す。
        this.viewModel.OnClosed();
        base.OnClosed(e);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 読み取り専用で、自分ではスクロールしない TextBox のときだけ横取りする。
        // 再調査の入力欄（複数行・スクロールする）はそのまま動かす。
        if (e.Handled
            || e.OriginalSource is not DependencyObject source
            || FindReadOnlyTextBox(source) is not TextBox box
            || box.ExtentHeight > box.ViewportHeight)
        {
            return;
        }

        e.Handled = true;

        MouseWheelEventArgs forwarded = new(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = box,
        };

        (box.Parent as UIElement)?.RaiseEvent(forwarded);
    }

    private static TextBox? FindReadOnlyTextBox(DependencyObject source)
    {
        for (DependencyObject? node = source; node is not null; node = VisualTreeHelperParent(node))
        {
            if (node is TextBox box)
            {
                return box.IsReadOnly ? box : null;
            }
        }

        return null;
    }

    private static DependencyObject? VisualTreeHelperParent(DependencyObject node) =>
        node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(node)
            : null;
}
