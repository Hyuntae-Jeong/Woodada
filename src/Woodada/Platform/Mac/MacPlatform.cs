using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Woodada.Platform.Mac;

/// <summary>macOS: 독이 보이면 독 위를, 숨겨져 있으면 화면 바닥을 달린다.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacPlatform : IPlatform
{
    /// <summary>
    /// 독을 자동으로 숨겨 두면 화면 가장자리에 몇 포인트만 비워 둔다(독을 불러내는 자리).
    /// 이보다 얇으면 독이 없는 것으로 보고 화면 바닥을 달린다.
    /// </summary>
    private const int MinDockThickness = 12;

    private readonly uint _host = MacNative.mach_host_self();

    public string DataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support");

    public string BarName => "독";
    public string StartupMenuLabel => "로그인 시 실행";

    /// <summary>전체 화면 앱은 따로 데스크톱(Space)을 차지하고, 오버레이는 그곳에 따라가지 않으니 알아서 숨는다.</summary>
    public bool CanDetectFullScreen => false;

    // ───────── 어디서 달릴지 ─────────

    /// <summary>
    /// macOS는 독 위치를 직접 알려 주지 않는다. 화면 전체(frame)와 창을 놓을 수 있는 영역(visibleFrame)의
    /// 차이가 독이 차지한 자리다. 위쪽 차이는 메뉴 막대라서 보지 않는다.
    /// </summary>
    public ScreenBar FindBar(Screen screen)
    {
        var b = screen.Bounds;
        // Avalonia는 모니터 구성이 바뀔 때만 화면 정보를 다시 읽어서 독을 숨기거나 옮긴 것이 WorkingArea에 반영되지 않는다.
        // 그래서 NSScreen에 매번 직접 묻고, 그게 안 되면 Avalonia 값을 쓴다.
        var e = DisplayId(screen) is { } id && ReadInsets(id) is { } live ? live : InsetsOf(screen);
        int middle = Math.Max(0, b.Height - e.Top - e.Bottom);

        if (e.Bottom >= MinDockThickness) return new ScreenBar(new PixelRect(b.X, b.Bottom - e.Bottom, b.Width, e.Bottom), BarEdge.Bottom, b, true);
        if (e.Left >= MinDockThickness) return new ScreenBar(new PixelRect(b.X, b.Y + e.Top, e.Left, middle), BarEdge.Left, b, true);
        if (e.Right >= MinDockThickness) return new ScreenBar(new PixelRect(b.Right - e.Right, b.Y + e.Top, e.Right, middle), BarEdge.Right, b, true);
        return ScreenBar.None(b);
    }

    /// <summary>화면 가장자리에서 창을 놓을 수 없는 두께(포인트). 위는 메뉴 막대, 나머지 한 곳은 독이다.</summary>
    private readonly record struct Insets(int Left, int Top, int Right, int Bottom);

    private static Insets InsetsOf(Screen s)
    {
        var b = s.Bounds;
        var w = s.WorkingArea;
        return new Insets(w.X - b.X, w.Y - b.Y, b.Right - w.Right, b.Bottom - w.Bottom);
    }

    private static readonly IntPtr ScreenNumberKey = Marshal.StringToHGlobalAnsi("NSScreenNumber");

    /// <summary>
    /// 지금 이 순간의 NSScreen frame과 visibleFrame 차이. Cocoa 좌표는 아래쪽이 0이다.
    /// 모니터는 deviceDescription의 NSScreenNumber(= CGDirectDisplayID)로 찾는다.
    /// </summary>
    private static Insets? ReadInsets(uint displayId)
    {
        IntPtr pool = MacNative.objc_autoreleasePoolPush();
        try
        {
            IntPtr screens = MacNative.Send(MacNative.objc_getClass("NSScreen"), Sel("screens"));
            if (screens == IntPtr.Zero) return null;
            IntPtr key = MacNative.Send(MacNative.objc_getClass("NSString"), Sel("stringWithUTF8String:"), ScreenNumberKey);
            nuint count = MacNative.SendReturnNUInt(screens, Sel("count"));
            for (nuint i = 0; i < count; i++)
            {
                IntPtr ns = MacNative.SendIndex(screens, Sel("objectAtIndex:"), i);
                IntPtr number = MacNative.Send(MacNative.Send(ns, Sel("deviceDescription")), Sel("objectForKey:"), key);
                if (number == IntPtr.Zero || MacNative.SendReturnUInt(number, Sel("unsignedIntValue")) != displayId) continue;

                var f = MacNative.SendReturnRect(ns, Sel("frame"));
                var v = MacNative.SendReturnRect(ns, Sel("visibleFrame"));
                return new Insets(
                    Left: (int)Math.Round(v.X - f.X),
                    Top: (int)Math.Round(f.Y + f.Height - (v.Y + v.Height)),
                    Right: (int)Math.Round(f.X + f.Width - (v.X + v.Width)),
                    Bottom: (int)Math.Round(v.Y - f.Y));
            }
            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            MacNative.objc_autoreleasePoolPop(pool);
        }
    }

    private static IntPtr Sel(string name) => MacNative.sel_registerName(name);

    public string ScreenKey(Screen screen) =>
        DisplayId(screen) is { } id ? $"display:{id}" : screen.DisplayName ?? $"{screen.Bounds.X},{screen.Bounds.Y}";

    private static uint? DisplayId(Screen screen) =>
        screen.TryGetPlatformHandle() is { HandleDescriptor: "CGDirectDisplayID" } h ? (uint)h.Handle.ToInt64() : null;

    public MonitorSize? MeasureMonitor(Screen screen)
    {
        try
        {
            if (DisplayId(screen) is not { } id) return null;
            var size = MacNative.CGDisplayScreenSize(id);
            int w = (int)Math.Round(size.Width), h = (int)Math.Round(size.Height);
            return MonitorPitch.Plausible(w, h) ? new MonitorSize(w, h, screen.DisplayName) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public bool IsFullScreenAppActive() => false;

    /// <summary>독은 시스템 화면 모드(라이트/다크)를 따른다. Avalonia가 읽어 둔 OS 테마를 그대로 쓴다.</summary>
    public bool IsBarLight() =>
        Avalonia.Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light;

    // ───────── 오버레이 창 ─────────

    /// <summary>
    /// 클릭은 아래 창으로 넘기고, 모든 데스크톱(Space)에 붙어 있고, 미션 컨트롤·창 순환에서 빠지게 한다.
    /// 투명 창에 그림자가 생기면 고양이 둘레에 테두리가 보이므로 그림자도 끈다.
    /// </summary>
    public void PrepareOverlay(Window window)
    {
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "NSWindow" } h || h.Handle == IntPtr.Zero) return;
        var ns = h.Handle;
        MacNative.SendBool(ns, MacNative.sel_registerName("setIgnoresMouseEvents:"), 1);
        MacNative.SendBool(ns, MacNative.sel_registerName("setHasShadow:"), 0);
        MacNative.SendNUInt(ns, MacNative.sel_registerName("setCollectionBehavior:"),
            MacNative.CanJoinAllSpaces | MacNative.Stationary | MacNative.IgnoresCycle);
    }

    /// <summary>떠 있는 창 단계(floating)는 다른 앱 창에 가려지지 않는다. 따로 다시 올릴 필요가 없다.</summary>
    public void KeepOverlayOnTop(Window window) { }

    // ───────── 지표 ─────────

    public bool TryReadCpuTimes(out ulong idle, out ulong total)
    {
        idle = total = 0;
        var ticks = new uint[4];
        uint count = 4;
        if (MacNative.host_statistics(_host, MacNative.HOST_CPU_LOAD_INFO, ticks, ref count) != MacNative.KERN_SUCCESS) return false;
        idle = ticks[MacNative.CPU_STATE_IDLE];
        total = (ulong)ticks[MacNative.CPU_STATE_USER] + ticks[MacNative.CPU_STATE_SYSTEM] + ticks[MacNative.CPU_STATE_NICE] + idle;
        return true;
    }

    /// <summary>활성 상태 보기의 "사용된 메모리"와 같은 계산: 앱 메모리 + 와이어드 + 압축됨.</summary>
    public double? ReadMemoryPercent()
    {
        uint count = MacNative.VmInfo64Count;
        if (MacNative.host_statistics64(_host, MacNative.HOST_VM_INFO64, out var vm, ref count) != MacNative.KERN_SUCCESS) return null;
        if (MacNative.host_page_size(_host, out var page) != MacNative.KERN_SUCCESS) return null;
        var size = (UIntPtr)sizeof(ulong);
        if (MacNative.sysctlbyname("hw.memsize", out ulong totalBytes, ref size, IntPtr.Zero, UIntPtr.Zero) != 0 || totalBytes == 0) return null;

        double appPages = Math.Max(0, (double)vm.internal_page_count - vm.purgeable_count);
        double usedBytes = (appPages + vm.wire_count + vm.compressor_page_count) * page.ToUInt64();
        return Math.Clamp(100.0 * usedBytes / totalBytes, 0, 100);
    }

    // ───────── 자동 실행 (LaunchAgent) ─────────

    private static string AgentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", $"{AppInfo.BundleId}.plist");

    public bool IsStartupEnabled() => File.Exists(AgentPath);

    /// <summary>DMG에서 바로 띄웠거나(/Volumes) macOS가 격리 실행한 앱(AppTranslocation)은 경로가 곧 사라진다.</summary>
    public bool CanRegisterStartupHere
    {
        get
        {
            string exe = Environment.ProcessPath ?? "";
            return exe.Length > 0
                   && !exe.StartsWith("/Volumes/", StringComparison.Ordinal)
                   && !exe.Contains("/AppTranslocation/", StringComparison.Ordinal);
        }
    }

    /// <summary>로그인하면 launchd가 이 실행 파일을 띄운다. 앱을 옮기면 다시 켜야 새 경로로 바뀐다.</summary>
    public void SetStartup(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(AgentPath);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(AgentPath)!);
        string exe = SecurityElement.Escape(Environment.ProcessPath ?? "") ?? "";
        File.WriteAllText(AgentPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{AppInfo.BundleId}</string>
                <key>ProgramArguments</key>
                <array>
                    <string>{exe}</string>
                </array>
                <key>RunAtLoad</key>
                <true/>
                <key>LimitLoadToSessionType</key>
                <string>Aqua</string>
                <key>ProcessType</key>
                <string>Interactive</string>
            </dict>
            </plist>
            """);
    }

    public void MigrateLegacyStartup() { }
}
