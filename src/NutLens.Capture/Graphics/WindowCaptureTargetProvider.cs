using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NutLens.Core.Capture;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace NutLens.Capture.Graphics;

/// <summary>撮影できるウィンドウを列挙する（F-01）。</summary>
public sealed class WindowCaptureTargetProvider : ICaptureTargetProvider
{
    /// <summary>自分自身のウィンドウは一覧に出さない。設定で差し替えられるようにしておく。</summary>
    public nint ExcludedWindow { get; set; }

    public IReadOnlyList<CaptureTarget> GetTargets()
    {
        List<CaptureTarget> targets = [];
        nint shellWindow = PInvoke.GetShellWindow();
        nint excluded = this.ExcludedWindow;

        PInvoke.EnumWindows(
            (HWND hwnd, LPARAM _) =>
            {
                if (IsCapturable(hwnd, shellWindow, excluded, out string title))
                {
                    targets.Add(new CaptureTarget(CaptureTargetKind.Window, hwnd, title));
                }

                return true;
            },
            default);

        targets.Sort(static (left, right) => string.CompareOrdinal(left.Title, right.Title));
        return targets;
    }

    private static bool IsCapturable(HWND hwnd, nint shellWindow, nint excluded, out string title)
    {
        title = string.Empty;

        if (hwnd == shellWindow || hwnd == excluded)
        {
            return false;
        }

        if (!PInvoke.IsWindowVisible(hwnd) || PInvoke.IsIconic(hwnd))
        {
            return false;
        }

        // ツールウィンドウ（ツールチップ、IME の候補窓など）は対象にしない。
        WINDOW_EX_STYLE exStyle = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(
            hwnd,
            WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        if (exStyle.HasFlag(WINDOW_EX_STYLE.WS_EX_TOOLWINDOW))
        {
            return false;
        }

        // DWM が cloaked とみなしたウィンドウ（UWP の幽霊ウィンドウ）を除く。
        // これを外すと、一覧が「見えていないのに存在するウィンドウ」で埋まる。
        if (IsCloaked(hwnd))
        {
            return false;
        }

        title = GetWindowTitle(hwnd);
        return !string.IsNullOrWhiteSpace(title);
    }

    private static unsafe bool IsCloaked(HWND hwnd)
    {
        int cloaked = 0;
        HRESULT hr = PInvoke.DwmGetWindowAttribute(
            hwnd,
            DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            &cloaked,
            sizeof(int));

        return hr.Succeeded && cloaked != 0;
    }

    private static unsafe string GetWindowTitle(HWND hwnd)
    {
        int length = PInvoke.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        Span<char> buffer = length < 256 ? stackalloc char[length + 1] : new char[length + 1];
        fixed (char* pointer = buffer)
        {
            int written = PInvoke.GetWindowText(hwnd, new PWSTR(pointer), buffer.Length);
            return written <= 0 ? string.Empty : new string(pointer, 0, written);
        }
    }
}

/// <summary>撮影できるモニタを列挙する。「画面の矩形」もここで得たモニタを切り出して実現する。</summary>
public sealed class MonitorCaptureTargetProvider : ICaptureTargetProvider
{
    public IReadOnlyList<CaptureTarget> GetTargets()
    {
        List<CaptureTarget> targets = [];

        unsafe
        {
            PInvoke.EnumDisplayMonitors(
                HDC.Null,
                (RECT*)null,
                (HMONITOR monitor, HDC _, RECT* _, LPARAM _) =>
                {
                    targets.Add(new CaptureTarget(
                        CaptureTargetKind.Monitor,
                        monitor,
                        DescribeMonitor(monitor, targets.Count + 1)));
                    return true;
                },
                default);
        }

        return targets;
    }

    /// <summary>モニタの境界（仮想デスクトップ座標）。矩形選択の結果をモニタ相対に直すのに使う。</summary>
    public static bool TryGetBounds(nint monitor, out PixelRect bounds)
    {
        if (TryGetInfo(monitor, out MONITORINFOEXW info))
        {
            bounds = new PixelRect(
                info.monitorInfo.rcMonitor.left,
                info.monitorInfo.rcMonitor.top,
                info.monitorInfo.rcMonitor.right - info.monitorInfo.rcMonitor.left,
                info.monitorInfo.rcMonitor.bottom - info.monitorInfo.rcMonitor.top);
            return true;
        }

        bounds = default;
        return false;
    }

    private static string DescribeMonitor(HMONITOR monitor, int ordinal)
    {
        if (TryGetBounds(monitor, out PixelRect bounds))
        {
            return $"モニタ {ordinal}（{bounds.Width} × {bounds.Height}）";
        }

        return $"モニタ {ordinal}";
    }

    private static unsafe bool TryGetInfo(nint monitor, out MONITORINFOEXW info)
    {
        MONITORINFOEXW local = default;
        local.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);

        if (PInvoke.GetMonitorInfo(new HMONITOR(monitor), (MONITORINFO*)&local))
        {
            info = local;
            return true;
        }

        info = default;
        return false;
    }
}
