using System;
using System.Runtime.InteropServices;
using NutLens.Core.Diagnostics;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;

namespace NutLens.Capture.Graphics;

/// <summary>
/// Direct3D11CaptureFramePool が要求する <see cref="IDirect3DDevice"/> を用意する役。
///
/// D3D11 に触るのはこのクラスだけ。
/// 画素の取り出しは <see cref="Windows.Graphics.Imaging.SoftwareBitmap"/> 側に任せており、
/// ステージングテクスチャの確保や Map/Unmap は書かない。
/// </summary>
internal sealed class Direct3DCaptureDevice : IDisposable
{
    private ID3D11Device? device;
    private ID3D11DeviceContext? context;

    private Direct3DCaptureDevice(ID3D11Device device, ID3D11DeviceContext context, IDirect3DDevice winrtDevice)
    {
        this.device = device;
        this.context = context;
        this.WinRtDevice = winrtDevice;
    }

    /// <summary>WinRT 側に渡すデバイス。</summary>
    public IDirect3DDevice WinRtDevice { get; }

    public static Direct3DCaptureDevice Create()
    {
        // BgraSupport は WGC のフレーム形式（B8G8R8A8）に合わせるために必須。
        // 付け忘れると、フレームプールの作成がそれらしい理由なく失敗する。
        const D3D11_CREATE_DEVICE_FLAG Flags = D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT;

        HRESULT hr = CreateDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE, Flags, out ID3D11Device? device, out ID3D11DeviceContext? context);

        if (hr.Failed)
        {
            // GPU が無い環境・リモートデスクトップ経由などではソフトウェア実装に落とす。
            hr = CreateDevice(D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_WARP, Flags, out device, out context);
        }

        if (hr.Failed || device is null || context is null)
        {
            throw new CaptureDeviceException(
                $"Direct3D 11 デバイスを作成できませんでした（HRESULT=0x{hr.Value:X8}）。");
        }

        nint dxgiPointer = 0;
        try
        {
            IDXGIDevice dxgiDevice = (IDXGIDevice)device;
            dxgiPointer = Marshal.GetComInterfaceForObject<IDXGIDevice, IDXGIDevice>(dxgiDevice);
            IDirect3DDevice winrtDevice = CaptureInterop.CreateDirect3DDevice(dxgiPointer);
            return new Direct3DCaptureDevice(device, context, winrtDevice);
        }
        catch (Exception exception) when (exception is InvalidCastException or COMException)
        {
            throw new CaptureDeviceException(
                "Direct3D 11 デバイスから WinRT のデバイスを作成できませんでした。",
                exception);
        }
        finally
        {
            if (dxgiPointer != 0)
            {
                Marshal.Release(dxgiPointer);
            }
        }
    }

    public void Dispose()
    {
        if (this.WinRtDevice is IDisposable disposableDevice)
        {
            disposableDevice.Dispose();
        }

        ReleaseComObject(ref this.context);
        ReleaseComObject(ref this.device);
    }

    private static unsafe HRESULT CreateDevice(
        D3D_DRIVER_TYPE driverType,
        D3D11_CREATE_DEVICE_FLAG flags,
        out ID3D11Device? device,
        out ID3D11DeviceContext? context)
    {
        return PInvoke.D3D11CreateDevice(
            pAdapter: null,
            DriverType: driverType,
            Software: HMODULE.Null,
            Flags: flags,
            pFeatureLevels: null,
            FeatureLevels: 0,
            SDKVersion: PInvoke.D3D11_SDK_VERSION,
            ppDevice: out device,
            pFeatureLevel: null,
            ppImmediateContext: out context);
    }

    private static void ReleaseComObject<T>(ref T? comObject)
        where T : class
    {
        if (comObject is null)
        {
            return;
        }

        if (Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }

        comObject = null;
    }
}
