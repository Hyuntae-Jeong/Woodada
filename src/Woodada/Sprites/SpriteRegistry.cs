namespace Woodada.Sprites;

public static class SpriteRegistry
{
    // 등록 순서가 옷장의 칸 순서이자 가격 순서다(RewardCatalog.RunnerPrice).
    private static readonly Dictionary<string, IRunnerSprite> Sprites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cat"] = new FrameSprite("cat", "고양이", "RunCat.cat_running_", 5),
        ["dachshund"] = new FrameSprite("dachshund", "닥스훈트", "Dog.dog_running_", 5),
        ["rabbit"] = new FrameSprite("rabbit", "토끼", "Rabbit.rabbit_running_", 5),
        // 다른 러너를 추가할 때 여기에 등록.
    };

    /// <summary>
    /// 그림은 다 됐지만 아직 내놓지 않은 러너. 옷장에 "곧 만나요" 칸으로 보이기만 하고 데려올 수 없다.
    /// 출시할 때 여기서 빼면 그 자리에서 바로 살 수 있는 칸이 된다.
    /// </summary>
    private static readonly HashSet<string> Upcoming = new(StringComparer.OrdinalIgnoreCase) { "dachshund", "rabbit" };

    public static IReadOnlyCollection<IRunnerSprite> All => Sprites.Values;

    public static bool IsUpcoming(string? id) => id is not null && Upcoming.Contains(id);

    public static IRunnerSprite Get(string? id) =>
        id is not null && Sprites.TryGetValue(id, out var s) ? s : Sprites["cat"];
}
