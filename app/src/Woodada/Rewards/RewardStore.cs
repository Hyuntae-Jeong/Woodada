using System.Text.Json;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;
using Woodada.Storage;

namespace Woodada.Rewards;

/// <summary>rewards.json 루트. 모은 생선은 저장하지 않고 달린 기록에서 계산한다.</summary>
public sealed class Inventory
{
    /// <summary>지금까지 쓴 생선.</summary>
    public long SpentFish { get; set; }

    /// <summary>잠금 해제한 기본 팔레트 색 Id. 무료 색은 넣지 않아도 갖고 있는 것으로 친다.</summary>
    public List<string> Colors { get; set; } = new();

    /// <summary>연 "나만의 색" 칸. 칸마다 고른 색(#RRGGBB)이 들어 있고, 개수가 연 칸 수다.</summary>
    public List<string> CustomColors { get; set; } = new();

    /// <summary>잠금 해제한 러너(스프라이트 Id). 무료 러너는 넣지 않는다.</summary>
    public List<string> Runners { get; set; } = new();

    /// <summary>냥뽑기로 얻은 스페셜 스킨 Id("ghost" …).</summary>
    public List<string> Skins { get; set; } = new();

    /// <summary>지금까지 꽝이 몇 번 이어졌는지. 천장(RewardCatalog.GachaPity)을 세는 데 쓰고, 스킨이 나오면 0이 된다.</summary>
    public int GachaMissStreak { get; set; }
}

/// <summary>
/// 생선 지갑과 잠금 해제 상태. %APPDATA%\Woodada\rewards.json에 산 것만 저장한다.
/// 생선 = 달린 몸 길이 합 ÷ 1,000 − 쓴 생선. 따로 쌓지 않아서 예전 기록도 그대로 생선이 되고,
/// 생선 수가 기록과 어긋날 일도 없다.
/// 파일에는 서명을 붙인다(<see cref="DataSeal"/>). 앱 밖에서 고친 흔적이 있으면 산 것을 비우고 생선도 0에서 다시 모은다.
/// </summary>
public sealed class RewardStore
{
    /// <summary>서명에 섞는 파일 종류.</summary>
    public const string Purpose = "rewards";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly StatsStore _stats;
    private readonly string _filePath;
    private readonly bool _isNew;

    public RewardStore(StatsStore stats)
    {
        _stats = stats;
        _filePath = Path.Combine(AppInfo.DataFolder, "rewards.json");
        _isNew = !File.Exists(_filePath);
        var (inventory, state) = Load();

        // 산 기록이 사라졌는데 예전에 이 파일을 쓴 적이 있으면(서명된 stats.json에 남아 있다) 누가 지우거나 망가뜨린 것이다.
        // 그냥 빈 지갑으로 시작하면 쓴 생선이 전부 돌아오니, 고친 파일과 똑같이 다룬다.
        WasReset = state == SealState.Tampered ||
                   (state is SealState.Missing or SealState.Broken && stats.Current.RewardsStarted);
        if (WasReset) inventory = new Inventory();
        Current = inventory;

        // 어느 쪽이든 초기화했으면 지금까지 모은 생선을 모두 쓴 것으로 쳐서 0에서 다시 모으게 한다.
        // 산 것만 비우고 생선을 남기면 rewards.json을 고치는 것이 "환불"이 된다.
        // stats.json만 초기화됐으면 모은 생선이 0이 되어 쓴 생선도 0이 된다. 산 것은 남는다.
        if (WasReset || stats.WasReset) Current.SpentFish = EarnedFish;

        // 옛 파일을 받아 줬거나 고쳐 썼으면 바로 서명해 둔다(StatsStore와 같은 이유).
        if (WasReset || stats.WasReset || state == SealState.Legacy) Save();
    }

    public Inventory Current { get; }

    /// <summary>앱 밖에서 고친 흔적이 있어 산 것과 생선을 처음 상태로 돌렸는지.</summary>
    public bool WasReset { get; }

    /// <summary>무언가를 사거나 나만의 색을 바꾸면 알린다. 생선이 느는 건 알리지 않으니 화면이 직접 읽는다.</summary>
    public event EventHandler? Changed;

