using System;
using System.Collections.Generic;
using System.IO;
using NutLens.Core.Imaging;
using NutLens.Core.Sessions;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class SessionLayoutTests
{
    [Fact]
    public void セッションフォルダは作業フォルダの下にセッションIDで作られる()
    {
        SessionId id = SessionId.Parse("8f14e45f-ceea-467a-9ba5-c4b5a0f0f0f0");
        SessionLayout layout = new(@"C:\work", id);

        Assert.Equal(@"C:\work\8f14e45f-ceea-467a-9ba5-c4b5a0f0f0f0", layout.Root);
        Assert.Equal(Path.Combine(layout.Root, "frames"), layout.FramesDirectory);
        Assert.Equal(Path.Combine(layout.Root, "prompt.md"), layout.PromptFile);
        Assert.Equal(Path.Combine(layout.Root, "claude.log"), layout.ClaudeLogFile);
        Assert.Equal(Path.Combine(layout.Root, "audio.wav"), layout.AudioFile);
    }

    [Theory]
    [InlineData(1, 0.0, "frame_001_t0000.0s.jpg")]
    [InlineData(2, 2.0, "frame_002_t0002.0s.jpg")]
    [InlineData(17, 123.45, "frame_017_t0123.5s.jpg")]
    [InlineData(999, 3599.9, "frame_999_t3599.9s.jpg")]
    public void フレームのファイル名に経過秒が入る(int index, double seconds, string expected)
    {
        // 段階 2 でプロンプトにパスを並べるだけで撮影時刻も伝わるようにするための書式（F-21）。
        string name = SessionLayout.FrameFileName(index, TimeSpan.FromSeconds(seconds), ".jpg");

        Assert.Equal(expected, name);
    }

    [Fact]
    public void セッションIDはハイフン区切りで文字列化される()
    {
        // Claude Code の --session-id が UUID を要求するため、形式を固定する（ADR-0007）。
        Guid guid = Guid.NewGuid();
        SessionId id = new(guid);

        Assert.Equal(guid.ToString("D"), id.ToString());
        Assert.True(SessionId.TryParse(id.ToString(), out SessionId roundTripped));
        Assert.Equal(id, roundTripped);
    }

    [Fact]
    public void 不正なセッションIDはパースに失敗する()
    {
        Assert.False(SessionId.TryParse("not-a-uuid", out _));
        Assert.False(SessionId.TryParse(null, out _));
    }
}

public sealed class FileSystemSessionWorkspaceTests : IDisposable
{
    private readonly string root;
    private readonly AppDataPaths paths;
    private readonly FileSystemSessionWorkspace workspace;

    public FileSystemSessionWorkspaceTests()
    {
        this.root = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.root);
        this.paths = new AppDataPaths(this.root);
        this.workspace = new FileSystemSessionWorkspace(this.paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    [Fact]
    public void 作成するとフレーム用のフォルダまでできる()
    {
        SessionLayout layout = this.workspace.Create(SessionId.New());

        Assert.True(Directory.Exists(layout.Root));
        Assert.True(Directory.Exists(layout.FramesDirectory));
    }

    [Fact]
    public void 書き出したファイルは撮影順で連番になる()
    {
        SessionLayout layout = this.workspace.Create(SessionId.New());
        List<EncodedFrame> frames =
        [
            new EncodedFrame([1, 2, 3], 100, 80, TimeSpan.FromSeconds(0), ".jpg"),
            new EncodedFrame([4, 5], 100, 80, TimeSpan.FromSeconds(2.5), ".jpg"),
        ];

        IReadOnlyList<string> written = this.workspace.WriteFrames(layout, frames);

        Assert.Equal(2, written.Count);
        Assert.EndsWith("frame_001_t0000.0s.jpg", written[0], StringComparison.Ordinal);
        Assert.EndsWith("frame_002_t0002.5s.jpg", written[1], StringComparison.Ordinal);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(written[0]));
        Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(written[1]));
    }

    [Fact]
    public void 削除するとセッションフォルダごと消える()
    {
        SessionId id = SessionId.New();
        SessionLayout layout = this.workspace.Create(id);
        File.WriteAllText(Path.Combine(layout.FramesDirectory, "dummy.jpg"), "x");

        Assert.True(this.workspace.Exists(id));

        this.workspace.Delete(id);

        Assert.False(this.workspace.Exists(id));
        Assert.False(Directory.Exists(layout.Root));
    }

    [Fact]
    public void 存在しないセッションを削除しても例外にならない()
    {
        this.workspace.Delete(SessionId.New());
    }

    [Fact]
    public void 残骸の一括削除で作業フォルダの中身が空になる()
    {
        // 異常終了で残った一時データを次回起動時に消す経路（5.5）。
        this.workspace.Create(SessionId.New());
        this.workspace.Create(SessionId.New());
        this.workspace.Create(SessionId.New());

        int deleted = this.workspace.DeleteAll();

        Assert.Equal(3, deleted);
        Assert.Empty(Directory.GetDirectories(this.paths.WorkRoot));
    }

    [Fact]
    public void 作業フォルダ自体が無くても一括削除は落ちない()
    {
        Assert.Equal(0, this.workspace.DeleteAll());
    }
}
