using System;
using NutLens.Core.Capture;
using NutLens.Core.Diagnostics;
using NutLens.Core.Settings;
using Windows.Graphics.Capture;

namespace NutLens.Capture.Graphics;

/// <summary>
/// 撮影対象から <see cref="IFrameSource"/> を組み立てる（ADR-0002）。
/// 矩形が指定されていれば <see cref="CroppingFrameSource"/> で包む。
/// </summary>
public sealed class WgcFrameSourceFactory : IFrameSourceFactory
{
    private readonly ISettingsStore settingsStore;

    public WgcFrameSourceFactory(ISettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        this.settingsStore = settingsStore;
    }

    /// <summary>この環境で Windows.Graphics.Capture が使えるか。</summary>
    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public IFrameSource Create(CaptureTarget target, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!IsSupported)
        {
            throw new CaptureUnavailableException(
                "この環境では画面キャプチャ（Windows.Graphics.Capture）が利用できません。");
        }

        bool captureCursor = this.settingsStore.Load().Capture.CaptureCursor;

        Direct3DCaptureDevice device = Direct3DCaptureDevice.Create();
        try
        {
            GraphicsCaptureItem item = target.Kind switch
            {
                CaptureTargetKind.Window => CaptureInterop.CreateItemForWindow(target.Handle),
                CaptureTargetKind.Monitor => CaptureInterop.CreateItemForMonitor(target.Handle),
                _ => throw new ArgumentOutOfRangeException(nameof(target), target.Kind, "未知の撮影対象です。"),
            };

            IFrameSource source = new DeviceOwningFrameSource(
                new WgcFrameSource(item, device, interval, captureCursor),
                device);

            if (target.CropRegion is PixelRect region && !region.IsEmpty)
            {
                source = new CroppingFrameSource(source, region);
            }

            return source;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }
}
