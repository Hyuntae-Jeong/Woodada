using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Woodada.Interop;

namespace Woodada.Rendering;

/// <summary>
/// UpdateLayeredWindow에 넘길 32bpp DIB와 그 메모리에 직접 그리는 GDI+ Graphics.
/// 매 프레임 비트맵을 새로 만들지 않도록 크기가 바뀔 때만 다시 만든다.
/// </summary>
internal sealed class LayeredSurface : IDisposable
{
    private IntPtr _memDc;
    private IntPtr _hBitmap;
    private IntPtr _oldBitmap;
    private Bitmap? _bitmap;
    private Graphics? _graphics;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public IntPtr MemoryDC => _memDc;
    public Graphics Graphics => _graphics ?? throw new InvalidOperationException("EnsureSize를 먼저 호출해야 한다.");

    public void EnsureSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_graphics is not null && width == Width && height == Height) return;
        Release();
        Create(width, height);
    }

    private void Create(int width, int height)
    {
        var bi = new NativeMethods.BITMAPINFO();
        bi.bmiHeader.biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>();
        bi.bmiHeader.biWidth = width;
        bi.bmiHeader.biHeight = -height; // 음수 = top-down. GDI+ 비트맵과 행 순서를 맞춘다.
        bi.bmiHeader.biPlanes = 1;
        bi.bmiHeader.biBitCount = 32;
        bi.bmiHeader.biCompression = NativeMethods.BI_RGB;

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            _memDc = NativeMethods.CreateCompatibleDC(screenDc);
            _hBitmap = NativeMethods.CreateDIBSection(screenDc, ref bi, NativeMethods.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            if (_hBitmap == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection 실패");
            _oldBitmap = NativeMethods.SelectObject(_memDc, _hBitmap);

            // DIB 메모리를 그대로 쓰는 PArgb 비트맵: UpdateLayeredWindow가 요구하는 premultiplied 알파로 바로 그려진다.
            _bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, bits);
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }

        _graphics = Graphics.FromImage(_bitmap);
        _graphics.SmoothingMode = SmoothingMode.AntiAlias;
        _graphics.CompositingQuality = CompositingQuality.HighSpeed;
        Width = width;
        Height = height;
    }

    public void Flush()
    {
        _graphics?.Flush(FlushIntention.Sync);
        NativeMethods.GdiFlush();
    }

    private void Release()
    {
        _graphics?.Dispose();
        _graphics = null;
        _bitmap?.Dispose();
        _bitmap = null;
        if (_memDc != IntPtr.Zero)
        {
            NativeMethods.SelectObject(_memDc, _oldBitmap);
            NativeMethods.DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
        }
        if (_hBitmap != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }
        Width = Height = 0;
    }

    public void Dispose() => Release();
}
