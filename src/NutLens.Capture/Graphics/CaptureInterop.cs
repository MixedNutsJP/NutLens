using System;
using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace NutLens.Capture.Graphics;

/// <summary>
/// GraphicsCaptureItem をウィンドウ・モニタから直接作るための COM インタフェース。
/// WinRT の射影には現れないので、自分で宣言する。
/// </summary>
[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    nint CreateForWindow(nint window, in Guid iid);

    nint CreateForMonitor(nint monitor, in Guid iid);
}

/// <summary>IDirect3DSurface / IDirect3DDevice から元の DXGI インタフェースを取り出す。</summary>
[ComImport]
[Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDxgiInterfaceAccess
{
    nint GetInterface(in Guid iid);
}

/// <summary>
/// WinRT / COM の相互運用。
///
/// ADR-0010 決定 5：<c>dynamic</c> を使わない。
/// 作例の多くは dynamic やリフレクションでアクティベーションファクトリを取るが、
/// 相互運用こそ型の取り違えが実行時まで出ない場所なので、
/// ComImport のインタフェース宣言と明示的なキャストで書く。
/// </summary>
internal static partial class CaptureInterop
{
    /// <summary>IGraphicsCaptureItem の IID。</summary>
    private static readonly Guid GraphicsCaptureItemIid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    /// <summary>IActivationFactory の IID。</summary>
    private static readonly Guid ActivationFactoryIid = new("00000035-0000-0000-C000-000000000046");

    private const string GraphicsCaptureItemClassName = "Windows.Graphics.Capture.GraphicsCaptureItem";

    public static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
    {
        IGraphicsCaptureItemInterop interop = GetItemInterop();
        nint abi = interop.CreateForWindow(hwnd, GraphicsCaptureItemIid);
        return FromAbi(abi);
    }

    public static GraphicsCaptureItem CreateItemForMonitor(nint hmonitor)
    {
        IGraphicsCaptureItemInterop interop = GetItemInterop();
        nint abi = interop.CreateForMonitor(hmonitor, GraphicsCaptureItemIid);
        return FromAbi(abi);
    }

    /// <summary>DXGI デバイスから WinRT の IDirect3DDevice を作る。</summary>
    public static IDirect3DDevice CreateDirect3DDevice(nint dxgiDevice)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint abi));

        try
        {
            return MarshalInspectable<IDirect3DDevice>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    /// <summary>WinRT のサーフェスから ID3D11Texture2D のポインタを取り出す。呼び出し側が Release する。</summary>
    public static nint GetDxgiInterface(object surface, Guid iid)
    {
        if (surface is not IDirect3DDxgiInterfaceAccess access)
        {
            throw new NotSupportedException(
                "キャプチャしたサーフェスが IDirect3DDxgiInterfaceAccess を実装していません。");
        }

        return access.GetInterface(iid);
    }

    private static GraphicsCaptureItem FromAbi(nint abi)
    {
        // FromAbi は所有権を取らない（内部で AddRef する）ので、こちらの参照は手放す。
        try
        {
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    private static IGraphicsCaptureItemInterop GetItemInterop()
    {
        nint hstring = 0;
        nint factory = 0;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(
                GraphicsCaptureItemClassName,
                (uint)GraphicsCaptureItemClassName.Length,
                out hstring));

            Guid iid = ActivationFactoryIid;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(hstring, ref iid, out factory));

            object? instance = Marshal.GetObjectForIUnknown(factory);
            if (instance is not IGraphicsCaptureItemInterop interop)
            {
                throw new NotSupportedException(
                    "GraphicsCaptureItem のアクティベーションファクトリが IGraphicsCaptureItemInterop を実装していません。");
            }

            return interop;
        }
        finally
        {
            if (factory != 0)
            {
                Marshal.Release(factory);
            }

            if (hstring != 0)
            {
                WindowsDeleteString(hstring);
            }
        }
    }

    [LibraryImport("d3d11.dll")]
    private static partial int CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string sourceString, uint length, out nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);
}