    public double TotalBodyLengths => _stats.Current.Runners.Values.Sum(r => r.BodyLengths);

    /// <summary>지금까지 모은 생선(쓴 것 포함).</summary>
    public long EarnedFish => (long)Math.Floor(TotalBodyLengths / RewardCatalog.BodyLengthsPerFish);

    /// <summary>지금 쓸 수 있는 생선. 기록 파일이 깨져 줄어들었어도 음수로 보이지 않게 한다.</summary>
    public long Fish => Math.Max(0, EarnedFish - Current.SpentFish);

    /// <summary>다음 생선까지 0~1.</summary>
    public double NextFishProgress
    {
        get
        {
            double per = RewardCatalog.BodyLengthsPerFish;
            return TotalBodyLengths % per / per;
        }
    }

    /// <summary>
    /// 최근 30분 평균으로 생선 1마리에 걸리는 초. 잰 지 1분이 안 됐으면 null,
    /// 그동안 고양이가 거의 안 달렸으면 무한대다.
    /// </summary>
    public double? SecondsPerFish
    {
        get
        {
            var pace = _stats.Pace;
            if (pace.Seconds < 60) return null;
            if (pace.BodyLengths <= 0) return double.PositiveInfinity;
            return pace.Seconds * RewardCatalog.BodyLengthsPerFish / pace.BodyLengths;
        }
    }

    // ───────────────────────── 색 ─────────────────────────

