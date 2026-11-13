using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NutLens.Core.Sessions;

namespace NutLens.Core.History;

/// <inheritdoc />
/// <remarks>
/// 1 解析 1 ファイル。<b>索引を持たない</b>（ADR-0024 決定 1）。
/// 索引を持つと「消したのに一覧に残る」という食い違いが生まれ、
/// それを直す仕組みをまた作ることになる。
/// </remarks>
public sealed class JsonHistoryStore : IHistoryStore
{
    /// <summary>利用者が開いて読むことがあるファイルは UTF-8（BOM つき）で書く。</summary>
    private static readonly Encoding TextEncoding =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    private const string Extension = ".json";

    /// <summary>コロンはファイル名に使えないので入れない。名前の順＝時間の順になる形。</summary>
    private const string TimestampFormat = "yyyyMMdd'T'HHmmss";

    private readonly IAppDataPaths paths;

    public JsonHistoryStore(IAppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
    }

    public IReadOnlyList<HistoryEntry> List()
    {
        if (!Directory.Exists(this.paths.HistoryRoot))
        {
            return [];
        }

        List<HistoryEntry> entries = [];
        foreach (string path in Directory.GetFiles(this.paths.HistoryRoot, "*" + Extension))
        {
            if (TryRead(path) is HistoryEntry entry)
            {
                entries.Add(entry);
            }

            // 読めなかったファイルは消さない。利用者が中身を確かめられるようにするため
            // （壊れた設定ファイルを退避するのと同じ扱い）。
        }

        entries.Sort(static (left, right) => right.SavedAt.CompareTo(left.SavedAt));
        return entries;
    }

    public void Save(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Directory.CreateDirectory(this.paths.HistoryRoot);

        // 再調査（F-27）で結果が変わったら同じファイルを上書きする。
        // 既存の名前を使い回さないと、日時の違う 2 つが残る（ADR-0024 決定 2）。
        string path = this.FindExisting(entry.Id)
            ?? Path.Combine(this.paths.HistoryRoot, BuildFileName(entry));

        string json = JsonSerializer.Serialize(entry, HistoryJsonContext.Default.HistoryEntry);

        // 設定と同じくアトミックに置き換える。
        // 直接上書きすると、書き込み中に落ちたときに履歴が 1 件壊れる。
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json, TextEncoding);
        File.Move(temporary, path, overwrite: true);
    }

    public void Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (this.FindExisting(id) is string path)
        {
            TryDelete(path);
        }
    }

    public void DeleteAll()
    {
        if (!Directory.Exists(this.paths.HistoryRoot))
        {
            return;
        }

        // フォルダ自体は残す。次の保存で作り直す手間と、消し漏れの判断が増えるため。
        foreach (string path in Directory.GetFiles(this.paths.HistoryRoot))
        {
            TryDelete(path);
        }
    }

    internal static string BuildFileName(HistoryEntry entry) => string.Format(
        CultureInfo.InvariantCulture,
        "{0}_{1}{2}",
        entry.SavedAt.ToLocalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture),
        entry.Id,
        Extension);

    /// <summary>
    /// 同じ ID のファイルを探す。
    /// ファイル名の日時の部分は保存のたびに変わりうるので、名前では組み立てられない。
    /// </summary>
    private string? FindExisting(string id)
    {
        if (!Directory.Exists(this.paths.HistoryRoot))
        {
            return null;
        }

        string[] matches = Directory.GetFiles(this.paths.HistoryRoot, $"*_{id}{Extension}");
        return matches.Length > 0 ? matches[0] : null;
    }

    private static HistoryEntry? TryRead(string path)
    {
        try
        {
            HistoryEntry? entry = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                HistoryJsonContext.Default.HistoryEntry);

            // 版が未来のファイルは読まない。知らない形を推測で解釈しない。
            if (entry is null || entry.Version > HistoryEntry.CurrentVersion || entry.Id is null)
            {
                return null;
            }

            return entry with
            {
                Candidates = entry.Candidates ?? [],
                UnverifiedPoints = entry.UnverifiedPoints ?? [],
            };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HistoryEntry))]
internal sealed partial class HistoryJsonContext : JsonSerializerContext
{
}
