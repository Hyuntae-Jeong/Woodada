namespace Woodada.Sprites;

/// <summary>색만으로는 낼 수 없는 외형(스페셜 스킨). 가차로 얻을 예정이다.</summary>
public enum RunnerSkin
{
    None,
    Ghost,
}

public static class RunnerSkins
{
    /// <summary>settings.json에 저장하는 Id. enum을 그대로 저장하면 나중에 스킨을 빼거나 이름을 바꿨을 때
    /// 옛 파일을 읽다가 설정 전체가 기본값으로 날아가므로, 모르는 Id는 그냥 무시하는 문자열로 둔다.</summary>
    public const string GhostId = "ghost";

    public static RunnerSkin Parse(string? id) =>
        string.Equals(id, GhostId, StringComparison.OrdinalIgnoreCase) ? RunnerSkin.Ghost : RunnerSkin.None;

    public static string? ToId(RunnerSkin skin) => skin == RunnerSkin.Ghost ? GhostId : null;

    /// <summary>오버레이 띠에서 몸 위로 남겨 둘 여유(화면 단위). 출렁임·둥실 효과와 몸 밖으로 번지는 발광이 잘리지 않게.</summary>
    public static int Headroom(RunnerSkin skin, int height)
    {
        int basic = Math.Max(4, height / 8); // 몸이 위아래로 출렁이는 여유분
        return skin == RunnerSkin.Ghost ? Math.Max(basic, GhostSkin.Headroom(height)) : basic;
    }

    /// <summary>바닥에서 떠 있는 높이(화면 단위).</summary>
    public static double Lift(RunnerSkin skin, int height, double seconds, double phase) =>
        skin == RunnerSkin.Ghost ? GhostSkin.Lift(height, seconds, phase) : 0;
}
