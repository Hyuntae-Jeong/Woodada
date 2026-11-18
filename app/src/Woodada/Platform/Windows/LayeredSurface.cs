using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;

namespace Woodada.Platform.Windows;

/// <summary>
/// UpdateLayeredWindow로 창에 그림을 넘기는 판. 그림은 위에서 아래로 쌓인 32비트 BGRA(알파를 곱해 둔 값)다.
/// 같은 크기면 메모리 DC와 DIB를 다시 쓰고, 크기가 바뀔 때만 새로 만든다.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LayeredSurface : ILayeredSurface
{
    private IntPtr _memDc;
    private IntPtr _bitmap;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private PixelSize _size;

    public IntPtr Prepare(PixelSize size)
    {
        if (size == _size && _bits != IntPtr.Zero) return _bits;
        Release();
        _size = size;
        var header = new Win32.BITMAPINFOHEADER
        {
            biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.BITMAPINFOHEADER>(),
            biWidth = size.Width,
            biHeight = -size.Height, // 음수면 위에서 아래로 쌓인다(Avalonia 그림과 같은 순서)
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr screen = Win32.GetDC(IntPtr.Zero);
        try
        {
            _memDc = Win32.CreateCompatibleDC(screen);
            _bitmap = Win32.CreateDIBSection(screen, ref header, Win32.DIB_RGB_COLORS, out _bits, IntPtr.Zero, 0);
            _oldBitmap = Win32.SelectObject(_memDc, _bitmap);
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
        return _bits;
    }

    public void Present(Window window, PixelPoint position)
    {
        if (_bits == IntPtr.Zero) return;
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        var dst = new Win32.POINT { X = position.X, Y = position.Y };
        var size = new Win32.SIZE { Cx = _size.Width, Cy = _size.Height };
        var src = new Win32.POINT();
        var blend = new Win32.BLENDFUNCTION
        {
            BlendOp = Win32.AC_SRC_OVER,
            SourceConstantAlpha = 255,
            AlphaFormat = Win32.AC_SRC_ALPHA,
        };
        IntPtr screen = Win32.GetDC(IntPtr.Zero);
        try
        {
            Win32.UpdateLayeredWindow(hwnd, screen, ref dst, ref size, _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA);
        }
        finally
        {
            Win32.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    /// <summary>포커스를 뺏지 않고(SW_SHOWNA) 보이거나 숨긴다. Avalonia의 Show/Hide는 쓰지 않는다(ILayeredSurface 참고).</summary>
    public void SetVisible(Window window, bool visible)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        Win32.ShowWindow(hwnd, visible ? Win32.SW_SHOWNA : Win32.SW_HIDE);
    }

    public void Dispose() => Release();

    private void Release()
    {
        if (_memDc != IntPtr.Zero)
        {
            Win32.SelectObject(_memDc, _oldBitmap);
            Win32.DeleteDC(_memDc);
        }
        if (_bitmap != IntPtr.Zero) Win32.DeleteObject(_bitmap);
        _memDc = _bitmap = _oldBitmap = _bits = IntPtr.Zero;
        _size = default;
    }
}
