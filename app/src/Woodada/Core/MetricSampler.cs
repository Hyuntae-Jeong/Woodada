using Woodada.Platform;

namespace Woodada.Core;

public enum MetricKind { Cpu, Memory }

/// <summary>CPU·메모리 사용률 수집. 값을 읽는 방법만 OS마다 다르고(IPlatform), 계산은 여기서 한다.</summary>
public sealed class MetricSampler
{
    private readonly IPlatform _platform = PlatformServices.Current;
    private ulong _prevIdle, _prevTotal;
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
        if (_platform.TryReadCpuTimes(out ulong idle, out ulong total))
        {
            // 누적 카운터가 한 바퀴 돌아 줄어들었으면(macOS는 32비트 틱) 이번 값은 버리고 다시 잰다.
            if (_primed && total > _prevTotal && idle >= _prevIdle)
            {
                double busy = (total - _prevTotal) - (double)(idle - _prevIdle);
                Cpu = Math.Clamp(100.0 * busy / (total - _prevTotal), 0, 100);
            }
            _prevIdle = idle;
            _prevTotal = total;
            _primed = true;
        }

        if (_platform.ReadMemoryPercent() is { } memory) Memory = memory;
    }
}
