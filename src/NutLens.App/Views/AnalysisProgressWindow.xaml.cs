using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using NutLens.App.Resources;

namespace NutLens.App.Views;

/// <summary>解析中の表示とキャンセル（F-25）。</summary>
public partial class AnalysisProgressWindow : Window
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private readonly DispatcherTimer timer;

    public AnalysisProgressWindow()
    {
        this.InitializeComponent();

        this.timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        this.timer.Tick += (_, _) => this.UpdateElapsed();
        this.timer.Start();
        this.UpdateElapsed();
    }

    /// <summary>利用者がキャンセルを押した。</summary>
    public event EventHandler? CancelRequested;

    public void Finish()
    {
        this.timer.Stop();
        this.stopwatch.Stop();

        // 呼び出し側が閉じる前に Close を 2 回呼ばないようにする。
        if (this.IsLoaded)
        {
            this.Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        this.timer.Stop();
        base.OnClosed(e);
    }

    private void UpdateElapsed() =>
        this.ElapsedText.Text = Strings.AnalysisRunningFormat(this.stopwatch.Elapsed.TotalSeconds);

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        this.CancelRequested?.Invoke(this, EventArgs.Empty);
}
