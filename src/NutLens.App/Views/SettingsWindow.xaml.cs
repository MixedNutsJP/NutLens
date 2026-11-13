using System;
using System.Windows;
using NutLens.App.ViewModels;

namespace NutLens.App.Views;

/// <summary>設定画面（詳細設計 06 §6）。</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        this.viewModel = viewModel;
        this.InitializeComponent();
        this.DataContext = viewModel;
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        AboutWindow about = new(new ViewModels.AboutViewModel()) { Owner = this };
        about.ShowDialog();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        this.viewModel.SaveCommand.Execute(null);

        // 保存できなかったとき（ホットキーの書式が不正・登録に失敗）は閉じない。
        // 閉じてしまうと、直すべき入力が画面ごと消える。
        if (this.viewModel.Saved)
        {
            this.DialogResult = true;
        }
    }
}
