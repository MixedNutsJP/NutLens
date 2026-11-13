using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NutLens.Core.Sessions;

/// <summary>データを移した顛末（ADR-0033 決定 3）。</summary>
public enum DataRootMoveStatus
{
    Moved,

    /// <summary>移さずに切り替えた（利用者の選択）。</summary>
    SwitchedWithoutMoving,

    /// <summary>新しい場所に書けない。<b>何も変えていない。</b></summary>
    DestinationNotWritable,

    /// <summary>コピーに失敗した。<b>何も変えていない。</b></summary>
    CopyFailed,

    /// <summary>
    /// 切り替えはできたが、元の場所を消せなかった。
    /// <b>実害は無い</b>（新しい方が使われる）が、場所を利用者に伝える。
    /// </summary>
    MovedButSourceRemains,
}

public sealed record DataRootMoveResult(DataRootMoveStatus Status, string? Detail)
{
    /// <summary>切り替わったか。消し残しがあっても切り替わってはいる。</summary>
    public bool Switched => this.Status is DataRootMoveStatus.Moved
        or DataRootMoveStatus.SwitchedWithoutMoving
        or DataRootMoveStatus.MovedButSourceRemains;
}

/// <summary>
/// データの保存先を決める（D-05 / ADR-0033）。
///
/// <b>解決は 2 段だけ。</b>
/// 1. 「場所の指定」ファイルがあれば、そこ
/// 2. 無ければ <c>%LOCALAPPDATA%\NutLens</c>（5.5 / 6 章の初期値）
///
/// 3 段にすると、**いまどこを見ているかが分からなくなる**。
/// </summary>
public static class DataRoot
{
    /// <summary>
    /// 「場所の指定」ファイルの名前。
    ///
    /// <b>置き場は <c>current\</c> の外</b>（ADR-0033 決定 2）。
    /// Velopack は更新のたびに <c>current\</c> を作り直すので、
    /// exe の隣に置くと<b>更新で指定が消え、初期値に戻る</b>。
    /// 利用者には「更新したら設定と履歴が消えた」ように見える。
    /// </summary>
    public const string PointerFileName = "data-dir.txt";

