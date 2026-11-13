using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NutLens.Core.Capture;
using NutLens.Core.Imaging;

namespace NutLens.Capture.Graphics;

/// <summary>
/// フレーム源と一緒に Direct3D デバイスを破棄するためのデコレータ。
///
/// デバイスはフレーム源より長く生きてはいけないが、フレーム源にデバイスの所有権を持たせると
/// 「誰が作って誰が壊すか」が曖昧になる。所有を 1 枚のクラスに切り出して明示する。
/// </summary>
internal sealed class DeviceOwningFrameSource : IFrameSource
{
    private readonly IFrameSource inner;
    private readonly Direct3DCaptureDevice device;

    public DeviceOwningFrameSource(IFrameSource inner, Direct3DCaptureDevice device)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(device);

        this.inner = inner;
        this.device = device;
    }

    public IAsyncEnumerable<CapturedFrame> ReadFramesAsync(CancellationToken cancellationToken) =>
        this.inner.ReadFramesAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await this.inner.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            this.device.Dispose();
        }
    }
}
