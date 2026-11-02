using Avalonia.Media;

namespace Woodada.Sprites;

/// <summary>색만으로는 낼 수 없는 외형(스페셜 스킨). 가차로 얻을 예정이다.</summary>
public enum RunnerSkin
{
    None,
    Ghost,
    Flash,
    Glitch,
    Ice,
    Kittens,
}

public static class RunnerSkins
{
    /// <summary>settings.json에 저장하는 Id. enum을 그대로 저장하면 나중에 스킨을 빼거나 이름을 바꿨을 때
    /// 옛 파일을 읽다가 설정 전체가 기본값으로 날아가므로, 모르는 Id는 그냥 무시하는 문자열로 둔다.</summary>
    private static readonly (RunnerSkin Skin, string Id)[] Known =
    {
        (RunnerSkin.Ghost, "ghost"),
        (RunnerSkin.Flash, "flash"),
        (RunnerSkin.Glitch, "glitch"),
        (RunnerSkin.Ice, "ice"),
        (RunnerSkin.Kittens, "kittens"),
    };

    public static RunnerSkin Parse(string? id) =>
        Known.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase)).Skin;

    public static string? ToId(RunnerSkin skin) => Known.FirstOrDefault(k => k.Skin == skin).Id;

    /// <summary>
    /// 이 스킨을 입었을 때 실제로 보이는 몸색. 유령·얼음은 고른 색 대신 제 색을 입는다.
    /// 설정 창이 몸과 바탕의 대비를 볼 때 쓴다.
    /// </summary>
    public static Color LookColor(RunnerSkin skin, Color chosen) => skin switch
    {
        RunnerSkin.Ghost => GhostSkin.DisplayColor,
        RunnerSkin.Ice => IceSkin.DisplayColor,
        _ => chosen,
    };

    /// <summary>오버레이 띠에서 몸 위로 남겨 둘 여유(화면 단위). 출렁임·둥실 효과와 몸 밖으로 번지는 발광이 잘리지 않게.
    /// 플래시의 불꽃 줄기는 몸 높이 안에서 살짝만 들려서 기본 여유로 충분하다.</summary>
    public static int Headroom(RunnerSkin skin, int height)
    {
        int basic = Math.Max(4, height / 8); // 몸이 위아래로 출렁이는 여유분
        return skin == RunnerSkin.Ghost ? Math.Max(basic, GhostSkin.Headroom(height)) : basic;
    }

    /// <summary>바닥에서 떠 있는 높이(화면 단위).</summary>
    public static double Lift(RunnerSkin skin, int height, double seconds, double phase) =>
        skin == RunnerSkin.Ghost ? GhostSkin.Lift(height, seconds, phase) : 0;
}
