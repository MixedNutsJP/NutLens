using System;
using NutLens.App.Mvvm;
using NutLens.App.Resources;
using NutLens.Core.Diagnostics;
using NutLens.Core.Security;
using NutLens.Core.Settings;

namespace NutLens.App.ViewModels;

/// <summary>
/// 「高度な設定（上級者向け）」の中身（F-14 / 5.6 / ADR-0021）。
///
/// 撮影の設定とは責務が違うので別クラスにする。
/// <b>有効化の入口はここだけ。</b>同意ダイアログを通らずに
/// <see cref="WebDetectionSettings.ConsentVersion"/> を書く経路を作らない。
/// </summary>
public sealed class WebDetectionViewModel : ObservableObject
{
    private readonly ISettingsStore settingsStore;
    private readonly ISecretStore secrets;
    private readonly IWebDetectionPrompt prompt;
    private readonly IUserNotifier notifier;

    private bool enabled;
    private int maxImages;
    private bool hasApiKey;

    public WebDetectionViewModel(
        ISettingsStore settingsStore,
        ISecretStore secrets,
        IWebDetectionPrompt prompt,
        IUserNotifier notifier)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(notifier);

        this.settingsStore = settingsStore;
        this.secrets = secrets;
        this.prompt = prompt;
        this.notifier = notifier;

        WebDetectionSettings current = this.LoadSettings();
        this.enabled = current.Enabled;
        this.maxImages = current.MaxImages;
        this.hasApiKey = this.ReadHasApiKey();

        this.SetApiKeyCommand = new RelayCommand(this.SetApiKey);
        this.DeleteApiKeyCommand = new RelayCommand(this.DeleteApiKey, () => this.hasApiKey);
    }

    public RelayCommand SetApiKeyCommand { get; }

    public RelayCommand DeleteApiKeyCommand { get; }

    /// <summary>
    /// 使うか。<b>true にしようとすると同意ダイアログが出る</b>（5.6）。
    /// 同意しなければ値は変わらず、画面のチェックも元に戻る。
    /// </summary>
    public bool Enabled
    {
        get => this.enabled;
        set
        {
            if (this.enabled == value)
            {
                return;
            }

            if (value && !this.prompt.AskConsent(this.maxImages))
            {
                // 同意しなかった。チェックボックスは既に見た目が変わっているので、
                // 同じプロパティの変更を知らせて読み直させる。
                this.RaisePropertyChanged(nameof(this.Enabled));
                return;
            }

            this.enabled = value;
            this.Persist(recordConsent: value);
            this.RaisePropertyChanged(nameof(this.Enabled));
            this.RaisePropertyChanged(nameof(this.KeyStatusText));
        }
    }

    public int MaxImages
    {
        get => this.maxImages;
        set
        {
            int clamped = Math.Clamp(
                value,
                WebDetectionSettings.MinMaxImages,
                WebDetectionSettings.MaxMaxImages);

            if (this.SetProperty(ref this.maxImages, clamped))
            {
                this.Persist(recordConsent: false);
            }
        }
    }

    /// <summary>
    /// 鍵の有無。<b>鍵が無ければ有効にしても送られない</b>ので、その旨を出す
    /// （黙って何もしないと「有効にしたのに動かない」が原因不明になる）。
    /// </summary>
    public string KeyStatusText => this.hasApiKey
        ? Strings.WebDetectionKeyPresent
        : Strings.WebDetectionKeyMissing;

    private void SetApiKey()
    {
        string? key = this.prompt.AskApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        try
        {
            this.secrets.Write(SecretNames.GoogleCloudVisionApiKey, key);
        }
        catch (InvalidOperationException exception)
        {
            this.notifier.ShowError(exception.Message);
            return;
        }

        this.RefreshKeyState();
        this.notifier.ShowInformation(Strings.WebDetectionKeySaved);
    }

    private void DeleteApiKey()
    {
        this.secrets.Delete(SecretNames.GoogleCloudVisionApiKey);
        this.RefreshKeyState();
        this.notifier.ShowInformation(Strings.WebDetectionKeyDeleted);
    }

    private void RefreshKeyState()
    {
        this.hasApiKey = this.ReadHasApiKey();
        this.RaisePropertyChanged(nameof(this.KeyStatusText));
        this.DeleteApiKeyCommand.RaiseCanExecuteChanged();
    }

    private bool ReadHasApiKey()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(this.secrets.TryRead(SecretNames.GoogleCloudVisionApiKey));
        }
        catch (InvalidOperationException)
        {
            // 資格情報マネージャーが使えない環境でも、画面は開けるようにする。
            return false;
        }
    }

    /// <summary>
    /// <paramref name="recordConsent"/> が true のときだけ同意の版を書く。
    /// <b>有効化と同意の記録を同じ保存で行う</b>（ADR-0021 決定 2）。
    /// 無効化では版を消さない。一度読んで同意した事実は、オフに戻しても変わらない。
    /// </summary>
    private void Persist(bool recordConsent)
    {
        try
        {
            NutLensSettings current = this.settingsStore.Load();
            WebDetectionSettings web = current.WebDetection ?? new WebDetectionSettings();

            this.settingsStore.Save(current with
            {
                WebDetection = web with
                {
                    Enabled = this.enabled,
                    MaxImages = this.maxImages,
                    ConsentVersion = recordConsent
                        ? WebDetectionSettings.CurrentConsentVersion
                        : web.ConsentVersion,
                },
            });
        }
        catch (SettingsCorruptedException)
        {
            // 保存できなくても画面は使える。次の保存で直る。
        }
    }

    private WebDetectionSettings LoadSettings()
    {
        try
        {
            return this.settingsStore.Load().WebDetection ?? new WebDetectionSettings();
        }
        catch (SettingsCorruptedException)
        {
            return new WebDetectionSettings();
        }
    }
}
