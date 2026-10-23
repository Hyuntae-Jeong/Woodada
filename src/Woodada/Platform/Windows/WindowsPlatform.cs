using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Win32;

namespace Woodada.Platform.Windows;

/// <summary>Windows: 작업표시줄 위를 달린다.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPlatform : IPlatform
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string DataRoot => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public string BarName => "작업표시줄";
    public string StartupMenuLabel => "Windows 시작 시 실행";
    public bool CanDetectFullScreen => true;

    // ───────── 어디서 달릴지 ─────────

    /// <summary>
    /// 자동 숨김 중에는 작업표시줄 창이 화면 밖으로 밀려나 있으므로, 실제 창 위치를 쓰면
    /// 작업표시줄이 내려갈 때 러너도 같이 내려간다.
    /// </summary>
    public ScreenBar FindBar(Screen screen)
    {
        var monitor = MonitorHandle(screen);
        foreach (var hwnd in EnumerateTaskbars())
        {
            if (Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST) != monitor) continue;
            if (!Win32.GetWindowRect(hwnd, out var r)) continue;
            var rect = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            return new ScreenBar(rect, EdgeOf(rect, screen.Bounds), screen.Bounds, true);
        }
        // "모든 디스플레이에 작업 표시줄 표시"가 꺼져 있으면 보조 모니터엔 작업표시줄이 없다.
        return ScreenBar.None(screen.Bounds);
    }

    /// <summary>주 모니터는 Shell_TrayWnd, 나머지 모니터는 Shell_SecondaryTrayWnd 창이 작업표시줄이다.</summary>
    private static IEnumerable<IntPtr> EnumerateTaskbars()
    {
        var primary = Win32.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) yield return primary;

        IntPtr prev = IntPtr.Zero;
        while (true)
        {
            prev = Win32.FindWindowEx(IntPtr.Zero, prev, "Shell_SecondaryTrayWnd", null);
            if (prev == IntPtr.Zero) yield break;
            yield return prev;
        }
    }

    private static BarEdge EdgeOf(PixelRect bar, PixelRect screen)
    {
        var c = bar.Center;
        var sc = screen.Center;
        if (bar.Width >= bar.Height) return c.Y >= sc.Y ? BarEdge.Bottom : BarEdge.Top;
        return c.X >= sc.X ? BarEdge.Right : BarEdge.Left;
    }

    private static IntPtr MonitorHandle(Screen screen) => screen.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    /// <summary>\\.\DISPLAY1 같은 장치 이름. 예전 버전 설정 파일에 이 값이 들어 있다.</summary>
    public string ScreenKey(Screen screen) => DeviceName(screen) ?? $"{screen.Bounds.X},{screen.Bounds.Y}";

    private static string? DeviceName(Screen screen)
    {
        var info = new Win32.MONITORINFOEX { cbSize = Marshal.SizeOf<Win32.MONITORINFOEX>() };
        return Win32.GetMonitorInfo(MonitorHandle(screen), ref info) ? info.szDevice : null;
    }

    public bool IsFullScreenAppActive() =>
        Win32.SHQueryUserNotificationState(out int state) == 0 &&
        state is Win32.QUNS_BUSY or Win32.QUNS_RUNNING_D3D_FULL_SCREEN or Win32.QUNS_PRESENTATION_MODE;

    // ───────── 오버레이 창 ─────────

    /// <summary>클릭을 통과시키고(TRANSPARENT), 포커스를 뺏지 않고(NOACTIVATE), Alt+Tab에 안 뜨게(TOOLWINDOW) 한다.</summary>
    public void PrepareOverlay(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        long ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        ex |= Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
    }

    /// <summary>작업표시줄도 topmost라 클릭하면 우리 창 위로 올라온다.</summary>
    public void KeepOverlayOnTop(Window window)
    {
        if (window.TryGetPlatformHandle()?.Handle is not { } hwnd || hwnd == IntPtr.Zero) return;
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_NOOWNERZORDER);
    }

    // ───────── 지표 ─────────

    /// <summary>
    /// PerformanceCounter는 한글 Windows에서 카운터 이름이 현지화돼 깨지기 쉬워서 GetSystemTimes를 직접 쓴다.
    /// kernel 시간에 idle이 포함돼 있으므로 전체 = kernel + user.
    /// </summary>
    public bool TryReadCpuTimes(out ulong idle, out ulong total)
    {
        idle = total = 0;
        if (!Win32.GetSystemTimes(out long i, out long kernel, out long user)) return false;
        idle = (ulong)i;
        total = (ulong)(kernel + user);
        return true;
    }

    public double? ReadMemoryPercent()
    {
        var mem = new Win32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
        if (!Win32.GlobalMemoryStatusEx(ref mem)) return null;
        // dwMemoryLoad는 정수 %라서 총량/가용량으로 소수점까지 계산한다(속도가 계단식으로 바뀌지 않게).
        return mem.ullTotalPhys > 0
            ? 100.0 * (mem.ullTotalPhys - mem.ullAvailPhys) / mem.ullTotalPhys
            : mem.dwMemoryLoad;
    }

    // ───────── 자동 실행 ─────────

    public bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppInfo.StartupName) is string;
    }

    public void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled) key.SetValue(AppInfo.StartupName, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(AppInfo.StartupName, throwOnMissingValue: false);
    }

    /// <summary>
    /// 옛 이름으로 "시작 시 실행"이 등록돼 있으면 새 이름·현재 exe 경로로 옮긴다.
    /// 그대로 두면 로그인할 때 없는 exe를 실행하려다 실패한다.
    /// </summary>
    public void MigrateLegacyStartup()
    {
        if (AppInfo.IsDev) return; // 진짜 앱의 시작 프로그램 등록은 진짜 앱만 정리한다
        string[] legacyNames = { "Udada", "TaskbarRunner" };
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;
        bool hadLegacy = false;
        foreach (var name in legacyNames)
        {
            if (key.GetValue(name) is null) continue;
            key.DeleteValue(name, throwOnMissingValue: false);
            hadLegacy = true;
        }
        if (hadLegacy && key.GetValue(AppInfo.StartupName) is null)
            key.SetValue(AppInfo.StartupName, $"\"{Environment.ProcessPath}\"");
    }

    // ───────── 모니터 실제 크기 (EDID) ─────────

    public MonitorSize? MeasureMonitor(Screen screen)
    {
        try
        {
            if (DeviceName(screen) is not { } device) return null;
            if (ReadEdid(device) is not { } edid || ParseSize(edid) is not { } size) return null;
            return new MonitorSize(size.WidthMm, size.HeightMm, ParseName(edid));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// \\.\DISPLAY1 → 모니터 장치 경로(\\?\DISPLAY#VSCE635#5&amp;6d461c1&amp;0&amp;UID4353#{…}) →
    /// HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\VSCE635\5&amp;6d461c1&amp;0&amp;UID4353\Device Parameters\EDID
    /// </summary>
    private static byte[]? ReadEdid(string deviceName)
    {
        var dd = new Win32.DISPLAY_DEVICE { cb = Marshal.SizeOf<Win32.DISPLAY_DEVICE>() };
        if (!Win32.EnumDisplayDevices(deviceName, 0, ref dd, Win32.EDD_GET_DEVICE_INTERFACE_NAME)) return null;

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
        if (!MonitorPitch.Plausible(w, h))
        {
            w = e[21] * 10;
            h = e[22] * 10;
        }
        return MonitorPitch.Plausible(w, h) ? (w, h) : null;
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
