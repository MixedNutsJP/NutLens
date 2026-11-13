using System;
using System.Collections.Generic;
using System.IO;
using NutLens.Core.Analysis;
using NutLens.Core.Diagnostics;
using NutLens.Core.Sessions;

namespace NutLens.Analysis;

/// <summary>
/// analyzer/ をセッションフォルダへ複製する（F-23 / ADR-0014）。
///
/// リンクではなくコピー。数十 KB しかないので費用はなく、
/// リンクだと一時データの削除で原本まで消える事故がありうる。
/// </summary>
public sealed class AnalyzerTemplateDeployer : IAnalyzerTemplateDeployer
{
    public const string DirectoryName = "analyzer";
    public const string ClaudeMarkdownName = "CLAUDE.md";
    public const string PromptTemplateName = "prompt-template.md";
    public const string SchemaRelativePath = "schema/result.schema.json";
    public const string SkillRelativePath = ".claude/skills/identify-movie/SKILL.md";

    private readonly IAppDataPaths paths;
    private readonly string installedRoot;

    /// <param name="installedRoot">同梱の analyzer/ がある場所（通常はアプリの実行ファイルの隣）。</param>
    public AnalyzerTemplateDeployer(IAppDataPaths paths, string installedRoot)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(installedRoot);

        this.paths = paths;
        this.installedRoot = installedRoot;
    }

    /// <summary>
    /// 使う原本。利用者の編集用（%LOCALAPPDATA%）があればそちら、無ければ同梱版。
    /// <b>同梱版は自動更新で上書きされる</b>ので、利用者には編集用を使わせる（ADR-0014 決定 2）。
    /// </summary>
    public string ResolveSourceDirectory()
    {
        string userEditable = Path.Combine(this.paths.Root, DirectoryName);
        if (Directory.Exists(userEditable) && File.Exists(Path.Combine(userEditable, ClaudeMarkdownName)))
        {
            return userEditable;
        }

        return Path.Combine(this.installedRoot, DirectoryName);
    }

    public AnalyzerFiles Deploy(SessionLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        string source = this.ResolveSourceDirectory();
        if (!Directory.Exists(source))
        {
            throw new AnalyzerTemplateException(
                $"解析用のテンプレート（analyzer）が見つかりませんでした: {source}");
        }

        VerifyRequiredFiles(source);

        try
        {
            CopyDirectory(source, layout.Root);
        }
        catch (IOException exception)
        {
            throw new AnalyzerTemplateException(
                $"解析用のテンプレートをセッションフォルダへ複製できませんでした: {layout.Root}",
                exception);
        }

        return new AnalyzerFiles(
            Path.Combine(layout.Root, ClaudeMarkdownName),
            Path.Combine(layout.Root, PromptTemplateName),
            Path.Combine(layout.Root, ToNativePath(SchemaRelativePath)),
            SchemaRelativePath,
            Path.Combine(layout.Root, ToNativePath(SkillRelativePath)));
    }

    private static void VerifyRequiredFiles(string source)
    {
        List<string> required =
        [
            ClaudeMarkdownName,
            PromptTemplateName,
            ToNativePath(SchemaRelativePath),
            ToNativePath(SkillRelativePath),
        ];

        foreach (string relative in required)
        {
            string path = Path.Combine(source, relative);
            if (!File.Exists(path))
            {
                throw new AnalyzerTemplateException(
                    $"解析用のテンプレートに必要なファイルがありません: {path}");
            }
        }
    }

    /// <summary>既にあっても上書きする。原本が変われば次の解析から反映される（ADR-0014 決定 3）。</summary>
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static string ToNativePath(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar);
}
