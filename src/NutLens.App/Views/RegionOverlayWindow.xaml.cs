using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using NutLens.Core.Capture;

namespace NutLens.App.Views;

/// <summary>
/// 全画面を覆う半透明の窓。ドラッグで矩形を選ばせる（詳細設計 §11.2）。
///
/// 座標は WPF の論理単位（96dpi 基準）で来るので、
/// そのまま使うと高 DPI 環境で実際の画素数とずれる。
/// 最後に <see cref="PresentationSource"/> の変換行列で物理画素に直している。
/// </summary>
public partial class RegionOverlayWindow : Window
{
    private Point origin;
    private bool dragging;

    public RegionOverlayWindow()
    {
        this.InitializeComponent();

        // 全モニタを覆う（仮想デスクトップ全体）。
        this.Left = SystemParameters.VirtualScreenLeft;
        this.Top = SystemParameters.VirtualScreenTop;
        this.Width = SystemParameters.VirtualScreenWidth;
        this.Height = SystemParameters.VirtualScreenHeight;
    }

    /// <summary>選ばれた矩形（仮想デスクトップ座標・物理画素）。取り消されたら null。</summary>
    public PixelRect? SelectedRegion { get; private set; }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        this.origin = e.GetPosition(this.SelectionCanvas);
        this.dragging = true;
        this.SelectionRectangle.Visibility = Visibility.Visible;
        this.UpdateRectangle(this.origin);
        this.CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (this.dragging)
        {
            this.UpdateRectangle(e.GetPosition(this.SelectionCanvas));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!this.dragging)
        {
            return;
        }

        this.dragging = false;
        this.ReleaseMouseCapture();

        Point end = e.GetPosition(this.SelectionCanvas);
        this.SelectedRegion = this.ToPixelRect(this.origin, end);
        this.DialogResult = this.SelectedRegion is not null;
        this.Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            this.SelectedRegion = null;
            this.DialogResult = false;
            this.Close();
        }
    }

    private void UpdateRectangle(Point current)
    {
        double x = Math.Min(this.origin.X, current.X);
        double y = Math.Min(this.origin.Y, current.Y);

        Rectangle rectangle = this.SelectionRectangle;
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        rectangle.Width = Math.Abs(current.X - this.origin.X);
        rectangle.Height = Math.Abs(current.Y - this.origin.Y);
    }

    private PixelRect? ToPixelRect(Point start, Point end)
    {
        double scaleX = 1.0;
        double scaleY = 1.0;

        if (PresentationSource.FromVisual(this) is HwndSource source
            && source.CompositionTarget is not null)
        {
            scaleX = source.CompositionTarget.TransformToDevice.M11;
            scaleY = source.CompositionTarget.TransformToDevice.M22;
        }

        int x = (int)Math.Round((Math.Min(start.X, end.X) + this.Left) * scaleX);
        int y = (int)Math.Round((Math.Min(start.Y, end.Y) + this.Top) * scaleY);
        int width = (int)Math.Round(Math.Abs(end.X - start.X) * scaleX);
        int height = (int)Math.Round(Math.Abs(end.Y - start.Y) * scaleY);

        // 誤クリックで 1 × 1 の矩形ができるのを防ぐ。
        if (width < 8 || height < 8)
        {
            return null;
        }

        return new PixelRect(x, y, width, height);
    }
}
