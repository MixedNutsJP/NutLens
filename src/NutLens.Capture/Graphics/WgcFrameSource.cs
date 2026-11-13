using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NutLens.Core.Capture;
using NutLens.Core.Diagnostics;
using NutLens.Core.Imaging;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NutLens.Capture.Graphics;

/// <summary>
/// Windows.Graphics.Capture でフレームを取り続ける <see cref="IFrameSource"/>（ADR-0003）。
///
/// 要点は 2 つ。
/// 1. 別タイマーを持たず、FrameArrived の中で撮影間隔を判定して捨てる。
///    タイマー方式だとフレームプールの寿命管理と競合し、落ちるのが撮影中のランダムな時点になる。
/// 2. 採用しないフレームは GPU 上のまま Dispose する。CPU への転送を一切しない。
/// </summary>
internal sealed class WgcFrameSource : IFrameSource
{
    /// <summary>Channel の容量。符号化が詰まってもフレームを溜め込まず、古いものから捨てる。</summary>
    private const int ChannelCapacity = 4;

    private readonly GraphicsCaptureItem item;
    private readonly Direct3DCaptureDevice device;
    private readonly TimeSpan interval;
    private readonly bool captureCursor;
    private readonly Channel<CapturedFrame> channel;
    private readonly Stopwatch stopwatch = new();
    private readonly SemaphoreSlim readbackGate = new(1, 1);

    private Direct3D11CaptureFramePool? framePool;
    private GraphicsCaptureSession? session;
    private SizeInt32 lastSize;
    private TimeSpan lastAccepted = TimeSpan.MinValue;
    private int started;
    private int disposed;

    public WgcFrameSource(
        GraphicsCaptureItem item,
        Direct3DCaptureDevice device,
        TimeSpan interval,
        bool captureCursor)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(device);

        this.item = item;
        this.device = device;
        this.interval = interval;
        this.captureCursor = captureCursor;
        this.channel = Channel.CreateBounded<CapturedFrame>(
            new BoundedChannelOptions(ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });
    }

    public async IAsyncEnumerable<CapturedFrame> ReadFramesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        this.Start();

        await foreach (CapturedFrame frame in this.channel.Reader
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    private void Start()
    {
        if (Interlocked.Exchange(ref this.started, 1) == 1)
        {
            throw new InvalidOperationException("この IFrameSource は既に開始されています。");
        }

        this.lastSize = this.item.Size;
        this.item.Closed += this.OnItemClosed;

        // CreateFreeThreaded にすると FrameArrived が UI スレッドに依存しなくなる。
        // UI がもたつかないための前提（非機能要件）。
        this.framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            this.device.WinRtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            numberOfBuffers: 2,
            this.lastSize);
        this.framePool.FrameArrived += this.OnFrameArrived;

        this.session = this.framePool.CreateCaptureSession(this.item);
        this.session.IsCursorCaptureEnabled = this.captureCursor;

        // IsBorderRequired（録画中の黄色い枠を消す）は Windows 11（10.0.22000）以降にしかない。
        // 版を確かめずに設定すると、落ちるのは撮影開始の瞬間になる（ADR-0003 決定 3）。
        // ApiInformation ではなく OperatingSystem を使うのは、
        // プラットフォーム互換性の解析器（CA1416）がこちらを理解するため。
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            this.session.IsBorderRequired = false;
        }

        this.stopwatch.Start();
        this.session.StartCapture();
    }

    private void OnItemClosed(GraphicsCaptureItem sender, object args)
    {
        this.channel.Writer.TryComplete(
            new CaptureTargetLostException("キャプチャ対象のウィンドウまたは画面が閉じられました。"));
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        Direct3D11CaptureFrame? frame = sender.TryGetNextFrame();
        if (frame is null)
        {
            return;
        }

        try
        {
            // 対象のサイズが変わったらプールを作り直す。
            // 作り直さないと、以後のフレームが引き伸ばされた／切れた絵になる。
            if (frame.ContentSize.Width != this.lastSize.Width
                || frame.ContentSize.Height != this.lastSize.Height)
            {
                this.lastSize = frame.ContentSize;
                sender.Recreate(
                    this.device.WinRtDevice,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    numberOfBuffers: 2,
                    this.lastSize);
            }

            TimeSpan elapsed = this.stopwatch.Elapsed;
            if (this.lastAccepted != TimeSpan.MinValue && elapsed - this.lastAccepted < this.interval)
            {
                // 撮影間隔に達していない。GPU 上のまま捨てる（CPU へ転送しない）。
                return;
            }

            // 前の読み出しがまだ終わっていなければ、このフレームは見送る。
            // 待つと FrameArrived のハンドラが詰まり、以後のフレームが遅れる。
            if (!this.readbackGate.Wait(0))
            {
                return;
            }

            this.lastAccepted = elapsed;

            Direct3D11CaptureFrame captured = frame;
            frame = null;
            _ = this.ReadbackAsync(captured, elapsed);
        }
        catch (ObjectDisposedException)
        {
            // 破棄と行き違った。何もしない。
        }
        finally
        {
            frame?.Dispose();
        }
    }

    /// <summary>
    /// GPU のサーフェスから BGRA を取り出して Channel へ流す。
    /// SoftwareBitmap を経由することで、ステージングテクスチャの確保と Map/Unmap を書かずに済む。
    /// </summary>
    private async Task ReadbackAsync(Direct3D11CaptureFrame frame, TimeSpan elapsed)
    {
        try
        {
            using (frame)
            {
                using SoftwareBitmap bitmap = await SoftwareBitmap
                    .CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied)
                    .AsTask()
                    .ConfigureAwait(false);

                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;
                int stride = width * 4;

                Windows.Storage.Streams.Buffer buffer = new((uint)(stride * height));
                bitmap.CopyToBuffer(buffer);

                byte[] bytes = new byte[buffer.Length];
                DataReader.FromBuffer(buffer).ReadBytes(bytes);

                this.channel.Writer.TryWrite(new CapturedFrame(bytes, width, height, stride, elapsed));
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            this.channel.Writer.TryComplete(
                new CaptureDeviceException("フレームの読み出しに失敗しました。", exception));
        }
        finally
        {
            this.readbackGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        this.item.Closed -= this.OnItemClosed;

        if (this.framePool is not null)
        {
            this.framePool.FrameArrived -= this.OnFrameArrived;
        }

        this.session?.Dispose();
        this.framePool?.Dispose();
        this.stopwatch.Stop();
        this.channel.Writer.TryComplete();
        this.readbackGate.Dispose();

        this.session = null;
        this.framePool = null;

        return ValueTask.CompletedTask;
    }
}
