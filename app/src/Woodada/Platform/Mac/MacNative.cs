using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Woodada.Platform.Mac;

[SupportedOSPlatform("macos")]
internal static class MacNative
{
    private const string LibSystem = "/usr/lib/libSystem.dylib";
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    // ---- mach: CPU·메모리 ----
    public const int KERN_SUCCESS = 0;
    public const int HOST_CPU_LOAD_INFO = 3;
    public const int HOST_VM_INFO64 = 4;
    public const int CPU_STATE_USER = 0, CPU_STATE_SYSTEM = 1, CPU_STATE_IDLE = 2, CPU_STATE_NICE = 3;

    [DllImport(LibSystem)]
    public static extern uint mach_host_self();

    /// <summary>HOST_CPU_LOAD_INFO: info는 natural_t[4](user, system, idle, nice 틱).</summary>
    [DllImport(LibSystem)]
    public static extern int host_statistics(uint host, int flavor, [Out] uint[] info, ref uint count);

    [DllImport(LibSystem)]
    public static extern int host_statistics64(uint host, int flavor, out VmStatistics64 info, ref uint count);

    [DllImport(LibSystem)]
    public static extern int host_page_size(uint host, out UIntPtr pageSize);

    [DllImport(LibSystem, CharSet = CharSet.Ansi)]
    public static extern int sysctlbyname(string name, out ulong value, ref UIntPtr size, IntPtr newValue, UIntPtr newSize);

    /// <summary>&lt;mach/vm_statistics.h&gt;의 vm_statistics64. 순서와 크기가 C 구조체와 똑같아야 한다.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VmStatistics64
    {
        public uint free_count;
        public uint active_count;
        public uint inactive_count;
        public uint wire_count;
        public ulong zero_fill_count;
        public ulong reactivations;
        public ulong pageins;
        public ulong pageouts;
        public ulong faults;
        public ulong cow_faults;
        public ulong lookups;
        public ulong hits;
        public ulong purges;
        public uint purgeable_count;
        public uint speculative_count;
        public ulong decompressions;
        public ulong compressions;
        public ulong swapins;
        public ulong swapouts;
        public uint compressor_page_count;
        public uint throttled_count;
        public uint external_page_count;
        public uint internal_page_count;
        public ulong total_uncompressed_pages_in_compressor;
    }

    /// <summary>HOST_VM_INFO64_COUNT = sizeof(vm_statistics64) / sizeof(integer_t).</summary>
    public static uint VmInfo64Count => (uint)(Marshal.SizeOf<VmStatistics64>() / sizeof(int));

    // ---- CoreGraphics: 모니터 실제 크기 ----
    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width, Height;
    }

    /// <summary>패널 크기(mm). 모르면 0을 돌려준다.</summary>
    [DllImport(CoreGraphics)]
    public static extern CGSize CGDisplayScreenSize(uint display);

    // ---- Objective-C: NSWindow 설정 ----
    [DllImport(LibObjC, CharSet = CharSet.Ansi)]
    public static extern IntPtr sel_registerName(string name);

    // objc_msgSend는 부르는 메서드의 인자 모양대로 따로 선언해야 한다. BOOL은 1바이트로 넘긴다.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendBool(IntPtr receiver, IntPtr selector, byte value);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendNUInt(IntPtr receiver, IntPtr selector, nuint value);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern void SendNInt(IntPtr receiver, IntPtr selector, nint value);

    // NSWindowCollectionBehavior
    public const nuint CanJoinAllSpaces = 1 << 0;
    public const nuint Stationary = 1 << 4;
    public const nuint IgnoresCycle = 1 << 6;

    // ---- Objective-C: NSScreen 읽기 ----
    [DllImport(LibObjC, CharSet = CharSet.Ansi)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    public static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(LibObjC)]
    public static extern void objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint index);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern nuint SendReturnNUInt(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    public static extern uint SendReturnUInt(IntPtr receiver, IntPtr selector);

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public double X, Y, Width, Height;
    }

    // NSRect(32바이트)를 돌려주는 메서드는 인텔 맥에서는 objc_msgSend_stret으로, 애플 실리콘에서는 objc_msgSend로 불러야 한다.
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern CGRect SendReturnRectArm(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend_stret")]
    private static extern CGRect SendReturnRectIntel(IntPtr receiver, IntPtr selector);

    public static CGRect SendReturnRect(IntPtr receiver, IntPtr selector) =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? SendReturnRectArm(receiver, selector)
            : SendReturnRectIntel(receiver, selector);
}
