using System;
using System.IO;
using System.Reflection;
using NutLens.App.Resources;

namespace NutLens.App.ViewModels;

/// <summary>
/// このアプリについて（D-06 / D-07 / D-11）。
///
/// <b>ライセンス文をコードに埋め込まない。</b>
/// リポジトリの <c>LICENSE</c> と <c>THIRD_PARTY_NOTICES.md</c> を配布物に含め、
/// その実体を読んで表示する。
/// 埋め込むと、ファイルを直したのに画面が古いまま、という食い違いが起きる。
/// </summary>
public sealed class AboutViewModel
{
    public const string LicenseFileName = "LICENSE";
    public const string ThirdPartyFileName = "THIRD_PARTY_NOTICES.md";

    public AboutViewModel()
        : this(AppContext.BaseDirectory)
    {
    }

    /// <summary>場所を明示する。テストから一時ディレクトリを渡すための口。</summary>
    public AboutViewModel(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        this.LicenseText = ReadOrExplain(Path.Combine(baseDirectory, LicenseFileName), LicenseFileName);
        this.ThirdPartyText = ReadOrExplain(Path.Combine(baseDirectory, ThirdPartyFileName), ThirdPartyFileName);

        Assembly assembly = typeof(AboutViewModel).Assembly;

        this.VersionText = Strings.AboutVersionFormat(
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0");

        this.Copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
            ?? string.Empty;

        // 配布先はビルドの設定に 1 箇所だけ置く（Directory.Build.props）。
        // コードに散らすと、リポジトリを移したときに直し漏れる。
        this.RepositoryUrl = ReadMetadata(assembly, "RepositoryUrl") ?? "https://github.com/";
    }

    public string VersionText { get; }

    public string Copyright { get; }

    public string LicenseText { get; }

    public string ThirdPartyText { get; }

    public string RepositoryUrl { get; }

    /// <summary>
    /// 読めなければ「無い」と出す。
    /// <b>空欄にしない。</b>ライセンスが表示されない状態に気づけなくなる。
    /// </summary>
    private static string ReadOrExplain(string path, string fileName)
    {
        try
        {
            return File.Exists(path)
                ? File.ReadAllText(path)
                : Strings.AboutFileMissingFormat(fileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Strings.AboutFileMissingFormat(fileName);
        }
    }

    private static string? ReadMetadata(Assembly assembly, string key)
    {
        foreach (AssemblyMetadataAttribute attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, key, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return attribute.Value;
            }
        }

        return null;
    }
}
