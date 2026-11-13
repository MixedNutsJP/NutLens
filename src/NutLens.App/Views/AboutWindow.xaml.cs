using System;
using System.Diagnostics;
using System.Windows;
using NutLens.App.ViewModels;

namespace NutLens.App.Views;

/// <summary>
/// このアプリについて（D-06：ライセンスを画面から見られるようにする / D-07 / D-11）。
/// </summary>
public partial class AboutWindow : Window
{
    private readonly AboutViewModel viewModel;

    public AboutWindow(AboutViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        this.viewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = viewModel;
    }

    private void OnOpenRepositoryClick(object sender, RoutedEventArgs e)
    {
        ProcessStartInfo info = new()
        {
            FileName = this.viewModel.RepositoryUrl,
            UseShellExecute = true,
        };

        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or System.IO.FileNotFoundException)
        {
            // 既定のブラウザが無い。URL は画面に出ているので、開けなくても詰まない。
        }
    }
}
