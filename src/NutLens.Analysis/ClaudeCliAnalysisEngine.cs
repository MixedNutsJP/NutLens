using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Analysis;
using NutLens.Core.Diagnostics;

namespace NutLens.Analysis;

/// <summary>
/// `claude -p` を起動して結果を受け取る（F-24 / F-25 / ADR-0006 / ADR-0011）。
///
/// <b>このクラスと <see cref="ClaudeCliLocator"/> だけが Process.Start を呼ぶ。</b>
/// 外部へ出ていく経路を 1 箇所に集めることで、後から点検できる状態を保つ。
/// </summary>
public sealed class ClaudeCliAnalysisEngine : IAnalysisEngine
{
    /// <summary>許可するツール。書き込みとシェルは入れない（5.4 / ADR-0006）。</summary>
    internal const string AllowedTools = "Read,WebSearch,WebFetch";

    private readonly IClaudeCliLocator locator;

    public ClaudeCliAnalysisEngine(IClaudeCliLocator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        this.locator = locator;
    }

    /// <summary>
    /// 同じ UUID で 2 回目の <c>--session-id</c> を渡すと claude が拒否するときの文言（実測）。
    /// 版が変われば文言も変わりうるので、照合は緩くしている。
    /// </summary>
    private const string SessionInUseMarker = "already in use";

    public async Task<ClaudeRunOutput> RunAsync(
        ClaudeInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        ClaudeRunOutput output = await this.RunOnceAsync(invocation, cancellationToken).ConfigureAwait(false);

        if (ShouldRetryAsResume(invocation.Resume, output))
        {
            AppendLog(invocation.LogFilePath, "--- セッションが使用中のため --resume で再実行します ---");
            return await this
                .RunOnceAsync(invocation with { Resume = true }, cancellationToken)
                .ConfigureAwait(false);
        }

        return output;
    }

    /// <summary>
    /// 撮影 1 回に対して解析を 2 回始める経路がある（解析を取り消して始め直す、など）。
    /// そのとき <c>--session-id</c> は「既に使われている」と拒否されるので、続きとして扱う。
    /// <b>ここで受け止めないと、取り消した直後にもう一度押しただけで解析できなくなる。</b>
    /// </summary>
    internal static bool ShouldRetryAsResume(bool wasResume, ClaudeRunOutput output) =>
        !wasResume
        && output.ExitCode != 0
        && output.StandardError.Contains(SessionInUseMarker, StringComparison.OrdinalIgnoreCase);

    private async Task<ClaudeRunOutput> RunOnceAsync(
        ClaudeInvocation invocation,
        CancellationToken cancellationToken)
    {
        ClaudeExecutable executable = this.locator.Locate(invocation.ExecutablePath);
        ProcessStartInfo info = BuildStartInfo(executable, invocation);

        WriteLogHeader(invocation, executable);

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Process process = new() { StartInfo = info };

        StringBuilder standardOutput = new();
        StringBuilder standardError = new();

        // 出力は非同期で読む。WaitForExit の前に読み始めないと、
        // 出力が多いときにパイプが詰まってプロセスが終わらない。
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardOutput.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                standardError.AppendLine(args.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new ClaudeExecutionException("claude の起動に失敗しました。");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ClaudeExecutionException(
                $"claude を起動できませんでした: {executable.Path}",
                exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WritePromptAsync(process, invocation.Prompt, cancellationToken).ConfigureAwait(false);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // F-25：キャンセルはプロセスを終了させる。
            // 子プロセス（node など）が残らないよう、ツリーごと止める。
            TryKill(process);
            AppendLog(invocation.LogFilePath, "--- 利用者がキャンセルしました ---");
            throw;
        }

        stopwatch.Stop();

        ClaudeRunOutput output = new(
            process.ExitCode,
            standardOutput.ToString(),
            standardError.ToString(),
            stopwatch.Elapsed);

        WriteLogBody(invocation.LogFilePath, output);
        return output;
    }

    /// <summary>
    /// プロンプトを標準入力へ流し込む（ADR-0038）。
    ///
    /// <para>
    /// <b>プロンプトを引数に載せない。</b>中身は OCR と文字起こし、つまり
    /// <b>画面に映っていた任意の文字列</b>で、こちらが決められない。
    /// npm 版の <c>claude.cmd</c> は <c>cmd.exe</c> 経由で起動するが、
    /// <c>cmd.exe</c> は .NET が付ける <c>\"</c> をエスケープと解釈しないため、
    /// <b>引用符を含む文字列が引用の外へ抜け、後続の <c>&amp;</c> が実行される</b>
    /// （CVE-2024-24576 と同じ形）。あわせて 8191 文字の上限も外れる。
    /// </para>
    /// </summary>
    private static async Task WritePromptAsync(
        Process process,
        string prompt,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // 相手が先に落ちた場合。終了コードと標準エラーで判断するので、ここでは握る。
        }
        finally
        {
            // **閉じるまで相手は入力の続きを待ち続ける。**
            // --max-turns でも --max-budget-usd でも止まらない（まだ 1 文字も処理していない）。
            process.StandardInput.Close();
        }
    }

