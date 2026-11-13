using System;

namespace NutLens.Core.Sessions;

/// <summary>
/// 1 回の撮影を表す識別子。
///
/// この値は Claude Code の <c>--session-id</c> にそのまま渡す（ADR-0007）。
/// 作業フォルダ名・ログの相関キー・Claude の会話 ID が 1 つの値で揃うので、
/// 不具合報告でこの ID だけを聞けば、フォルダとログと会話を同時にたどれる。
/// </summary>
public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.NewGuid());

    /// <summary>ハイフン区切り。フォルダ名と --session-id の両方でこの形を使う。</summary>
    public override string ToString() => this.Value.ToString("D");

    public static bool TryParse(string? text, out SessionId id)
    {
        if (Guid.TryParseExact(text, "D", out Guid guid))
        {
            id = new SessionId(guid);
            return true;
        }

        id = default;
        return false;
    }

    public static SessionId Parse(string text)
    {
        if (!TryParse(text, out SessionId id))
        {
            throw new FormatException($"セッション ID の形式が不正です: {text}");
        }

        return id;
    }
}

/// <summary>アプリが使うフォルダの場所。環境依存の値を固定で書かないための窓口（D-05）。</summary>
public interface IAppDataPaths
{
    /// <summary>%LOCALAPPDATA%\NutLens</summary>
    string Root { get; }

    /// <summary>一時作業フォルダの親。この下は丸ごと消してよい（5.5）。</summary>
    string WorkRoot { get; }

    /// <summary>履歴の置き場。結果のテキストだけを置く（F-28：サムネイルは持たない）。</summary>
    string HistoryRoot { get; }

    /// <summary>settings.json のフルパス。</summary>
    string SettingsFile { get; }

    /// <summary>Whisper のモデルの置き場。同梱せず初回セットアップで入れる（D-04）。</summary>
    string ModelsRoot { get; }
}

/// <inheritdoc />
public sealed class AppDataPaths : IAppDataPaths
{
    public const string ApplicationDirectoryName = "NutLens";
    public const string SettingsFileName = "settings.json";

    /// <summary>既定の場所（%LOCALAPPDATA%\NutLens）を使う。</summary>
    public AppDataPaths()
        : this(DefaultRoot())
    {
    }

    /// <summary>場所を明示する。テストで一時ディレクトリを渡すために使う。</summary>
    public AppDataPaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        this.Root = root;
        this.WorkRoot = System.IO.Path.Combine(root, "work");
        this.HistoryRoot = System.IO.Path.Combine(root, "history");
        this.SettingsFile = System.IO.Path.Combine(root, SettingsFileName);
        this.ModelsRoot = System.IO.Path.Combine(root, Settings.WhisperSettings.ModelsDirectoryName);
    }

    public string Root { get; }

    public string WorkRoot { get; }

    public string HistoryRoot { get; }

    public string SettingsFile { get; }

    public string ModelsRoot { get; }

    private static string DefaultRoot() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationDirectoryName);
}
