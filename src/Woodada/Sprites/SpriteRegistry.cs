namespace Woodada.Sprites;

public static class SpriteRegistry
{
    private static readonly Dictionary<string, IRunnerSprite> Sprites = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cat"] = new RunCatSprite(),
        // 다른 러너를 추가할 때 여기에 등록.
    };

    public static IReadOnlyCollection<IRunnerSprite> All => Sprites.Values;

    public static IRunnerSprite Get(string? id) =>
        id is not null && Sprites.TryGetValue(id, out var s) ? s : Sprites["cat"];
}
