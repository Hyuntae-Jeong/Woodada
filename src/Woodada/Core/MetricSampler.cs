using System.Runtime.InteropServices;
using Woodada.Interop;

namespace Woodada.Core;

public enum MetricKind { Cpu, Memory }

/// <summary>
/// CPU·메모리 사용률 수집. PerformanceCounter는 한글 Windows에서 카운터 이름이 현지화돼 깨지기 쉬워서
/// GetSystemTimes / GlobalMemoryStatusEx를 직접 쓴다.
/// </summary>
public sealed class MetricSampler
{
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _primed;

    public double Cpu { get; private set; }
    public double Memory { get; private set; }

    public MetricSampler() => Sample();

    public double Read(MetricKind kind) => kind switch
    {
        MetricKind.Cpu => Cpu,
        MetricKind.Memory => Memory,
        _ => 0,
    };

    public void Sample()
    {
        if (NativeMethods.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            if (_primed)
            {
                // kernel 시간에 idle이 포함돼 있으므로 전체 = kernel + user, 사용 = 전체 - idle.
                long total = (kernel - _prevKernel) + (user - _prevUser);
                long idleDelta = idle - _prevIdle;
                if (total > 0) Cpu = Math.Clamp(100.0 * (total - idleDelta) / total, 0, 100);
            }
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _primed = true;
        }

        var mem = new NativeMethods.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>() };
        if (NativeMethods.GlobalMemoryStatusEx(ref mem))
        {
            // dwMemoryLoad는 정수 %라서 총량/가용량으로 소수점까지 계산한다(속도가 계단식으로 바뀌지 않게).
            Memory = mem.ullTotalPhys > 0
                ? 100.0 * (mem.ullTotalPhys - mem.ullAvailPhys) / mem.ullTotalPhys
                : mem.dwMemoryLoad;
        }
    }
}
