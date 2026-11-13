using System;
using System.IO;
using NutLens.Core.Analysis;
using NutLens.Core.Sessions;

namespace NutLens.Analysis;

/// <summary>
/// Claude Code の会話履歴（.jsonl）を<b>探す</b>（ADR-0013 決定 3）。
///
/// パスを組み立てない。作業ディレクトリのエスケープ規則（<c>:</c> と <c>\</c> を <c>-</c> に置換）は
/// 観察からの推測で、Claude Code の版で変わりうる。
/// 規則を信じて組み立てると、版が変わったときに
/// <b>存在しないパスを「ここにあります」と表示する</b>ことになる。
///
/// セッション ID はこちらが決めているので（ADR-0007）、ファイル名で探せば一意に当たる。
/// </summary>
public sealed class ClaudeTranscriptLocator : IClaudeTranscriptLocator
{
    private readonly string projectsRoot;

    public ClaudeTranscriptLocator()
        : this(DefaultProjectsRoot())
    {
    }

    public ClaudeTranscriptLocator(string projectsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectsRoot);
        this.projectsRoot = projectsRoot;
    }

    /// <summary>%USERPROFILE%\.claude\projects</summary>
    public static string DefaultProjectsRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "projects");

    public string? TryFind(SessionId sessionId)
    {
        if (!Directory.Exists(this.projectsRoot))
        {
            return null;
        }

        string fileName = sessionId + ".jsonl";

        try
        {
            foreach (string directory in Directory.EnumerateDirectories(this.projectsRoot))
            {
                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (IOException)
        {
            // 読めないなら「見つからなかった」と同じに扱う。推測のパスは返さない。
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }
}
