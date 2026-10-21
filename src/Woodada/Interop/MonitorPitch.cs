using System.Text;
using Microsoft.Win32;

namespace Woodada.Interop;

/// <summary>화면 1px의 실제 길이. 고양이가 달린 픽셀을 cm·km로 바꾸는 데 쓴다.</summary>
/// <param name="MillimetersPerPixel">지금 해상도 기준 1px의 가로 길이(mm).</param>
/// <param name="Measured">모니터가 알려준 실제 크기로 계산했으면 true, 화면 배율로 추정했으면 false.</param>
/// <param name="MonitorName">EDID에 적힌 모델명. 없으면 null.</param>
/// <param name="WidthMm">패널 가로 길이(mm). 추정이면 0.</param>
/// <param name="HeightMm">패널 세로 길이(mm). 추정이면 0.</param>
public readonly record struct PixelPitch(double MillimetersPerPixel, bool Measured, string? MonitorName, int WidthMm, int HeightMm);

/// <summary>
/// 모니터가 EDID로 알려주는 패널 실제 크기(mm)를 지금 해상도로 나눠 1px 길이를 구한다.
/// TV·프로젝터·원격 데스크톱처럼 크기를 알 수 없으면 Windows 화면 배율(DPI)로 추정한다.
/// </summary>
public static class MonitorPitch
{
    private static readonly Dictionary<(string Device, int Width, int Height), PixelPitch> Cache = new();

    /// <summary>해상도·배치가 바뀌면 불러서 다시 읽게 한다.</summary>
    public static void Invalidate() => Cache.Clear();

    public static PixelPitch For(Screen screen)
    {
        var key = (screen.DeviceName, screen.Bounds.Width, screen.Bounds.Height);
        if (!Cache.TryGetValue(key, out var pitch))
        {
            pitch = Measure(screen) ?? Estimate(screen);
            Cache[key] = pitch;
        }
        return pitch;
    }

    private static PixelPitch? Measure(Screen screen)
    {
        try
        {
            if (ReadEdid(screen.DeviceName) is not { } edid || ParseSize(edid) is not { } size) return null;
            var (wMm, hMm) = size;

            // 세로로 돌려 놓은 모니터는 EDID(가로 기준)와 화면의 가로·세로가 뒤바뀐다.
            var b = screen.Bounds;
            if ((b.Width > b.Height) != (wMm > hMm)) (wMm, hMm) = (hMm, wMm);

            // 고유 해상도보다 낮춰 쓰면 1px이 커지므로, 패널 크기를 "지금" 해상도로 나눈다.
            return new PixelPitch((double)wMm / b.Width, true, ParseName(edid), wMm, hMm);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Windows는 화면 배율을 대략 실제 픽셀 밀도에 맞춰 고르므로, 배율 DPI를 실제 DPI로 본다.</summary>
    private static PixelPitch Estimate(Screen screen)
    {
        uint dpi = 96;
        try
        {
            var b = screen.Bounds;
            var monitor = NativeMethods.MonitorFromPoint(
                new NativeMethods.POINT(b.Left + b.Width / 2, b.Top + b.Height / 2), NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint x, out _) == 0 && x > 0) dpi = x;
        }
        catch (Exception)
        {
            // 96 DPI로 둔다.
        }
        return new PixelPitch(25.4 / dpi, false, null, 0, 0);
    }

    /// <summary>
    /// \\.\DISPLAY1 → 모니터 장치 경로(\\?\DISPLAY#VSCE635#5&amp;6d461c1&amp;0&amp;UID4353#{…}) →
    /// HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\VSCE635\5&amp;6d461c1&amp;0&amp;UID4353\Device Parameters\EDID
    /// </summary>
    private static byte[]? ReadEdid(string deviceName)
    {
        var dd = new NativeMethods.DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
        if (!NativeMethods.EnumDisplayDevices(deviceName, 0, ref dd, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME)) return null;

        var parts = dd.DeviceID?.Split('#');
        if (parts is not { Length: >= 3 }) return null;
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
        return key?.GetValue("EDID") as byte[];
    }

    private static readonly byte[] EdidHeader = { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 };

    /// <summary>
    /// 첫 번째 상세 타이밍 블록(54~71)에 mm 단위 크기가 있다. 없으면 기본 블록(21·22)의 cm 단위 크기를 쓴다.
    /// 값이 없거나 말이 안 되면(TV가 0이나 엉터리를 보내는 경우) null.
    /// </summary>
    private static (int WidthMm, int HeightMm)? ParseSize(byte[] e)
    {
        if (e.Length < 128 || !e.AsSpan(0, 8).SequenceEqual(EdidHeader)) return null;

        int w = 0, h = 0;
        bool isTiming = e[54] != 0 || e[55] != 0; // 픽셀 클럭이 0이면 타이밍이 아닌 설명 블록
        if (isTiming)
        {
            w = e[66] | (e[68] >> 4) << 8;
            h = e[67] | (e[68] & 0x0F) << 8;
        }
        if (!Plausible(w, h))
        {
            w = e[21] * 10;
            h = e[22] * 10;
        }
        return Plausible(w, h) ? (w, h) : null;

        // 10cm 미만이나 5m 넘는 화면은 잘못 적힌 값으로 본다.
        static bool Plausible(int w, int h) => w is >= 100 and <= 5000 && h is >= 50 and <= 5000;
    }

    /// <summary>설명 블록 중 태그 0xFC가 모델명이다.</summary>
    private static string? ParseName(byte[] e)
    {
        for (int d = 54; d + 18 <= Math.Min(e.Length, 126); d += 18)
        {
            if (e[d] != 0 || e[d + 1] != 0 || e[d + 3] != 0xFC) continue;
            string name = Encoding.ASCII.GetString(e, d + 5, 13).Split('\n')[0].Trim();
            return name.Length > 0 ? name : null;
        }
        return null;
    }
}
