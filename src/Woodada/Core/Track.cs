using Woodada.Interop;

namespace Woodada.Core;

/// <summary>러너의 왼쪽 모서리 X가 움직일 수 있는 범위(화면 좌표).</summary>
public readonly record struct TrackBounds(double MinX, double MaxX)
{
    public double Width => MaxX - MinX;
}

/// <summary>러너들이 달리는 가로 띠. 작업표시줄 위치에 따라 정해진다.</summary>
public readonly record struct Runway(int Left, int Right, int GroundY, bool HangBelow)
{
    public int Width => Right - Left;

    /// <summary>러너 발이 닿는 선 기준으로 오버레이 창이 차지할 사각형.</summary>
    public Rectangle GetStrip(int height)
    {
        // 작업표시줄과 1px 겹쳐서 발이 떠 보이지 않게 한다.
        const int overlap = 1;
        return HangBelow
            ? new Rectangle(Left, GroundY - overlap, Width, height)
            : new Rectangle(Left, GroundY - height + overlap, Width, height);
    }

    public TrackBounds TrackFor(double bodyWidth) => new(Left, Right - bodyWidth);

    public static Runway FromTaskbar(TaskbarGeometry tb)
    {
        var b = tb.Bounds;
        var s = tb.Screen;
        return tb.Edge switch
        {
            // 기본: 작업표시줄 윗선 위를 달린다.
            TaskbarEdge.Bottom => new Runway(b.Left, b.Right, b.Top, HangBelow: false),
            // 위쪽 작업표시줄: 위로 올라갈 공간이 없으니 아랫선 밑으로 매달려 달린다.
            TaskbarEdge.Top => new Runway(b.Left, b.Right, b.Bottom, HangBelow: true),
            // 세로 작업표시줄: 화면 바닥을 따라, 작업표시줄을 피해서 달린다.
            TaskbarEdge.Left => new Runway(b.Right, s.Right, s.Bottom, HangBelow: false),
            TaskbarEdge.Right => new Runway(s.Left, b.Left, s.Bottom, HangBelow: false),
            _ => new Runway(s.Left, s.Right, s.Bottom, HangBelow: false),
        };
    }
}

/// <summary>사용률 → 속도 환산 규칙. 몸 길이 기준이라 크기를 바꿔도 체감 속도가 같다.</summary>
public readonly record struct SpeedProfile(double BodyWidth, double Min, double Max, double Gamma, double StridesPerBody)
{
    public static SpeedProfile ForBodyWidth(double bodyWidth) => new(
        BodyWidth: bodyWidth,
        Min: bodyWidth * 0.4,      // 0%: 초당 몸 길이 0.4개 — 어슬렁
        Max: bodyWidth * 7.0,      // 100%: 초당 몸 길이 7개 — 전력 질주
        Gamma: 0.7,                // 평소 구간(5~30%)에서도 속도 차이가 보이도록 곡선을 위로 휜다
        StridesPerBody: 0.8);      // 몸 길이 1개 이동할 때 다리 주기 0.8회
}
