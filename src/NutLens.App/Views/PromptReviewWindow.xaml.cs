using System;
using System.Windows;
using NutLens.App.ViewModels;

namespace NutLens.App.Views;

/// <summary>プロンプトの確認・編集（F-22）。</summary>
public partial class PromptReviewWindow : Window
{
    public PromptReviewWindow(PromptReviewViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        this.InitializeComponent();
        this.DataContext = viewModel;
        this.ViewModel = viewModel;
    }

    public PromptReviewViewModel ViewModel { get; }

    private void OnSendClick(object sender, RoutedEventArgs e)
    {
        this.DialogResult = true;
        this.Close();
    }
}
