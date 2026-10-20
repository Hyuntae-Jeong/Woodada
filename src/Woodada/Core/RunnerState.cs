namespace Woodada.Core;

/// <summary>러너 한 마리의 위치·방향·보행 위상.</summary>
public sealed class RunnerState
{
    /// <summary>러너 왼쪽 모서리의 화면 X 좌표(px).</summary>
    public double X { get; set; }

    /// <summary>+1 오른쪽, -1 왼쪽.</summary>
    public int Direction { get; set; } = 1;

    /// <summary>0~1 보행 주기 위상. 스프라이트가 다리 각도를 여기서 계산한다.</summary>
    public double GaitPhase { get; set; }

    /// <summary>현재 속도(px/초).</summary>
    public double Speed { get; private set; }

    /// <summary>0~1. 스프라이트가 보폭·몸 기울기를 키우는 데 쓴다.</summary>
    public double Intensity { get; private set; }

    public void Advance(double dt, double usagePercent, TrackBounds track, SpeedProfile profile)
    {
        Intensity = Math.Clamp(usagePercent / 100.0, 0, 1);
        Speed = profile.Min + (profile.Max - profile.Min) * Math.Pow(Intensity, profile.Gamma);

        if (track.Width <= 0)
        {
            // 작업표시줄이 러너보다 좁으면 제자리 달리기.
            X = track.MinX;
        }
        else
        {
            double x = X + Direction * Speed * dt;
            // 벽을 넘은 거리를 버리지 않고 반사시켜야 빠를 때 벽에 붙어 떠는 것처럼 안 보인다.
            // dt 상한이 있어 보통 1회면 끝나지만, 트랙이 갑자기 줄어든 경우를 위해 횟수를 제한한다.
            for (int i = 0; i < 4 && (x < track.MinX || x > track.MaxX); i++)
            {
                if (x > track.MaxX) { x = 2 * track.MaxX - x; Direction = -1; }
                else { x = 2 * track.MinX - x; Direction = 1; }
            }
            X = Math.Clamp(x, track.MinX, track.MaxX);
        }

        // 시간이 아니라 이동 거리에 비례시켜야 발이 바닥에서 미끄러지지 않는다.
        double cycles = Speed * dt / Math.Max(1, profile.BodyWidth) * profile.StridesPerBody;
        GaitPhase = (GaitPhase + cycles) % 1.0;
    }
}
