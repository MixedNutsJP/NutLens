using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NutLens.CaptureProbe;

/// <summary>
/// 確認用の「動く対象」。
///
/// 静止した画面を撮ると重複除去が効いて 1 枚しか残らないため、
/// 「別の場面はちゃんと採用されるか」「間引きが時間的に均等か」を確かめられない。
/// 一定間隔で中身が変わる窓を自前で出して、それを撮ることで確かめる。
/// </summary>
internal sealed class AnimatedWindow : Window
{
    private static readonly Color[] Palette =
    [
        Colors.Crimson,
        Colors.DarkSlateBlue,
        Colors.SeaGreen,
        Colors.DarkOrange,
        Colors.Teal,
        Colors.MediumVioletRed,
    ];

    private readonly TextBlock label;
    private readonly DispatcherTimer timer;
    private int step;

    public AnimatedWindow()
    {
        this.Title = "NutLens CaptureProbe";
        this.Width = 640;
        this.Height = 360;
        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.ResizeMode = ResizeMode.NoResize;

        this.label = new TextBlock
        {
            FontSize = 160,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "0",
        };

        this.Content = new Border { Child = this.label };
        this.ApplyStep();

        // 1 秒ごとに別の「場面」にする。撮影間隔より速くして、重複と新規が混ざる状況を作る。
        this.timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        this.timer.Tick += (_, _) =>
        {
            this.step++;
            this.ApplyStep();
        };
        this.timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        this.timer.Stop();
        base.OnClosed(e);
    }

    private void ApplyStep()
    {
        this.label.Text = this.step.ToString(CultureInfo.InvariantCulture);

        if (this.Content is Border border)
        {
            border.Background = new SolidColorBrush(Palette[this.step % Palette.Length]);
        }
    }
}
