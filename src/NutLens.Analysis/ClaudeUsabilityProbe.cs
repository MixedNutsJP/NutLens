using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Analysis;
using NutLens.Core.Diagnostics;
using NutLens.Core.Distribution;

namespace NutLens.Analysis;

/// <summary>
/// <c>claude -p</c> が動くかを 1 回だけ確かめる（D-03 の (2) / ADR-0029 決定 1）。
///
/// <b>これを呼ぶと利用者の利用枠を使う。</b>
/// 起動のたびに自動で走らせない。**押されたときだけ**呼ぶこと。
///
/// 解析の本番（<see cref="ClaudeCliAnalysisEngine"/>）と分けてあるのは、
/// 本番の起動行に確認用の分岐を入れないため。
/// </summary>
public sealed class ClaudeUsabilityProbe : IClaudeProbe
{
    /// <summary>
    /// 投げるプロンプト。<b>消費を最小にするために、できるだけ短く・ツールを使わせない。</b>
    /// 返事の中身は見ない。<b>返ってくること自体</b>が、ログイン済みで動く証拠になる。
    /// </summary>
    internal const string Prompt = "ok";

    /// <summary>応答までの上限。ログインしていない場合は待たされることがある。</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private readonly IClaudeCliLocator locator;

    public ClaudeUsabilityProbe(IClaudeCliLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        this.locator = locator;
    }

    public async Task<ClaudeProbeResult> ProbeAsync(
        string? configuredPath,
        CancellationToken cancellationToken)
    {
        ClaudeExecutable executable;
        try
        {
            executable = this.locator.Locate(configuredPath);
        }
        catch (ClaudeNotFoundException)
        {
            return new ClaudeProbeResult(false, "claude コマンドが見つかりません。");
        }

        using CancellationTokenSource timeout = new(Timeout);
        using CancellationTokenSource linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            return await RunAsync(executable, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new ClaudeProbeResult(false, "応答がありませんでした（90 秒で打ち切りました）。");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or System.IO.IOException)
        {
            return new ClaudeProbeResult(false, exception.Message);
        }
    }

    internal static ProcessStartInfo BuildStartInfo(ClaudeExecutable executable)
    {
        ProcessStartInfo info = new()
        {
            FileName = executable.RequiresCmdShell ? "cmd.exe" : executable.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (executable.RequiresCmdShell)
        {
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(executable.Path);
        }

        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(Prompt);

        // 解析と同じく、ツールを 3 重に絞る（ADR-0006）。
        // 確認のつもりの実行が、書き込みやシェルを動かせる状態にならないようにする。
        info.ArgumentList.Add("--tools");
        info.ArgumentList.Add(string.Empty);
        info.ArgumentList.Add("--permission-prompts");
        info.ArgumentList.Add("none");
        info.ArgumentList.Add("--max-turns");
        info.ArgumentList.Add("1");

        return info;
    }

    private static async Task<ClaudeProbeResult> RunAsync(
        ClaudeExecutable executable,
        CancellationToken cancellationToken)
    {
        using Process process = new() { StartInfo = BuildStartInfo(executable) };
        process.Start();

        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        string error = await standardError.ConfigureAwait(false);
        await standardOutput.ConfigureAwait(false);

        if (process.ExitCode == 0)
        {
            return new ClaudeProbeResult(true, "claude -p が動きました。");
        }

        // 標準エラーをそのまま出さない。長いことがあるので先頭だけ。
        string detail = error.Length > 200 ? error[..200] + "…" : error.Trim();
        return new ClaudeProbeResult(
            false,
            string.IsNullOrWhiteSpace(detail)
                ? $"claude が終了コード {process.ExitCode} で終わりました。"
                : detail);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
        }
    }
}
