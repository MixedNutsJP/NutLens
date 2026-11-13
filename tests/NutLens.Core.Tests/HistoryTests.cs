using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NutLens.Core.Analysis;
using NutLens.Core.History;
using NutLens.Core.Sessions;
using Xunit;

namespace NutLens.Core.Tests;

/// <summary>履歴の保存・一覧・削除（F-28 / 5.5 / ADR-0024）。</summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly string root;
    private readonly AppDataPaths paths;
    private readonly JsonHistoryStore store;

    public HistoryStoreTests()
    {
        this.root = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.root);
        this.paths = new AppDataPaths(this.root);
        this.store = new JsonHistoryStore(this.paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    private static HistoryEntry Entry(SessionId id, DateTimeOffset savedAt, string title) => new(
        HistoryEntry.CurrentVersion,
        id.ToString(),
        savedAt,
        [new HistoryCandidate(title, null, 2004, "movie", 0.8, ["根拠"], [], ["https://example.com"])],
        ["確認できなかった点"],
        "https://example.com/short",
        "投稿者",
        "キャプション",
        "メモ",
        7,
        42.5,
        0.12m);

    [Fact]
    public void 書いて読める()
    {
        SessionId id = SessionId.New();
        this.store.Save(Entry(id, DateTimeOffset.Now, "タクシー"));

        IReadOnlyList<HistoryEntry> entries = this.store.List();

        Assert.Single(entries);
        Assert.Equal(id.ToString(), entries[0].Id);
        Assert.Equal("タクシー", entries[0].Top?.DisplayTitle);
        Assert.Equal("メモ", entries[0].FreeText);
    }

    [Fact]
    public void 新しい順に並ぶ()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        this.store.Save(Entry(SessionId.New(), now.AddHours(-2), "古い"));
        this.store.Save(Entry(SessionId.New(), now, "新しい"));
        this.store.Save(Entry(SessionId.New(), now.AddHours(-1), "中くらい"));

        string[] titles = this.store.List().Select(static e => e.Top!.DisplayTitle).ToArray();

        Assert.Equal(["新しい", "中くらい", "古い"], titles);
    }

    [Fact]
    public void 同じIDで保存しても増えない()
    {
        // 再調査（F-27）で結果が変わったら上書きする。
        // 増やすと、一覧が同じ作品で埋まる（ADR-0024 決定 2）。
        SessionId id = SessionId.New();
        DateTimeOffset now = DateTimeOffset.Now;

        this.store.Save(Entry(id, now, "最初の候補"));
        this.store.Save(Entry(id, now.AddMinutes(5), "再調査後の候補"));

        IReadOnlyList<HistoryEntry> entries = this.store.List();

        Assert.Single(entries);
        Assert.Equal("再調査後の候補", entries[0].Top?.DisplayTitle);
        Assert.Single(Directory.GetFiles(this.paths.HistoryRoot));
    }

    [Fact]
    public void 個別に削除できる()
    {
        SessionId keep = SessionId.New();
        SessionId remove = SessionId.New();
        this.store.Save(Entry(keep, DateTimeOffset.Now, "残す"));
        this.store.Save(Entry(remove, DateTimeOffset.Now.AddMinutes(-1), "消す"));

        this.store.Delete(remove.ToString());

        Assert.Single(this.store.List());
        Assert.Equal("残す", this.store.List()[0].Top?.DisplayTitle);
    }

    [Fact]
    public void 一括削除してもフォルダは残る()
    {
        this.store.Save(Entry(SessionId.New(), DateTimeOffset.Now, "A"));
        this.store.Save(Entry(SessionId.New(), DateTimeOffset.Now, "B"));

        this.store.DeleteAll();

        Assert.Empty(this.store.List());
        Assert.True(Directory.Exists(this.paths.HistoryRoot));
    }

    [Fact]
    public void 壊れたファイルは消さずに飛ばす()
    {
        // 利用者が中身を確かめられるようにする（壊れた設定ファイルと同じ扱い）。
        this.store.Save(Entry(SessionId.New(), DateTimeOffset.Now, "正常"));

        string broken = Path.Combine(this.paths.HistoryRoot, "20261114T000000_broken.json");
        File.WriteAllText(broken, "{ これは JSON ではない");

        Assert.Single(this.store.List());
        Assert.True(File.Exists(broken));
    }

    [Fact]
    public void 知らない版は読まない()
    {
        string future = Path.Combine(this.paths.HistoryRoot, "20261114T000000_future.json");
        Directory.CreateDirectory(this.paths.HistoryRoot);
        File.WriteAllText(future, """{ "version": 999, "id": "future", "savedAt": "2026-11-14T00:00:00+09:00" }""");

        Assert.Empty(this.store.List());
        Assert.True(File.Exists(future));
    }

    [Fact]
    public void 履歴が無ければ空()
    {
        Assert.Empty(this.store.List());
    }
}

