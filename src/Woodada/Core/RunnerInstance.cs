using Avalonia;
using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.Core;

/// <summary>지나온 길의 한 지점. 잔상은 그때의 자리·방향·다리 자세를 그대로 그린다.</summary>
/// <param name="Odometer">이 러너가 지금까지 달린 거리. 잔상 간격을 시간이 아니라 거리로 재는 데 쓴다.</param>
public readonly record struct TrailSample(double Odometer, double X, int Direction, double GaitPhase);

/// <summary>설정 + 스프라이트 + 상태를 묶은 실행 중인 러너 한 마리.</summary>
public sealed class RunnerInstance
{
    /// <summary>
    /// 지나온 길을 몸 길이 몇 개만큼 남길지. 플래시 잔상 꼬리(몸 길이 0.9개)와 줄줄이 새끼 줄(합류 중일 때 최대 약 5개)보다 넉넉하다.
    /// 프레임 수로 세면 느리게 걸을 때(초당 몸 길이 0.4개) 2초에 몸 길이 1개도 안 돼서 새끼들이 따라올 길이 모자란다.
    /// </summary>
    private const double TrailBodyLengths = 8;
    /// <summary>아주 느리거나 높은 fps에서도 기록이 끝없이 늘지 않게 하는 상한.</summary>
    private const int MaxTrailSamples = 2048;

    private readonly List<TrailSample> _trail = new();
    private bool _placed;
    private double _smoothedUsage = double.NaN;
    private double _odometer;

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

    /// <summary>
    /// 그릴 키. 보통은 설정의 크기(화면 단위)이고, 설정 창 미리보기처럼 다른 단위로 그릴 때만 HeightOverride로 정한다.
    /// </summary>
    public double Height => HeightOverride ?? Config.Size;

    public double? HeightOverride { get; set; }

    public Size BodySize => Sprite.Measure(Height);

    public RunnerSkin Skin => RunnerSkins.Parse(Config.Skin);

    /// <summary>스킨 효과(둥실 흔들림, 불꽃 일렁임)의 시작 위상. 두 마리가 같은 스킨이어도 똑같이 움직이지 않게 한다.</summary>
    public double EffectPhase { get; } = Random.Shared.NextDouble() * 2 * Math.PI;

    /// <summary>글리치 스킨이 언제 터졌고 지금 어떤 모양인지.</summary>
    public GlitchState Glitch { get; } = new();

    /// <summary>얼음 스킨이 바닥에 남긴 서리와 흩날리는 눈가루.</summary>
    public IceState Ice { get; } = new();

    /// <summary>줄줄이 스킨이 데리고 다니는 새끼들.</summary>
    public KittenState Kittens { get; } = new();

    /// <summary>지금까지 달린 거리. 지나온 길 기록(Behind)과 같은 기준이다.</summary>
    public double Odometer => _odometer;

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
            _trail.Clear(); // 순간이동한 자리 사이에 잔상이 이어지면 안 된다
            // 이미 그 방향으로 달려온 것처럼 뒤쪽 길을 깔아 둔다. 없으면 새끼들이 출발점에 멈춰 서서 길이 생기기를 기다린다.
            double back = TrailBodyLengths * size.Width;
            _trail.Add(new TrailSample(_odometer - back, State.X - State.Direction * back, State.Direction, State.GaitPhase));
            Ice.Clear();
            Kittens.Reset();
        }
        double moved = State.Advance(dt, _smoothedUsage, track, SpeedProfile.ForBodyWidth(size.Width));

        _odometer += moved;
        _trail.Add(new TrailSample(_odometer, State.X, State.Direction, State.GaitPhase));
        // 맨 앞 하나는 keep보다 뒤에 남겨야 keep 바로 그 자리도 사이를 이어 찾을 수 있다.
        double keep = _odometer - TrailBodyLengths * size.Width;
        while (_trail.Count > 2 && (_trail[1].Odometer <= keep || _trail.Count > MaxTrailSamples)) _trail.RemoveAt(0);
        return moved;
    }

    /// <summary>
    /// 지금 자리에서 지나온 길을 따라 distance만큼 뒤의 지점. 기록 사이는 선형으로 이어서
    /// 프레임 간격보다 촘촘한 잔상도 부드럽게 놓인다. 기록이 없으면 null.
    /// </summary>
    public TrailSample? Behind(double distance)
    {
        if (_trail.Count == 0) return null;
        double target = _odometer - distance;
        for (int i = _trail.Count - 1; i > 0; i--)
        {
            var a = _trail[i - 1];
            var b = _trail[i];
            if (a.Odometer > target) continue;
            double k = b.Odometer > a.Odometer ? (target - a.Odometer) / (b.Odometer - a.Odometer) : 0;
            // 벽에서 돌아선 구간은 섞지 않고 가까운 쪽을 쓴다. 섞으면 벽 앞에서 옆걸음치는 잔상이 생긴다.
            if (a.Direction != b.Direction) return k < 0.5 ? a : b;
            return b with { Odometer = target, X = a.X + (b.X - a.X) * k };
        }
        return _trail[0];
    }
}