    /// <summary>起動の引数を組み立てる（ADR-0006 決定 4 / ADR-0011 決定 3 / ADR-0038）。</summary>
    internal static ProcessStartInfo BuildStartInfo(
        ClaudeExecutable executable,
        ClaudeInvocation invocation)
    {
        ProcessStartInfo info = new()
        {
            FileName = executable.RequiresCmdShell ? "cmd.exe" : executable.Path,
            WorkingDirectory = invocation.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,

            // プロンプトはここから渡す。引数には載せない（ADR-0038）。
            RedirectStandardInput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,

            // **既定は現在のコードページ。** 指定しないと日本語のプロンプトが化ける。
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        if (executable.RequiresCmdShell)
        {
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(executable.Path);
        }

        // 文字列連結しない。プロンプトに " が入った瞬間に壊れる。
        foreach (string argument in BuildArguments(invocation))
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    internal static IReadOnlyList<string> BuildArguments(ClaudeInvocation invocation)
    {
        List<string> arguments =
        [
            "-p",
            "--output-format", "json",

            // --json-schema はインライン JSON のみ。ファイルパスは受け付けない（実測）。
            "--json-schema", invocation.SchemaJson,

            // ツール集合そのものを絞る（--allowedTools は許可リストで、ツール自体は残る）。
            "--tools", AllowedTools,
            "--allowedTools", AllowedTools,

            // 問い合わせが必要な操作は、誰にも聞かずに拒否する。
            "--permission-prompts", "none",

            "--max-turns", invocation.MaxTurns.ToString(CultureInfo.InvariantCulture),
        ];

        // 初回はこちらで ID を発番し、再調査は同じ ID を続ける（ADR-0007）。
        arguments.Add(invocation.Resume ? "--resume" : "--session-id");
        arguments.Add(invocation.SessionId.ToString());

        // **プロンプトはここに積まない。** 標準入力から渡す（ADR-0038）。
        // 引数の最後にプロンプトを置くと、npm 版（cmd.exe 経由）で
        // OCR が拾った文字列がシェルに解釈される。
        return arguments;
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
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // 既に終わっていた。何もしない。
        }
    }

    /// <summary>
    /// 起動の条件をログの先頭に書く（5.4）。
    /// <b>プロンプト本文とスキーマは長さだけを書く。</b>全文を入れるとログが読めなくなる。
    /// </summary>
    private static void WriteLogHeader(ClaudeInvocation invocation, ClaudeExecutable executable)
    {
        StringBuilder builder = new();
        builder.AppendLine("=== NutLens / claude -p ===");
        builder.AppendLine(CultureInfo.InvariantCulture, $"実行ファイル : {executable.Path}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"版           : {executable.Version}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"cmd 経由     : {executable.RequiresCmdShell}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"作業ディレクトリ : {invocation.WorkingDirectory}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"セッション ID : {invocation.SessionId}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"再調査       : {invocation.Resume}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"許可ツール   : {AllowedTools}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"最大ターン   : {invocation.MaxTurns}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"プロンプト長 : {invocation.Prompt.Length} 文字");
        builder.AppendLine(CultureInfo.InvariantCulture, $"スキーマ長   : {invocation.SchemaJson.Length} 文字");
        builder.AppendLine();

        TryWrite(invocation.LogFilePath, builder.ToString(), append: false);
    }

    private static void WriteLogBody(string path, ClaudeRunOutput output)
    {
        StringBuilder builder = new();
        builder.AppendLine(CultureInfo.InvariantCulture, $"--- 終了コード: {output.ExitCode} / 所要 {output.Duration.TotalSeconds:0.0} 秒 ---");
        builder.AppendLine("--- 標準出力 ---");
        builder.AppendLine(output.StandardOutput);
        builder.AppendLine("--- 標準エラー ---");
        builder.AppendLine(output.StandardError);

        TryWrite(path, builder.ToString(), append: true);
    }

    private static void AppendLog(string path, string message) =>
        TryWrite(path, message + Environment.NewLine, append: true);

    /// <summary>
    /// UTF-8（BOM つき）。
    /// BOM が無いと、Windows のメモ帳などが既定のコードページで開いて日本語が化ける。
    /// ログは利用者が開いて読むものなので、読めることを優先する。
    /// </summary>
    private static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private static void TryWrite(string path, string content, bool append)
    {
        try
        {
            if (append)
            {
                File.AppendAllText(path, content, LogEncoding);
            }
            else
            {
                File.WriteAllText(path, content, LogEncoding);
            }
        }
        catch (IOException)
        {
            // ログが書けなくても解析は続ける。ログのために解析を止めない。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
