using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Analysis;
using NutLens.Core.Diagnostics;
using NutLens.Core.Distribution;
using NutLens.Core.Media;
using NutLens.Core.Sessions;
using NutLens.Core.Settings;

namespace NutLens.App.Services;

/// <summary>
/// 初回セットアップの確認（D-03 / ADR-0029）。
///
/// <b>利用枠を使う確認と、使わない確認を分けてある。</b>
/// <see cref="CheckWithoutSpendingQuota"/> は起動のたびに呼んでよい。
/// <see cref="CheckClaudeUsableAsync"/> は<b>利用者が押したときだけ</b>呼ぶ。
/// </summary>
public sealed class SetupChecker : ISetupChecker
{
    private readonly IClaudeCliLocator locator;
    private readonly ISettingsStore settingsStore;
    private readonly IAppDataPaths paths;
    private readonly IOcrEngine ocrEngine;
    private readonly IClaudeProbe probe;

    public SetupChecker(
        IClaudeCliLocator locator,
        ISettingsStore settingsStore,
        IAppDataPaths paths,
        IOcrEngine ocrEngine,
        IClaudeProbe probe)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(ocrEngine);
        ArgumentNullException.ThrowIfNull(probe);

        this.locator = locator;
        this.settingsStore = settingsStore;
        this.paths = paths;
        this.ocrEngine = ocrEngine;
        this.probe = probe;
    }

    public IReadOnlyList<SetupCheck> CheckWithoutSpendingQuota()
    {
        NutLensSettings settings = this.LoadSettings();

        return
        [
            this.CheckClaude(settings),
            this.CheckModel(settings),
            this.CheckOcr(),
        ];
    }

    public async Task<SetupCheck> CheckClaudeUsableAsync(CancellationToken cancellationToken)
    {
        // **ここで利用者の利用枠を使う。** 呼ぶのは押されたときだけ（ADR-0029 決定 1）。
        ClaudeProbeResult result = await this.probe
            .ProbeAsync(this.LoadSettings().Claude.ExecutablePath, cancellationToken)
            .ConfigureAwait(false);

        // 成功したときは文言を添えない。**マークが状態を示す**ので、
        // 「動きました」は印と同じことを二度言うだけになる。
        return result.Succeeded
            ? SetupCheck.Ok(SetupCheckKind.ClaudeUsable)
            : SetupCheck.Missing(SetupCheckKind.ClaudeUsable, result.Detail);
    }

    private SetupCheck CheckClaude(NutLensSettings settings)
    {
        try
        {
            // --version を実行するだけ。**利用枠は使わない。**
            ClaudeExecutable executable = this.locator.Locate(settings.Claude.ExecutablePath);
            // 添えるのは**読む価値のある情報だけ**（場所と版）。「あります」は印が示す。
            return SetupCheck.Ok(
                SetupCheckKind.ClaudeInstalled,
                Resources.Strings.SetupClaudeFoundFormat(executable.Path, executable.Version));
        }
        catch (ClaudeNotFoundException)
        {
            return SetupCheck.Missing(SetupCheckKind.ClaudeInstalled, Resources.Strings.SetupClaudeMissing);
        }
        catch (ClaudeExecutionException exception)
        {
            return SetupCheck.Missing(SetupCheckKind.ClaudeInstalled, exception.Message);
        }
    }

    private SetupCheck CheckModel(NutLensSettings settings)
    {
        string path = settings.Whisper.ModelPath ?? Path.Combine(
            this.paths.ModelsRoot,
            WhisperSettings.DefaultModelFileName);

        if (!File.Exists(path))
        {
            return SetupCheck.Missing(SetupCheckKind.WhisperModel, Resources.Strings.SetupModelMissing);
        }

        long megabytes = new FileInfo(path).Length / 1024 / 1024;
        return SetupCheck.Ok(
            SetupCheckKind.WhisperModel,
            Resources.Strings.SetupModelFoundFormat(Path.GetFileName(path), megabytes));
    }

    private SetupCheck CheckOcr()
    {
        return this.ocrEngine.IsAvailable(out string? reason)
            ? SetupCheck.Ok(SetupCheckKind.OcrLanguage)
            : SetupCheck.Missing(SetupCheckKind.OcrLanguage, reason ?? Resources.Strings.SetupOcrMissing);
    }

    private NutLensSettings LoadSettings()
    {
        try
        {
            return this.settingsStore.Load();
        }
        catch (SettingsCorruptedException)
        {
            return new NutLensSettings();
        }
    }
}
