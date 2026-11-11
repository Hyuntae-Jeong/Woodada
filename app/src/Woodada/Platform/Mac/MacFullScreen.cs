using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Woodada.Platform.Mac;

/// <summary>
/// 고양이가 달리는 모니터가 전체 화면 앱에 덮였는지. 오버레이는 모든 데스크톱(Space)에 붙어 있어서 전체 화면 앱 위에도 보이므로 직접 알아내 숨긴다.
/// 다른 모니터만 전체 화면이면 숨기지 않는다(맥북 화면에 유튜브를 전체 화면으로 틀어도 외부 모니터의 고양이는 달린다).
/// 화면에 떠 있는 창 목록(창 크기·층·주인만 읽어 화면 기록 권한이 필요 없다)에서 두 가지를 본다.
/// 1) 그 모니터에서 맨 앞 앱의 창이 모니터 폭 전체와 바닥까지 채운다. 노치 맥북은 전체 화면 창이 노치 아래(메뉴 막대 높이)부터 시작한다.
/// 2) 그 모니터에 메뉴 막대 아이콘(상태 막대 층 창)이 하나도 안 보인다. 전체 화면에서는 메뉴 막대가 숨는다.
/// 2)가 있어야 메뉴 막대 바로 아래까지 키운 보통 창(독을 숨겼을 때)과 구별된다.
/// 초록 버튼 전체 화면뿐 아니라 메뉴 막대를 숨기고 화면을 덮는 게임도 같은 모양이라 함께 잡힌다.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacFullScreen
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint OnScreenOnly = 1 << 0;            // kCGWindowListOptionOnScreenOnly
    private const uint ExcludeDesktopElements = 1 << 4;  // kCGWindowListExcludeDesktopElements
    private const int NormalLayer = 0;                   // 보통 앱 창
    private const int StatusLayer = 25;                  // kCGStatusWindowLevel: 메뉴 막대 아이콘
    private const nint SInt32Type = 3;                   // kCFNumberSInt32Type

    /// <summary>창 위 끝이 모니터 위에서 이만큼 안이면 화면을 다 채운 것으로 본다. 노치 맥북 메뉴 막대는 33~38pt.</summary>
    private const double MenuBarAllowance = 50;

    /// <summary>모니터 가장자리 비교 여유(소수점 좌표).</summary>
    private const double Slack = 1;

    private readonly record struct WindowInfo(int Layer, int Pid, MacNative.CGRect Bounds);

    private static IntPtr _layerKey, _boundsKey, _pidKey;

    /// <param name="displayId">고양이가 달리는 모니터. 모르면 모든 모니터를 본다.</param>
    public static bool IsActive(uint? displayId)
    {
        if (!LoadKeys()) return false;
        var windows = ReadWindows();
        int self = Environment.ProcessId;

        List<MacNative.CGRect> displays = displayId is { } id ? [CGDisplayBounds(id)] : ActiveDisplays();
        foreach (var display in displays)
        {
            if (windows.Any(w => w.Layer == StatusLayer && Intersects(w.Bounds, display))) continue; // 메뉴 막대가 보인다
            // 이 모니터의 맨 앞 앱: 가운데가 이 모니터에 있는 보통 층 창 중 가장 앞 창의 주인. 우리 설정 창은 뺀다.
            // 전체 화면 창은 모니터마다 따로라서, 다른 모니터에서 다른 앱을 눌러도 이 모니터의 전체 화면 앱은 그대로다.
            int front = windows.FirstOrDefault(w => w.Layer == NormalLayer && w.Pid != self && CenterIn(w.Bounds, display)).Pid;
            if (front == 0) continue;
            if (windows.Any(w => w.Pid == front && w.Layer == NormalLayer && Fills(w.Bounds, display))) return true;
        }
        return false;
    }

    private static bool Fills(MacNative.CGRect w, MacNative.CGRect d) =>
        w.X <= d.X + Slack && w.X + w.Width >= d.X + d.Width - Slack &&
        w.Y + w.Height >= d.Y + d.Height - Slack && w.Y <= d.Y + MenuBarAllowance;

    private static bool CenterIn(MacNative.CGRect w, MacNative.CGRect d)
    {
        double cx = w.X + w.Width / 2, cy = w.Y + w.Height / 2;
        return cx >= d.X && cx < d.X + d.Width && cy >= d.Y && cy < d.Y + d.Height;
    }

    private static bool Intersects(MacNative.CGRect a, MacNative.CGRect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static List<WindowInfo> ReadWindows()
    {
        var result = new List<WindowInfo>();
        IntPtr list = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        if (list == IntPtr.Zero) return result;
        try
        {
            nint count = CFArrayGetCount(list);
            for (nint i = 0; i < count; i++)
            {
                IntPtr dict = CFArrayGetValueAtIndex(list, i);
                if (!ReadInt(dict, _layerKey, out int layer) || !ReadInt(dict, _pidKey, out int pid)) continue;
                IntPtr bounds = CFDictionaryGetValue(dict, _boundsKey);
                if (bounds == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(bounds, out var rect)) continue;
                result.Add(new WindowInfo(layer, pid, rect));
            }
        }
        finally
        {
            CFRelease(list);
        }
        return result;
    }

    private static bool ReadInt(IntPtr dict, IntPtr key, out int value)
    {
        value = 0;
        IntPtr number = CFDictionaryGetValue(dict, key);
        return number != IntPtr.Zero && CFNumberGetValue(number, SInt32Type, out value);
    }

    private static List<MacNative.CGRect> ActiveDisplays()
    {
        var ids = new uint[16];
        if (CGGetActiveDisplayList((uint)ids.Length, ids, out uint count) != 0) return [];
        return ids.Take((int)count).Select(CGDisplayBounds).ToList();
    }

    /// <summary>창 정보 사전의 키(kCGWindowLayer 등)는 CoreGraphics가 내보내는 CFString 상수라 주소로 읽어 온다.</summary>
    private static bool LoadKeys()
    {
        if (_pidKey != IntPtr.Zero) return true;
        if (!NativeLibrary.TryLoad(CoreGraphics, out var lib)) return false;
        IntPtr Read(string name) => NativeLibrary.TryGetExport(lib, name, out var p) ? Marshal.ReadIntPtr(p) : IntPtr.Zero;
        _layerKey = Read("kCGWindowLayer");
        _boundsKey = Read("kCGWindowBounds");
        var pid = Read("kCGWindowOwnerPID");
        if (_layerKey == IntPtr.Zero || _boundsKey == IntPtr.Zero || pid == IntPtr.Zero) return false;
        _pidKey = pid;
        return true;
    }

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out MacNative.CGRect rect);

    [DllImport(CoreGraphics)]
    private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint count);

    [DllImport(CoreGraphics)]
    private static extern MacNative.CGRect CGDisplayBounds(uint display);

    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFNumberGetValue(IntPtr number, nint type, out int value);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
