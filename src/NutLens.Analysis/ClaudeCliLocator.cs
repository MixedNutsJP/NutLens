using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NutLens.Core.Analysis;
using NutLens.Core.Diagnostics;

namespace NutLens.Analysis;

/// <summary>
/// `claude` の実行ファイルを探し、`--version` で動作を確かめる（5.4 / D-05）。
///
/// 環境に依存する値を固定で書かない。初期値は自動で探し、設定で上書きできる。
/// </summary>
public sealed class ClaudeCliLocator : IClaudeCliLocator
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

    public ClaudeExecutable Locate(string? configuredPath)
    {
        List<string> searched = [];

        foreach (string candidate in EnumerateCandidates(configuredPath))
        {
            searched.Add(candidate);

            if (TryVerify(candidate, out ClaudeExecutable? executable))
            {
                return executable;
            }
        }

        throw new ClaudeNotFoundException(
            "claude コマンドが見つかりませんでした。Claude Code をインストールするか、設定で場所を指定してください。",
            searched);
    }

    /// <summary>探す順。設定 → ネイティブ版 → PATH → npm 版（概要設計 §9.1）。</summary>
    private static IEnumerable<string> EnumerateCandidates(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            yield return configuredPath;
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, ".local", "bin", "claude.exe");
            yield return Path.Combine(home, ".local", "bin", "claude");
        }

        foreach (string onPath in EnumerateFromPath())
        {
            yield return onPath;
        }

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            // npm のグローバルインストール。拡張子が .cmd なので cmd.exe 経由が要る。
            yield return Path.Combine(appData, "npm", "claude.cmd");
        }
    }

    private static IEnumerable<string> EnumerateFromPath()
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            yield break;
        }

        string[] names = ["claude.exe", "claude.cmd", "claude"];

        foreach (string directory in pathVariable.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            foreach (string name in names)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory.Trim(), name);
                }
                catch (ArgumentException)
                {
                    // PATH に不正な文字が混ざっていることがある。その項目だけ飛ばす。
                    break;
                }

                yield return candidate;
            }
        }
    }

    /// <summary>`claude --version` を実行して、本当に動くかを確かめる（5.4）。</summary>
    private static bool TryVerify(string path, out ClaudeExecutable executable)
    {
        executable = null!;

        if (!File.Exists(path))
        {
            return false;
        }

        bool requiresCmd = RequiresCmdShell(path);

        ProcessStartInfo info = new()
        {
            FileName = requiresCmd ? "cmd.exe" : path,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (requiresCmd)
        {
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(path);
        }

        info.ArgumentList.Add("--version");

        try
        {
            using Process? process = Process.Start(info);
            if (process is null)
            {
                return false;
            }

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(VersionTimeout))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            if (process.ExitCode != 0)
            {
                return false;
            }

            executable = new ClaudeExecutable(path, requiresCmd, output.Trim());
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// .NET からは .cmd / .bat を直接起動できない（UseShellExecute = false のとき）。
    /// npm でインストールした場合がこれに当たる。
    /// </summary>
    internal static bool RequiresCmdShell(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }
}
