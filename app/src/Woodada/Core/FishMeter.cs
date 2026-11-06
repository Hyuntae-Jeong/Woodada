namespace Woodada.Core;

/// <summary>
/// 생선으로 칠 거리. 고양이가 한 마리라도 화면에 떠 있는 동안, 켜고 끈 것과 상관없이
/// CPU 고양이와 메모리 고양이가 둘 다 달렸다고 치고 몸 길이를 센다.
/// 그래서 한 마리만 켜도 두 마리를 켠 것과 생선이 똑같이 쌓인다.
/// </summary>
public sealed class FishMeter
{
    private static readonly SpeedProfile PerBody = SpeedProfile.ForBodyWidth(1);
    private static readonly MetricKind[] Metrics = Enum.GetValues<MetricKind>();

    // 실제 러너와 똑같이 부드럽게 따라가야 켜 둔 고양이와 같은 만큼 쌓인다.
    private readonly double[] _smoothed = Metrics.Select(_ => double.NaN).ToArray();

    /// <returns>이번 프레임에 쌓을 몸 길이.</returns>
    public double Advance(double dt, MetricSampler sampler)
    {
        double bodyLengths = 0;
        for (int i = 0; i < Metrics.Length; i++)
        {
            _smoothed[i] = MetricSampler.Smooth(_smoothed[i], sampler.Read(Metrics[i]), dt);
            bodyLengths += PerBody.SpeedAt(_smoothed[i]) * dt;
        }
        return bodyLengths;
    }
}
