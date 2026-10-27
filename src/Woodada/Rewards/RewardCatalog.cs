using Avalonia.Media;
using Woodada.Sprites;

namespace Woodada.Rewards;

/// <summary>기본 팔레트의 색 하나. Price가 0이면 처음부터 갖고 있다.</summary>
public sealed record PresetColor(string Id, string Name, string Hex, int Price)
{
    public bool IsFree => Price <= 0;
}

/// <summary>뽑기로 얻는 스페셜 스킨 하나.</summary>
/// <param name="Weight">뽑기 비율. 가진 스킨을 빼고 남은 것끼리 이 비율대로 나눈다.</param>
/// <param name="Accent">무대의 빛·별 색.</param>
/// <param name="Ink">흰 바탕 위 글자(별·등급) 색.</param>
public sealed record SkinInfo(RunnerSkin Skin, string Name, int Stars, string Tier, int Weight, string Description, Color Accent, Color Ink);

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

    // ───────────────────────── 스페셜 스킨 뽑기 (개발용 빌드에서만) ─────────────────────────

    /// <summary>뽑기 한 번 가격.</summary>
    public const int GachaPrice = 2000;

    /// <summary>
    /// 도감 순서 그대로. 등급별 비율은 레어 60 : 에픽 30 : 전설 10이고, 같은 등급끼리는 똑같이 나눠 갖는다.
    /// 가진 스킨은 빼고 남은 것끼리 나눈다.
    /// </summary>
    public static readonly IReadOnlyList<SkinInfo> Skins = new SkinInfo[]
    {
        new(RunnerSkin.Ghost, "유령냥", 2, "레어", 60,
            "반투명한 몸으로 작업표시줄 위를 둥실둥실 떠다녀요. 고른 색 대신 유령색을 입어요.",
            Color.FromRgb(0x8E, 0xC5, 0xFF), Color.FromRgb(0x3D, 0x86, 0xD6)),
        new(RunnerSkin.Flash, "플래시냥", 3, "에픽", 15,
            "지나온 길에 잔상과 불꽃을 남겨요. 빠를수록 꼬리가 길어져요.",
            Color.FromRgb(0xFF, 0x8A, 0x1F), Color.FromRgb(0xE0, 0x70, 0x10)),
        new(RunnerSkin.Ice, "얼음냥", 3, "에픽", 15,
            "얼음빛 몸으로 달리며 발자국마다 서리를 남겨요. 빠를수록 서리가 촘촘하고 오래 남아요.",
            Color.FromRgb(0x7F, 0xD8, 0xF5), Color.FromRgb(0x1F, 0x9A, 0xC8)),
        new(RunnerSkin.Glitch, "글리치냥", 4, "전설", 5,
            "가끔 화면이 고장 난 것처럼 지지직거려요. CPU가 바쁠수록 자주요.",
            Color.FromRgb(0xC0, 0x7B, 0xFF), Color.FromRgb(0x9A, 0x4F, 0xE0)),
        new(RunnerSkin.Kittens, "줄줄이냥", 4, "전설", 5,
            "새끼 고양이들이 졸졸 따라와요. CPU가 바쁠수록 한 마리씩 늘어나 최대 네 마리까지요.",
            Color.FromRgb(0xFF, 0x9E, 0xC4), Color.FromRgb(0xE0, 0x5A, 0x95)),
    };

    /// <summary>
    /// 다음 릴리즈에 공개할 스킨(유령냥·플래시냥·글리치냥). 일반 버전의 냥뽑기 예고 화면은 이것만 보여 준다.
    /// 나머지 스킨은 개발용 빌드에서만 보인다. Skins보다 아래에 있어야 초기화 순서가 맞는다.
    /// </summary>
    public static readonly IReadOnlyList<SkinInfo> UpcomingSkins =
        Skins.Where(s => s.Skin is RunnerSkin.Ghost or RunnerSkin.Flash or RunnerSkin.Glitch).ToList();

    public static SkinInfo? FindSkin(RunnerSkin skin) => Skins.FirstOrDefault(s => s.Skin == skin);

    /// <summary>뽑기 무대·도감에서 입힐 몸색. 플래시냥은 달아오른 빨간 고양이(체리)로, 나머지는 하양으로 보여 준다.</summary>
    public static Color ShowcaseColor(RunnerSkin skin) =>
        skin == RunnerSkin.Flash ? Color.FromRgb(0xD9, 0x62, 0x6B) : Color.FromRgb(0xEC, 0xE7, 0xDF);
}
