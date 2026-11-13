using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Diagnostics;
using NutLens.Core.Distribution;
using NutLens.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace NutLens.App.Services;

/// <summary>
/// GitHub Releases からの更新（D-02 / ADR-0028）。
///
/// <b>例外を外へ出さない。</b>確認の失敗は <see cref="UpdateCheckStatus.Failed"/> で返し、
/// 画面には出さない（決定 2：利用者が頼んだ操作ではない）。
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly ISettingsStore settingsStore;
    private readonly TimeProvider timeProvider;

    /// <summary>確認で見つかった更新。同意の後に落とすために持っておく（決定 6）。</summary>
    private UpdateInfo? pending;

    private UpdateManager? manager;

    public VelopackUpdateService(ISettingsStore settingsStore, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.settingsStore = settingsStore;
        this.timeProvider = timeProvider;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        NutLensSettings settings = this.LoadSettings();

        if (!settings.Update.CheckOnStartup)
        {
            return UpdateCheckResult.Of(UpdateCheckStatus.Disabled);
        }

        UpdateManager updates;
        try
        {
            updates = this.GetManager();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return UpdateCheckResult.Of(UpdateCheckStatus.Failed, exception.Message);
        }

        // 開発中の実行やポータブル版ではインストールされていない。確認もしない。
        if (!updates.IsInstalled)
        {
            return UpdateCheckResult.Of(UpdateCheckStatus.NotInstalled);
        }

        string current = updates.CurrentVersion?.ToString() ?? "0.0.0";

        try
        {
            UpdateInfo? info = await updates.CheckForUpdatesAsync().ConfigureAwait(false);

            if (info is null)
            {
                this.RecordCheck(Resources.Strings.UpdateUpToDate);
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, current, null);
            }

            string available = info.TargetFullRelease.Version.ToString();

            // 見送った版は、もう聞かない（決定 4）。
            if (string.Equals(settings.Update.SkippedVersion, available, StringComparison.Ordinal))
            {
                this.RecordCheck(Resources.Strings.UpdateSkippedFormat(available));
                return new UpdateCheckResult(UpdateCheckStatus.Skipped, available, current, null);
            }

            this.pending = info;
            this.RecordCheck(Resources.Strings.UpdateFoundFormat(available));
            return new UpdateCheckResult(UpdateCheckStatus.Available, available, current, null);
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException
            or TaskCanceledException
            or System.IO.IOException
            or InvalidOperationException)
        {
            // オフライン・GitHub に繋がらない・上限。**画面には出さない**（決定 2）。
            // ただし忘れない。「このアプリについて」で見られる。
            this.RecordCheck(Resources.Strings.UpdateCheckFailed);
            return UpdateCheckResult.Of(UpdateCheckStatus.Failed, exception.Message);
        }
    }

    public async Task<bool> DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken)
    {
        if (this.pending is not UpdateInfo info)
        {
            return false;
        }

        try
        {
            await this.GetManager()
                .DownloadUpdatesAsync(info, percent => progress?.Report(percent), cancelToken: cancellationToken)
                .ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException
            or TaskCanceledException
            or System.IO.IOException
            or InvalidOperationException)
        {
            return false;
        }
    }

    public void ApplyAndRestart()
    {
        if (this.pending is UpdateInfo info)
        {
            // 戻ってこない。
            this.GetManager().ApplyUpdatesAndRestart(info.TargetFullRelease);
        }
    }

    public void ApplyOnNextStart()
    {
        if (this.pending is UpdateInfo info)
        {
            // アプリが終わるのを待ってから当てる。終了の経路は変えない。
            this.GetManager().WaitExitThenApplyUpdates(info.TargetFullRelease);
        }
    }

    /// <summary>利用者が見送った版を覚える（決定 4）。</summary>
    public void RememberSkipped(string version)
    {
        this.Mutate(update => update with { SkippedVersion = version });
    }

    private UpdateManager GetManager()
    {
        // 更新元は GitHub Releases だけ。**端点は設定で変えない**（ADR-0028 影響）。
        return this.manager ??= new UpdateManager(new GithubSource(RepositoryUrl(), null, false));
    }

    private static string RepositoryUrl()
    {
        // build/Distribution.props から埋め込んだ値を読む（ADR-0031 決定 7）。
        foreach (AssemblyMetadataAttribute attribute in typeof(VelopackUpdateService).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(attribute.Key, "RepositoryUrl", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(attribute.Value))
            {
                return attribute.Value;
            }
        }

        throw new InvalidOperationException("配布先の URL が埋め込まれていません（build/Distribution.props）。");
    }

    private void RecordCheck(string result)
    {
        this.Mutate(update => update with
        {
            LastCheckedAt = this.timeProvider.GetLocalNow(),
            LastCheckResult = result,
        });
    }

    private void Mutate(Func<UpdateSettings, UpdateSettings> change)
    {
        try
        {
            NutLensSettings current = this.settingsStore.Load();
            this.settingsStore.Save(current with { Update = change(current.Update) });
        }
        catch (Exception exception) when (exception is SettingsCorruptedException or System.IO.IOException)
        {
            // 記録できなくても更新の確認そのものは続く。
        }
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