    public bool Owns(PresetColor color) =>
        color.IsFree || Current.Colors.Contains(color.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>이 색을 입을 수 있는지. 기본 팔레트에서 산 색이거나 나만의 색 칸에 넣어 둔 색이면 된다.</summary>
    public bool OwnsColor(string? hex)
    {
        if (RewardCatalog.FindPreset(hex) is { } preset && Owns(preset)) return true;
        return Current.CustomColors.Contains(hex ?? "", StringComparer.OrdinalIgnoreCase);
    }

    public int CustomSlotsOpened => Current.CustomColors.Count;

    /// <summary>다음에 열 나만의 색 칸 가격. 다 열었으면 null.</summary>
    public int? NextCustomSlotPrice =>
        CustomSlotsOpened < RewardCatalog.CustomSlotCount ? RewardCatalog.CustomSlotPrice(CustomSlotsOpened) : null;

    public bool TryBuy(PresetColor color)
    {
        if (Owns(color) || !Spend(color.Price)) return false;
        Current.Colors.Add(color.Id);
        Save();
        return true;
    }

    /// <summary>다음 나만의 색 칸을 열고 고른 색을 넣는다.</summary>
    public bool TryBuyCustomSlot(string hex)
    {
        if (NextCustomSlotPrice is not { } price || !Spend(price)) return false;
        Current.CustomColors.Add(hex);
        Save();
        return true;
    }

    /// <summary>
    /// 연 칸의 색은 10칸을 모두 연 뒤에야 바꿀 수 있다. 그 전에 바꿀 수 있으면 첫 칸 하나만 사서
    /// 색을 계속 갈아 끼우면 되니 나머지 칸을 열 이유가 없어진다.
    /// </summary>
    public bool CanRecolorCustomSlots => CustomSlotsOpened >= RewardCatalog.CustomSlotCount;

    /// <summary>10칸을 모두 열었으면 연 칸의 색을 생선 없이 바꾼다. 아니면 아무것도 하지 않는다.</summary>
    public void SetCustomColor(int index, string hex)
    {
        if (!CanRecolorCustomSlots || index < 0 || index >= Current.CustomColors.Count) return;
        Current.CustomColors[index] = hex;
        Save();
    }

    // ───────────────────────── 러너 ─────────────────────────

    /// <summary>출시 전 러너는 rewards.json에 들어 있어도(손으로 고친 파일 등) 갖고 있지 않은 것으로 본다.</summary>
    public bool OwnsRunner(string spriteId) =>
        !SpriteRegistry.IsUpcoming(spriteId) &&
        (RewardCatalog.RunnerPrice(spriteId) == 0 || Current.Runners.Contains(spriteId, StringComparer.OrdinalIgnoreCase));

    public bool TryBuyRunner(string spriteId)
    {
        if (SpriteRegistry.IsUpcoming(spriteId)) return false;
        if (OwnsRunner(spriteId) || !Spend(RewardCatalog.RunnerPrice(spriteId))) return false;
        Current.Runners.Add(spriteId);
        Save();
        return true;
    }

    // ───────────────────────── 스페셜 스킨(뽑기) ─────────────────────────

    /// <summary>스킨 없음(None)은 늘 갖고 있다.</summary>
    public bool OwnsSkin(RunnerSkin skin) =>
        skin == RunnerSkin.None || Current.Skins.Contains(RunnerSkins.ToId(skin) ?? "", StringComparer.OrdinalIgnoreCase);

    /// <summary>천장까지: 이번을 포함해 몇 번 안에 꼭 스킨이 나오는지(1이면 다음 뽑기가 확정).</summary>
    public int PullsUntilGuaranteed => Math.Max(1, RewardCatalog.GachaPity + 1 - Current.GachaMissStreak);

    /// <summary>
    /// 한 번 뽑는다. GachaWinChance 확률로 스킨, 아니면 꽝(RunnerSkin.None)이다. 꽝이 GachaPity번 이어졌으면 이번은 꼭 스킨.
    /// 스킨은 가진 것을 빼고 남은 것끼리 RewardCatalog의 비율대로 고른다.
    /// 다 모았거나 생선이 모자라면 null. 결과는 연출을 보기 전에 바로 저장한다(연출 중에 창을 닫아도 남게).
    /// </summary>
    /// <param name="forced">개발용: None이면 꽝, 남아 있는 스킨이면 그 스킨을 준다.</param>
    /// <param name="free">개발용: 생선을 쓰지 않는다.</param>
    public RunnerSkin? TryPull(RunnerSkin? forced = null, bool free = false)
    {
        var pool = RewardCatalog.AvailableSkins.Where(s => !OwnsSkin(s.Skin)).ToList();
        if (pool.Count == 0) return null;
        if (!free && !Spend(RewardCatalog.GachaPrice)) return null;

        bool win = forced is { } f
            ? f != RunnerSkin.None
            : Current.GachaMissStreak >= RewardCatalog.GachaPity || Random.Shared.NextDouble() < RewardCatalog.GachaWinChance;
        if (!win)
        {
            Current.GachaMissStreak++;
            Save();
            return RunnerSkin.None;
        }

        var pick = pool.FirstOrDefault(s => s.Skin == forced) ?? PickWeighted(pool);
        Current.Skins.Add(RunnerSkins.ToId(pick.Skin)!);
        Current.GachaMissStreak = 0;
        Save();
        return pick.Skin;
    }

    private static SkinInfo PickWeighted(IReadOnlyList<SkinInfo> pool)
    {
        double roll = Random.Shared.NextDouble() * pool.Sum(RewardCatalog.WeightOf);
        foreach (var s in pool)
        {
            roll -= RewardCatalog.WeightOf(s);
            if (roll < 0) return s;
        }
        return pool[^1];
    }

    /// <summary>개발용: 뽑은 스킨과 꽝 연속 횟수를 모두 지운다.</summary>
    public void DevClearSkins()
    {
        Current.Skins.Clear();
        Current.GachaMissStreak = 0;
        Save();
    }

    /// <summary>개발용: 생선을 더 준다. 생선은 기록에서 계산하므로 쓴 생선을 줄이는 식으로 준다.</summary>
    public void DevAddFish(int amount)
    {
        Current.SpentFish -= amount;
        Save();
    }

    // ───────────────────────── 처음 실행 ─────────────────────────

    /// <summary>
    /// 보상 시스템이 생기기 전부터 쓰던 사람이 지금 입고 있는 색을 잃지 않게 한다.
    /// rewards.json이 없을 때 한 번만: 팔레트 색이면 그 색을, 직접 고른 색이면 나만의 색 칸을 무료로 준다.
    /// </summary>
    public void GrantStartingItems(AppSettings settings)
    {
        if (!_isNew || File.Exists(_filePath)) return;
        // 보상 시스템 전부터 쓰던 사람만 해당한다. 그런 사람은 서명을 넣기 전 버전이 쓴 stats.json이 있다.
        // 새로 설치했거나 기록을 지운 경우까지 주면, settings.json에 비싼 색을 적고 기록을 지워 공짜로 얻을 수 있다.
        if (_stats.LoadedAs != SealState.Legacy) return;
        foreach (var r in settings.Runners)
        {
            if (OwnsColor(r.Color)) continue;
            if (RewardCatalog.FindPreset(r.Color) is { } preset) Current.Colors.Add(preset.Id);
            else if (Current.CustomColors.Count < RewardCatalog.CustomSlotCount) Current.CustomColors.Add(r.Color);
        }
        foreach (var r in settings.Runners)
        {
            if (!SpriteRegistry.IsUpcoming(r.SpriteId) && !OwnsRunner(r.SpriteId)) Current.Runners.Add(r.SpriteId);
        }
        Save();
    }

    /// <summary>
    /// 갖고 있지 않은 색·러너·스킨을 입고 있으면 벗긴 설정을 돌려준다. 고칠 게 없으면 null.
    /// settings.json은 서명하지 않으니, 손으로 스킨 Id를 적어 넣거나 기록이 초기화된 뒤에도 입고 있는 걸 여기서 막는다.
    /// </summary>
    public AppSettings? WithoutUnowned(AppSettings settings)
    {
        var fixedSettings = settings.Clone();
        bool changed = false;
        foreach (var r in fixedSettings.Runners)
        {
            if (!OwnsColor(r.Color)) { r.Color = AppSettings.DefaultColor; changed = true; }
            if (!OwnsRunner(r.SpriteId)) { r.SpriteId = "cat"; changed = true; }
            if (r.Skin is not null && !OwnsSkin(RunnerSkins.Parse(r.Skin))) { r.Skin = null; changed = true; }
        }
        return changed ? fixedSettings : null;
    }

    private bool Spend(int price)
    {
        if (price < 0 || Fish < price) return false;
        Current.SpentFish += price;
        return true;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            // 쓰는 도중 꺼져도 산 기록이 반쯤 잘리지 않도록 임시 파일에 쓰고 교체한다.
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, DataSeal.Seal(Purpose, JsonSerializer.Serialize(Current, Options)));
            File.Move(tmp, _filePath, overwrite: true);
            // 이 파일을 쓴 적이 있다고 서명된 stats.json에 남긴다. 나중에 누가 이 파일을 지우면 알아챈다.
            _stats.MarkRewardsStarted();
        }
        catch (Exception)
        {
            // OneDrive 동기화 잠금 등으로 실패해도 메모리에는 남아 있고, 다음에 살 때 다시 쓴다.
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private (Inventory, SealState) Load()
    {
        try
        {
            var (state, json) = DataSeal.Open(Purpose, _filePath);
            // stats.json이 켤 때 이미 서명된 모양이었다면 이 컴퓨터는 이미 새 버전으로 넘어왔다.
            // 그런데 이 파일만 옛 모양이면 누가 서명을 지운 것이다.
            if (state == SealState.Legacy && !DataSeal.AcceptLegacy(_stats.LoadedAs)) state = SealState.Tampered;

            switch (state)
            {
                case SealState.Missing:
                    return (new Inventory(), state);
                case SealState.Tampered:
                    // 덮어쓰기 전에 남겨 둔다. 오탐이면 이 파일로 되살릴 수 있다.
                    DataSeal.Quarantine(_filePath, ".tampered");
                    return (new Inventory(), state);
                case SealState.Sealed or SealState.Legacy:
                    var inv = JsonSerializer.Deserialize<Inventory>(json!, Options);
                    if (inv is null) break;
                    inv.Colors ??= new();
                    inv.CustomColors ??= new();
                    inv.Runners ??= new();
                    inv.Skins ??= new();
                    inv.CustomColors = inv.CustomColors
                        .Where(h => ColorUtil.TryParse(h, out _)).Take(RewardCatalog.CustomSlotCount).ToList();
                    return (inv, state);
            }
        }
        catch (Exception)
        {
        }
        // 깨진 파일은 남겨 둔다. 산 기록이 조용히 사라지면 아쉬우니까.
        if (File.Exists(_filePath)) DataSeal.Quarantine(_filePath, ".broken");
        return (new Inventory(), SealState.Broken);
    }
}
