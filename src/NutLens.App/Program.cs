using System;
using System.IO;
using NutLens.Core.Sessions;
using Velopack;
using Velopack.Locators;

namespace NutLens.App;

/// <summary>
/// 入口（ADR-0027 決定 4）。
///
/// <b><see cref="VelopackApp"/> を他の何よりも先に走らせる。</b>
/// これを呼ばないと、インストール・更新・アンインストールのフックが動かない。
/// しかも<b>アプリは普通に起動する</b>ので、壊れていることに気づけない。
/// 気づくのは「更新したのに新しくならない」「アンインストールしても残る」という形になる。
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ここより前に何も書かない。UI も DI も設定の読み込みも後。
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(OnBeforeUninstall)
            .Run();

        App app = new();
        app.InitializeComponent();
        app.Run();
    }

    /// <summary>
    /// アンインストールの直前に走る（D-01 / ADR-0027 決定 3）。
    ///
    /// <b>消すのは一時作業フォルダと設定だけ。</b>
    /// 履歴とモデルは残す。D-01 が「履歴を残すかどうかは利用者が選べる」と言っているので、
    /// <b>黙って消さない。</b>残したものの消し方は README に書いてある。
    ///
    /// <b>ここで UI を出さない。</b>アンインストール中に動く UI は最も壊れやすい。
    /// 失敗しても例外を投げない。投げるとアンインストールが途中で止まる。
    /// </summary>
    private static void OnBeforeUninstall(SemanticVersion version)
    {
        try
        {
            // **指定された保存先を見る**（ADR-0033 決定 6）。
            // %LOCALAPPDATA% を直接見ると、保存先を変えた利用者の work\ が消えずに残る。
            AppDataPaths paths = new(DataRoot.Resolve(BootstrapDirectory()));

            if (Directory.Exists(paths.WorkRoot))
            {
                Directory.Delete(paths.WorkRoot, recursive: true);
            }

            if (File.Exists(paths.SettingsFile))
            {
                File.Delete(paths.SettingsFile);
            }
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            // 消せなくてもアンインストールは続ける。
        }
    }

    /// <summary>
    /// 「場所の指定」（<c>data-dir.txt</c>）を探すフォルダ（ADR-0033 決定 2）。
    ///
    /// インストール済みなら Velopack の <c>RootAppDir</c>。
    /// <b><c>current\</c> の中（exe の隣）ではない。</b>
    /// あそこは更新のたびに作り直されるので、指定が消えて初期値に戻る。
    ///
    /// ポータブル版・開発中の実行では exe のあるフォルダ。
    /// </summary>
    public static string BootstrapDirectory()
    {
        try
        {
            // VelopackApp.Build().Run() が process 全体の locator を立てる。
            // Main の先頭で呼んでいるので、ここでは既にある（ADR-0027 決定 4）。
            if (VelopackLocator.Current?.RootAppDir is string root && !string.IsNullOrWhiteSpace(root))
            {
                return root;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or IOException
            or UnauthorizedAccessException)
        {
            // インストールされていない。exe の隣で続ける。
        }

        return AppContext.BaseDirectory;
    }
}
