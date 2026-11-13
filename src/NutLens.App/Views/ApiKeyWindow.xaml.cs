using System.Windows;

namespace NutLens.App.Views;

/// <summary>
/// API キーの入力（5.6 / ADR-0020）。
///
/// 入力された文字列は <see cref="ApiKey"/> で 1 度だけ取り出し、
/// 呼び出し側が資格情報マネージャーへ渡したらそこで終わる。
/// <b>この窓は鍵を保持しない。</b>
/// </summary>
public partial class ApiKeyWindow : Window
{
    public ApiKeyWindow()
    {
        this.InitializeComponent();
        this.Loaded += (_, _) => this.KeyBox.Focus();
    }

    /// <summary>取り消された場合は null。</summary>
    public string? ApiKey { get; private set; }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string key = this.KeyBox.Password.Trim();
        if (key.Length == 0)
        {
            // 空のまま保存すると「設定した」と「していない」の区別が付かなくなる。
            return;
        }

        this.ApiKey = key;
        this.KeyBox.Clear();
        this.DialogResult = true;
    }
}
