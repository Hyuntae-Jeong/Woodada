using Woodada.Sprites;

namespace Woodada.Rewards;

/// <summary>기본 팔레트의 색 하나. Price가 0이면 처음부터 갖고 있다.</summary>
public sealed record PresetColor(string Id, string Name, string Hex, int Price)
{
    public bool IsFree => Price <= 0;
}

/// <summary>
/// 생선 보상 규칙과 가격표.
/// 평소(CPU 10%, 메모리 50%)에 두 고양이를 합쳐 시간당 생선 약 22개, 하루 8시간이면 150개쯤 모이도록 맞췄다.
/// </summary>
public static class RewardCatalog
{
    /// <summary>
    /// 몸 길이 이만큼 달리면 생선 1개. 픽셀이 아니라 몸 길이로 세야 크기를 키워 생선을 더 버는 일이 없다.
    /// 기본 크기(28px)로는 약 4만 px, 화면을 20번쯤 가로지르는 거리다.
    /// </summary>
    public const double BodyLengthsPerFish = 1000;

    /// <summary>그리드 순서 그대로. 무료 → 흔함 → 보통 → 희귀.</summary>
    public static readonly IReadOnlyList<PresetColor> Presets = new PresetColor[]
    {
        new("black", "까망", "#1E1E22", 0),
        new("white", "하양", "#ECE7DF", 0),
        new("cheese", "치즈", "#F4A261", 50),
        new("mackerel", "고등어", "#6D8BA8", 50),
        new("gray", "회색", "#9A9AA0", 50),
        new("calico", "삼색", "#C9825B", 150),
        new("mint", "민트", "#7FB8A4", 150),
        new("lemon", "레몬", "#E6C55A", 150),
        new("lavender", "라벤더", "#AE8FC7", 300),
        new("cherry", "체리", "#D9626B", 300),
    };

    /// <summary>아무 색이나 직접 골라 넣는 칸 수.</summary>
    public const int CustomSlotCount = 10;

    /// <summary>나만의 색 칸은 앞에서부터 차례로 연다. 첫 칸 500, 한 칸마다 100씩 오른다.</summary>
    public static int CustomSlotPrice(int index) => 500 + 100 * index;

    /// <summary>처음부터 갖고 있는 러너.</summary>
    public const string FreeRunnerId = "cat";

    /// <summary>새 러너는 등록된 순서대로 1,000 → 2,000 → 3,000 …</summary>
    public static int RunnerPrice(string spriteId)
    {
        if (string.Equals(spriteId, FreeRunnerId, StringComparison.OrdinalIgnoreCase)) return 0;
        int index = PaidRunners.ToList().FindIndex(s => string.Equals(s.Id, spriteId, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? NextUnreleasedRunnerPrice : 1000 * (index + 1);
    }

    /// <summary>아직 그림이 없는 다음 러너의 가격. 옷장에 "곧 만나요" 칸으로 보여준다.</summary>
    public static int NextUnreleasedRunnerPrice => 1000 * (PaidRunners.Count() + 1);

    private static IEnumerable<IRunnerSprite> PaidRunners =>
        SpriteRegistry.All.Where(s => !string.Equals(s.Id, FreeRunnerId, StringComparison.OrdinalIgnoreCase));

    public static PresetColor? FindPreset(string? hex) =>
        Presets.FirstOrDefault(p => string.Equals(p.Hex, hex, StringComparison.OrdinalIgnoreCase));
}
