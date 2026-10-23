using Avalonia;
using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.Core;

/// <summary>설정 + 스프라이트 + 상태를 묶은 실행 중인 러너 한 마리.</summary>
public sealed class RunnerInstance
{
    private bool _placed;
    private double _smoothedUsage = double.NaN;

    public RunnerInstance(RunnerConfig config, IRunnerSprite sprite)
    {
        Config = config;
        Sprite = sprite;
        // 러너끼리 같은 자리에서 겹쳐 출발하지 않게 방향을 섞는다.
        State.Direction = Random.Shared.Next(2) == 0 ? 1 : -1;
    }

    public RunnerConfig Config { get; set; }
    public IRunnerSprite Sprite { get; set; }
    public RunnerState State { get; } = new();
    public double Usage => double.IsNaN(_smoothedUsage) ? 0 : _smoothedUsage;

    public Size BodySize => Sprite.Measure(Config.Size);

    /// <returns>이번에 실제로 달린 거리(px). 다른 모니터로 옮겨져 새 자리에서 출발한 순간이동은 세지 않는다.</returns>
    public double Update(double dt, double rawUsage, Runway runway)
    {
        // 샘플이 0.5초마다 계단식으로 바뀌므로 지수이동평균으로 부드럽게 따라가게 한다(시정수 ≈ 0.6초).
        _smoothedUsage = double.IsNaN(_smoothedUsage)
            ? rawUsage
            : _smoothedUsage + (rawUsage - _smoothedUsage) * Math.Min(1, dt / 0.6);

        var size = BodySize;
        var track = runway.TrackFor(size.Width);
        // 처음이거나, 다른 모니터로 옮겨져 트랙 밖에 있으면 새 트랙 안 임의 위치에서 출발.
        if (!_placed || State.X < track.MinX - size.Width || State.X > track.MaxX + size.Width)
        {
            State.X = track.MinX + Random.Shared.NextDouble() * Math.Max(0, track.Width);
            _placed = true;
        }
        return State.Advance(dt, _smoothedUsage, track, SpeedProfile.ForBodyWidth(size.Width));
    }
}
