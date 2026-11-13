using System;
using System.IO;
using NutLens.Core.Sessions;
using Xunit;

namespace NutLens.Core.Tests;

/// <summary>
/// データの保存先（D-05 / ADR-0033）。
///
/// <b>ここが壊れると、利用者のデータが「消えたように見える」。</b>
/// </summary>
public sealed class DataRootTests : IDisposable
{
    private readonly string sandbox;
    private readonly string bootstrap;

    public DataRootTests()
    {
        this.sandbox = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        this.bootstrap = Path.Combine(this.sandbox, "install");
        Directory.CreateDirectory(this.bootstrap);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.sandbox))
        {
            Directory.Delete(this.sandbox, recursive: true);
        }
    }

    private string Path_(string name) => Path.Combine(this.sandbox, name);

    private void Seed(string root, int historyCount = 2)
    {
        Directory.CreateDirectory(Path.Combine(root, "history"));
        Directory.CreateDirectory(Path.Combine(root, "models"));
        File.WriteAllText(Path.Combine(root, "settings.json"), "{\"version\":2}");

        for (int i = 0; i < historyCount; i++)
        {
            File.WriteAllText(Path.Combine(root, "history", $"entry{i}.json"), "{}");
        }

        File.WriteAllBytes(Path.Combine(root, "models", "ggml-base.bin"), new byte[4096]);
    }

    [Fact]
    public void 指定が無ければ既定を使う()
    {
        // 5.5 と 6 章が名指ししている場所。
        Assert.Equal(DataRoot.Default, DataRoot.Resolve(this.bootstrap));
        Assert.EndsWith("NutLens", DataRoot.Default, StringComparison.Ordinal);
    }

    [Fact]
    public void 指定があればそこを使う()
    {
        string target = this.Path_("data");
        DataRoot.WritePointer(this.bootstrap, target);

        Assert.Equal(target, DataRoot.Resolve(this.bootstrap));
    }

    [Fact]
    public void 指定を消すと既定に戻る()
    {
        DataRoot.WritePointer(this.bootstrap, this.Path_("data"));
        DataRoot.WritePointer(this.bootstrap, null);

        Assert.Equal(DataRoot.Default, DataRoot.Resolve(this.bootstrap));
    }

    [Fact]
    public void 相対パスの指定は受け付けない()
    {
        // どこを基準にするかが決まらない。既定に落とす。
        File.WriteAllText(Path.Combine(this.bootstrap, DataRoot.PointerFileName), @"..\data");

        Assert.Equal(DataRoot.Default, DataRoot.Resolve(this.bootstrap));
    }

    [Fact]
    public void 空の指定は既定に落とす()
    {
        File.WriteAllText(Path.Combine(this.bootstrap, DataRoot.PointerFileName), "   ");

        Assert.Equal(DataRoot.Default, DataRoot.Resolve(this.bootstrap));
    }

    [Fact]
    public void 指定を探す場所が無くても落ちない()
    {
        Assert.Equal(DataRoot.Default, DataRoot.Resolve(this.Path_("ない")));
        Assert.Equal(DataRoot.Default, DataRoot.Resolve(null));
    }

    [Fact]
    public void 移すと中身がそのまま移る()
    {
        string from = this.Path_("from");
        string to = this.Path_("to");
        this.Seed(from);

        DataRootMoveResult result = DataRoot.Switch(this.bootstrap, from, to, moveExistingData: true);

        Assert.Equal(DataRootMoveStatus.Moved, result.Status);
        Assert.True(File.Exists(Path.Combine(to, "settings.json")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(to, "history")).Length);
        Assert.True(File.Exists(Path.Combine(to, "models", "ggml-base.bin")));

        // 元は消える。
        Assert.False(Directory.Exists(from));

        // 指定が新しい場所を指す。
        Assert.Equal(to, DataRoot.Resolve(this.bootstrap));
    }

    [Fact]
    public void 移さずに切り替えると元が残る()
    {
        string from = this.Path_("from");
        string to = this.Path_("to");
        this.Seed(from);

        DataRootMoveResult result = DataRoot.Switch(this.bootstrap, from, to, moveExistingData: false);

        Assert.Equal(DataRootMoveStatus.SwitchedWithoutMoving, result.Status);
        Assert.Equal(to, DataRoot.Resolve(this.bootstrap));

        // **元のデータは消さない。** 利用者が手で移せるようにする。
        Assert.True(File.Exists(Path.Combine(from, "settings.json")));
    }

    [Fact]
    public void 書けない場所なら何も変えない()
    {
        string from = this.Path_("from");
        this.Seed(from);

        // 既存のファイルをフォルダとして指定する＝作れない。
        string blocked = Path.Combine(from, "settings.json");

        DataRootMoveResult result = DataRoot.Switch(this.bootstrap, from, blocked, moveExistingData: true);

        Assert.Equal(DataRootMoveStatus.DestinationNotWritable, result.Status);
        Assert.False(result.Switched);

        // **指定は書かれていない。** 元のデータもそのまま。
        Assert.Equal(DataRoot.Default, DataRoot.Resolve(this.bootstrap));
        Assert.True(File.Exists(Path.Combine(from, "settings.json")));
    }

    [Fact]
    public void 同じ場所を指定しても壊れない()
    {
        string from = this.Path_("from");
        this.Seed(from);

        DataRootMoveResult result = DataRoot.Switch(this.bootstrap, from, from, moveExistingData: true);

        Assert.True(result.Switched);

        // **消さない。** 同じ場所なので移す必要が無い。
        Assert.True(File.Exists(Path.Combine(from, "settings.json")));
    }

    [Fact]
    public void 元が無くても切り替えられる()
    {
        string from = this.Path_("まだ無い");
        string to = this.Path_("to");

        DataRootMoveResult result = DataRoot.Switch(this.bootstrap, from, to, moveExistingData: true);

        Assert.True(result.Switched);
        Assert.Equal(to, DataRoot.Resolve(this.bootstrap));
    }

    [Fact]
    public void 中身の概要を出せる()
    {
        string root = this.Path_("data");
        this.Seed(root, historyCount: 3);

        DataRootSummary summary = DataRoot.Describe(root);

        Assert.Equal(root, summary.Root);
        Assert.Equal(3, summary.HistoryCount);

        // settings.json ＋ history 3 件 ＋ モデル 1 件。
        Assert.Equal(5, summary.FileCount);
        Assert.True(summary.TotalBytes > 4096);
    }

    [Fact]
    public void 概要はフォルダが無くても落ちない()
    {
        DataRootSummary summary = DataRoot.Describe(this.Path_("ない"));

        Assert.Equal(0, summary.FileCount);
        Assert.Equal(0, summary.HistoryCount);
    }

    [Fact]
    public void 指定ファイルの名前は固定()
    {
        // **current\ の外に置く**ことが要（ADR-0033 決定 2）。
        // 名前を変えると、既に指定した利用者の保存先が見つからなくなる。
        Assert.Equal("data-dir.txt", DataRoot.PointerFileName);
    }
}