    /// <summary>利用者が開いて読むことがあるので UTF-8（BOM つき）。</summary>
    private static readonly Encoding TextEncoding =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>初期値。5.5 と 6 章が名指ししている場所。</summary>
    public static string Default => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDataPaths.ApplicationDirectoryName);

    /// <param name="bootstrapDirectory">
    /// 「場所の指定」を探すフォルダ。インストール済みなら Velopack の
    /// <c>RootAppDir</c>、そうでなければ exe のあるフォルダ。
    /// </param>
    public static string Resolve(string? bootstrapDirectory) =>
        ReadPointer(bootstrapDirectory) ?? Default;

    /// <summary>指定を読む。無い・空・読めないなら null（＝初期値を使う）。</summary>
    public static string? ReadPointer(string? bootstrapDirectory)
    {
        if (string.IsNullOrWhiteSpace(bootstrapDirectory))
        {
            return null;
        }

        string path = Path.Combine(bootstrapDirectory, PointerFileName);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string value = File.ReadAllText(path).Trim();

            // 空の指定を「空文字のパス」として扱わない。初期値に落とす。
            if (value.Length == 0)
            {
                return null;
            }

            // 相対パスを受け取らない。どこを基準にするかが決まらない。
            return Path.IsPathFullyQualified(value) ? value : null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            // 読めないだけで起動を止めない。初期値で続ける。
            return null;
        }
    }

    /// <summary><paramref name="dataRoot"/> が null なら指定を消す（初期値に戻す）。</summary>
    public static void WritePointer(string bootstrapDirectory, string? dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapDirectory);

        string path = Path.Combine(bootstrapDirectory, PointerFileName);

        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        Directory.CreateDirectory(bootstrapDirectory);
        File.WriteAllText(path, dataRoot, TextEncoding);
    }

    /// <summary>
    /// 保存先を切り替える（ADR-0033 決定 3）。
    ///
    /// <b>順序を間違えると、戻せない形で壊れる。</b>
    /// コピー → 照合 → 指定を書く → 元を消す。
    /// 先に消すと、指定を書く直前で落ちたときに
    /// <b>データは移ったのに指定は古いまま</b>になる。
    /// </summary>
    public static DataRootMoveResult Switch(
        string bootstrapDirectory,
        string currentRoot,
        string newRoot,
        bool moveExistingData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(newRoot);

        if (!TryPrepare(newRoot, out string? reason))
        {
            return new DataRootMoveResult(DataRootMoveStatus.DestinationNotWritable, reason);
        }

        bool same = string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentRoot)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(newRoot)),
            StringComparison.OrdinalIgnoreCase);

        if (same)
        {
            return new DataRootMoveResult(DataRootMoveStatus.SwitchedWithoutMoving, null);
        }

        if (!moveExistingData || !Directory.Exists(currentRoot))
        {
            WritePointer(bootstrapDirectory, newRoot);
            return new DataRootMoveResult(DataRootMoveStatus.SwitchedWithoutMoving, null);
        }

        try
        {
            CopyDirectory(currentRoot, newRoot);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            // **何も変えていない。** 指定はまだ書いていない。
            return new DataRootMoveResult(DataRootMoveStatus.CopyFailed, exception.Message);
        }

        if (!Matches(currentRoot, newRoot, out string? mismatch))
        {
            return new DataRootMoveResult(DataRootMoveStatus.CopyFailed, mismatch);
        }

        // ★ ここで切り替わる。これ以降に落ちても、新しい方が使われる。
        WritePointer(bootstrapDirectory, newRoot);

        try
        {
            Directory.Delete(currentRoot, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new DataRootMoveResult(DataRootMoveStatus.MovedButSourceRemains, currentRoot);
        }

        return new DataRootMoveResult(DataRootMoveStatus.Moved, null);
    }

    /// <summary>書けるかを実際に試す。<b>権限の判定を推測で行わない。</b></summary>
    internal static bool TryPrepare(string root, out string? reason)
    {
        reason = null;

        try
        {
            Directory.CreateDirectory(root);

            string probe = Path.Combine(root, $".nutlens-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            reason = exception.Message;
            return false;
        }
    }

    /// <summary>ファイル数と合計の大きさで照合する。1 つずつのハッシュまでは見ない。</summary>
    internal static bool Matches(string source, string destination, out string? mismatch)
    {
        (int Count, long Bytes) from = Measure(source);
        (int Count, long Bytes) to = Measure(destination);

        if (from.Count != to.Count || from.Bytes != to.Bytes)
        {
            mismatch = $"コピーが一致しません（{from.Count} 件 {from.Bytes} バイト → {to.Count} 件 {to.Bytes} バイト）。";
            return false;
        }

        mismatch = null;
        return true;
    }

    private static (int Count, long Bytes) Measure(string root)
    {
        if (!Directory.Exists(root))
        {
            return (0, 0);
        }

        int count = 0;
        long bytes = 0;

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            count++;
            bytes += new FileInfo(path).Length;
        }

        return (count, bytes);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    /// <summary>画面に出す「いまの中身」。移すかどうかの判断に使う。</summary>
    public static DataRootSummary Describe(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        (int Count, long Bytes) measured = Measure(root);
        int history = 0;

        try
        {
            string historyRoot = Path.Combine(root, "history");
            if (Directory.Exists(historyRoot))
            {
                history = Directory.GetFiles(historyRoot, "*.json").Length;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return new DataRootSummary(root, measured.Count, measured.Bytes, history);
    }
}

/// <summary>保存先の中身の概要。</summary>
public sealed record DataRootSummary(string Root, int FileCount, long TotalBytes, int HistoryCount)
{
    public long TotalMegabytes => this.TotalBytes / 1024 / 1024;
}
