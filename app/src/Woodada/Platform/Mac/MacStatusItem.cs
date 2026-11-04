using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Woodada.Platform.Mac;

/// <summary>
/// 메뉴 막대 항목의 폭. Avalonia는 항목을 정사각형 폭(NSSquareStatusItemLength)으로 만들어서
/// 고양이 옆에 생선 수를 붙인 넓은 그림을 넣으면 오른쪽이 잘린다. 그림 폭을 따라가도록 바꾼다.
/// Avalonia가 NSStatusItem을 내주지 않으니, 앱 창 목록에서 메뉴 막대 항목 창(NSStatusBarWindow)을 찾아
/// 그 창의 statusItem으로 닿는다. 이 앱의 메뉴 막대 항목은 트레이 아이콘 하나뿐이다.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacStatusItem
{
    private const double VariableLength = -1; // NSVariableStatusItemLength
    private const double SquareLength = -2;   // NSSquareStatusItemLength

    /// <summary>폭을 그림에 맞출지(true), 원래 정사각형으로 돌릴지(false). 항목을 못 찾으면 false.</summary>
    public static bool SetFitsImage(bool fits)
    {
        IntPtr pool = MacNative.objc_autoreleasePoolPush();
        try
        {
            IntPtr app = MacNative.Send(MacNative.objc_getClass("NSApplication"), Sel("sharedApplication"));
            IntPtr windows = MacNative.Send(app, Sel("windows"));
            nuint count = MacNative.SendReturnNUInt(windows, Sel("count"));
            IntPtr statusItemSel = Sel("statusItem");
            bool found = false;
            for (nuint i = 0; i < count; i++)
            {
                IntPtr window = MacNative.SendIndex(windows, Sel("objectAtIndex:"), i);
                if (Marshal.PtrToStringAnsi(MacNative.object_getClassName(window)) != "NSStatusBarWindow") continue;
                if (MacNative.SendReturnBool(window, Sel("respondsToSelector:"), statusItemSel) == 0) continue;
                IntPtr item = MacNative.Send(window, statusItemSel);
                if (item == IntPtr.Zero) continue;
                MacNative.SendDouble(item, Sel("setLength:"), fits ? VariableLength : SquareLength);
                found = true;
            }
            return found;
        }
        finally
        {
            MacNative.objc_autoreleasePoolPop(pool);
        }
    }

    private static IntPtr Sel(string name) => MacNative.sel_registerName(name);
}