/// <summary>
/// <b>残すものと消すものの境目</b>（5.5 / ADR-0024 決定 3）。
/// ここが緩むと「解析が終わったら消す」が嘘になる。
/// </summary>
public sealed class HistoryEntryTests
{
    private static AnalysisOutcome Outcome() => new(
        Succeeded: true,
        new AnalysisResult(
            [
                new MovieCandidate("タクシー", "Taxi", 1998, "movie", 0.9, ["字幕に…"], [], ["https://example.com"]),
                new MovieCandidate("別の候補", null, 2001, "drama", 0.3, [], ["年代が合わない"], []),
            ],
            ["観察した特徴"],
            ["確認できなかった点"],
            ["検索語"]),
        new AnalysisRunInfo(SessionId.New(), 7, 0.12m, "end_turn", 0, TimeSpan.FromSeconds(42.5)),
        RawOutput: "{}",
        ErrorMessage: null);

    [Fact]
    public void 候補とメモを残す()
    {
        HistoryEntry entry = HistoryEntry.From(
            SessionId.New(),
            DateTimeOffset.Now,
            Outcome(),
            new UserNotes("https://example.com/short", "投稿者", "キャプション", "メモ"));

        Assert.Equal(2, entry.Candidates.Count);
        Assert.Equal("タクシー", entry.Top?.DisplayTitle);
        Assert.Equal("投稿者", entry.AuthorName);
        Assert.Equal(["確認できなかった点"], entry.UnverifiedPoints);
        Assert.Equal(7, entry.Turns);
    }

    [Fact]
    public void 観察した特徴は残さない()
    {
        // observed_features はフレームを見て書かれたもの。F-28 は「結果のテキスト」
        // （候補・根拠・URL）だけを求めており、履歴に要らない。
        HistoryEntry entry = HistoryEntry.From(
            SessionId.New(),
            DateTimeOffset.Now,
            Outcome(),
            UserNotes.Empty);

        string json = System.Text.Json.JsonSerializer.Serialize(entry);

        Assert.DoesNotContain("観察した特徴", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 候補が無くても作れる()
    {
        AnalysisOutcome failed = new(
            Succeeded: false,
            Result: null,
            new AnalysisRunInfo(SessionId.New(), 1, null, "error", 0, TimeSpan.Zero),
            RawOutput: "壊れた出力",
            ErrorMessage: "JSON が壊れています");

        HistoryEntry entry = HistoryEntry.From(SessionId.New(), DateTimeOffset.Now, failed, UserNotes.Empty);

        Assert.Empty(entry.Candidates);
        Assert.Null(entry.Top);
    }

    [Fact]
    public void 生の出力を残さない()
    {
        // claude.log と同じく、一時データ側（5.5）。
        AnalysisOutcome failed = new(
            Succeeded: false,
            Result: null,
            new AnalysisRunInfo(SessionId.New(), 1, null, "error", 0, TimeSpan.Zero),
            RawOutput: "ここに生の出力が入る",
            ErrorMessage: null);

        string json = System.Text.Json.JsonSerializer.Serialize(
            HistoryEntry.From(SessionId.New(), DateTimeOffset.Now, failed, UserNotes.Empty));

        Assert.DoesNotContain("ここに生の出力が入る", json, StringComparison.Ordinal);
    }
}
