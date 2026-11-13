using System;
using System.IO;
using NutLens.Core.Diagnostics;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;
using Xunit;

namespace NutLens.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string root;
    private readonly AppDataPaths paths;
    private readonly JsonSettingsStore store;

    public SettingsTests()
    {
        this.root = Path.Combine(Path.GetTempPath(), "NutLensTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.root);
        this.paths = new AppDataPaths(this.root);
        this.store = new JsonSettingsStore(this.paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.root))
        {
            Directory.Delete(this.root, recursive: true);
        }
    }

    [Fact]
    public void ファイルが無ければ既定値を返す()
    {
        NutLensSettings settings = this.store.Load();

        Assert.Equal(2.0, settings.Capture.IntervalSeconds);
        Assert.Equal(60.0, settings.Capture.DurationSeconds);
        Assert.Equal(12, settings.Capture.DuplicateThreshold);
        Assert.Equal(20, settings.Capture.MaxFrames);
        Assert.Equal(1280, settings.Capture.LongEdgePixels);
        Assert.Equal("ja", settings.UiLanguage);
        Assert.Equal(30, settings.Claude.MaxTurns);
    }

    [Fact]
    public void 一時データを残す設定の既定値は偽である()
    {
        // 既定が false であること自体が要件 5.5 の達成条件なので、明示的に固定する。
        Assert.False(new NutLensSettings().KeepTemporaryData);
        Assert.False(this.store.Load().KeepTemporaryData);
    }

    [Fact]
    public void 保存して読み戻すと同じ値になる()
    {
        NutLensSettings original = new()
        {
            UiLanguage = "en",
            KeepTemporaryData = true,
            Capture = new CaptureSettings
            {
                IntervalSeconds = 1.5,
                DurationSeconds = 120,
                DuplicateThreshold = 8,
                MaxFrames = 30,
                LongEdgePixels = 960,
                JpegQuality = 92,
                MaxFramesInMemoryLimit = 400,
                CaptureCursor = true,
            },
            Claude = new ClaudeSettings { ExecutablePath = @"C:\tools\claude.exe", MaxTurns = 45 },
        };

        this.store.Save(original);
        NutLensSettings loaded = this.store.Load();

        Assert.Equal(original, loaded);
    }

    [Fact]
    public void 壊れたJSONなら退避して例外を投げる()
    {
        File.WriteAllText(this.paths.SettingsFile, "{ これは JSON ではない");

        SettingsCorruptedException exception = Assert.Throws<SettingsCorruptedException>(() => this.store.Load());

        Assert.True(File.Exists(exception.BackupPath));
        Assert.False(File.Exists(this.paths.SettingsFile));
    }

    [Fact]
    public void 範囲外の値は丸められる()
    {
        CaptureSettings extreme = new()
        {
            IntervalSeconds = 0.01,
            DurationSeconds = 99999,
            DuplicateThreshold = 99,
            MaxFrames = 100000,
            LongEdgePixels = 10,
            JpegQuality = 5,
            MaxFramesInMemoryLimit = 1,
        };

        CaptureSettings normalized = extreme.Normalize();

        Assert.Equal(CaptureSettings.MinIntervalSeconds, normalized.IntervalSeconds);
        Assert.Equal(CaptureSettings.MaxDurationSeconds, normalized.DurationSeconds);
        Assert.Equal(64, normalized.DuplicateThreshold);
        Assert.Equal(CaptureSettings.MaxMaxFrames, normalized.MaxFrames);
        Assert.Equal(CaptureSettings.MinLongEdgePixels, normalized.LongEdgePixels);
        Assert.Equal(CaptureSettings.MinJpegQuality, normalized.JpegQuality);

        // メモリ上限は保存枚数を下回ってはいけない。
        Assert.True(normalized.MaxFramesInMemoryLimit >= normalized.MaxFrames);
    }

    [Fact]
    public void 範囲外の設定ファイルを読んでも丸めた値になる()
    {
        File.WriteAllText(
            this.paths.SettingsFile,
            """
            {
              "version": 1,
              "uiLanguage": "",
              "capture": { "intervalSeconds": 0.01, "maxFrames": 100000 }
            }
            """);

        NutLensSettings loaded = this.store.Load();

        Assert.Equal(CaptureSettings.MinIntervalSeconds, loaded.Capture.IntervalSeconds);
        Assert.Equal(CaptureSettings.MaxMaxFrames, loaded.Capture.MaxFrames);
        Assert.Equal("ja", loaded.UiLanguage);
    }

    [Fact]
    public void 入れ子の設定が書かれていなくても既定値で読める()
    {
        // 実装中に見つかった不具合の回帰テスト。
        // System.Text.Json の「ソース生成」で逆直列化すると、JSON に無い入れ子オブジェクトに
        // プロパティ初期化子が効かず null が入る（リフレクション経路では初期化子が効く）。
        // 受け止めないと、落ちるのは設定を読んだ瞬間＝アプリの起動時になる。
        File.WriteAllText(this.paths.SettingsFile, """{ "version": 1 }""");

        NutLensSettings loaded = this.store.Load();

        Assert.NotNull(loaded.Capture);
        Assert.NotNull(loaded.Claude);
        Assert.Equal(2.0, loaded.Capture.IntervalSeconds);
        Assert.Equal(30, loaded.Claude.MaxTurns);
    }

    [Fact]
    public void 入れ子の設定がnullと書かれていても既定値で読める()
    {
        File.WriteAllText(
            this.paths.SettingsFile,
            """{ "version": 1, "capture": null, "claude": null }""");

        NutLensSettings loaded = this.store.Load();

        Assert.Equal(20, loaded.Capture.MaxFrames);
        Assert.Equal(30, loaded.Claude.MaxTurns);
    }

    [Fact]
    public void WebDetectionがnullと書かれていても既定値で読める()
    {
        File.WriteAllText(
            this.paths.SettingsFile,
            """{ "version": 2, "webDetection": null }""");

        NutLensSettings loaded = this.store.Load();

        Assert.NotNull(loaded.WebDetection);
        Assert.False(loaded.WebDetection.Enabled);
        Assert.Equal(0, loaded.WebDetection.ConsentVersion);
        Assert.Equal(3, loaded.WebDetection.MaxImages);
    }

    [Fact]
    public void WebDetectionの既定はオフ()
    {
        // 5.6：初期値はオフ。**ここが true になったら要件違反。**
        File.WriteAllText(this.paths.SettingsFile, """{ "version": 2 }""");

        Assert.False(this.store.Load().WebDetection.Enabled);
    }

    [Fact]
    public void ホットキーがnullと書かれていても既定値で読める()
    {
        File.WriteAllText(this.paths.SettingsFile, """{ "version": 2, "hotkey": null }""");

        NutLensSettings loaded = this.store.Load();

        Assert.NotNull(loaded.Hotkey);
        Assert.True(loaded.Hotkey.Enabled);
        Assert.Equal("Ctrl+Shift+F9", loaded.Hotkey.Gesture);
    }

    [Fact]
    public void 読めないホットキーは既定へ戻して読み込む()
    {
        // ここで直さないと、起動のたびに「登録できません」が出続ける。
        File.WriteAllText(
            this.paths.SettingsFile,
            """{ "version": 2, "hotkey": { "enabled": true, "gesture": "Ctrl+??" } }""");

        Assert.Equal("Ctrl+Shift+F9", this.store.Load().Hotkey.Gesture);
    }

    [Fact]
    public void WebDetectionの範囲外の値は丸まる()
    {
        File.WriteAllText(
            this.paths.SettingsFile,
            """{ "version": 2, "webDetection": { "maxImages": 9999, "timeoutSeconds": 0.1 } }""");

        NutLensSettings loaded = this.store.Load();

        Assert.Equal(WebDetectionSettings.MaxMaxImages, loaded.WebDetection.MaxImages);
        Assert.Equal(WebDetectionSettings.MinTimeoutSeconds, loaded.WebDetection.TimeoutSeconds);
    }

    [Fact]
    public void 旧い版のしきい値は既定値に戻る()
    {
        // ADR-0015 決定 4。版 1 の 5 は 64 bit 中の値で、576 bit では極端に厳しい設定として効く。
        // そのまま読むと「更新したら重複が除かれなくなった」という形で表れる。
        File.WriteAllText(
            this.paths.SettingsFile,
            """
            {
              "version": 1,
              "capture": { "duplicateThreshold": 5, "maxFrames": 30 }
            }
            """);

        NutLensSettings loaded = this.store.Load();

        Assert.Equal(NutLensSettings.CurrentVersion, loaded.Version);
        Assert.Equal(12, loaded.Capture.DuplicateThreshold);

        // しきい値以外の設定は保たれること。移行で全部を初期化しない。
        Assert.Equal(30, loaded.Capture.MaxFrames);
    }

    [Fact]
    public void 今の版のしきい値は保たれる()
    {
        File.WriteAllText(
            this.paths.SettingsFile,
            """
            {
              "version": 2,
              "capture": { "duplicateThreshold": 20 }
            }
            """);

        Assert.Equal(20, this.store.Load().Capture.DuplicateThreshold);
    }

    [Fact]
    public void 保存後に一時ファイルが残らない()
    {
        this.store.Save(new NutLensSettings());

        Assert.True(File.Exists(this.paths.SettingsFile));
        Assert.False(File.Exists(this.paths.SettingsFile + ".tmp"));
    }
}
