using System.Text.Json;
using Woodada.Settings;
using Woodada.Sprites;
using Woodada.Stats;

namespace Woodada.Rewards;

/// <summary>rewards.json 루트. 모은 생선은 여기 저장하지 않고 stats.json의 생선 거리에서 계산한다.</summary>
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

    /// <summary>
    /// 스킨 Id마다 이 앱이 그 스킨을 처음 본 때(UTC). NEW 표시 기간을 여기서부터 센다.
    /// DateTime.MinValue는 처음부터 있던 스킨이라 새것이 아니라는 뜻이다. null이면 NEW 표시가 생기기 전 파일이다.
    /// </summary>
    public Dictionary<string, DateTime>? SkinsFirstSeen { get; set; }

    /// <summary>냥뽑기 탭을 마지막으로 열어 새 스킨 알림(탭의 빨간 점)을 확인한 때(UTC).</summary>
    public DateTime? GachaTabOpenedAt { get; set; }
}

/// <summary>뽑기 한 번의 결과. Skin이 None이면 꽝이고, Refund는 꽝일 때 돌려준 생선이다.</summary>
public readonly record struct GachaResult(RunnerSkin Skin, int Refund);

/// <summary>
/// 생선 지갑과 잠금 해제 상태. %APPDATA%\Woodada\rewards.json에 산 것만 저장한다.
/// 생선 = 생선 거리(몸 길이) ÷ 1,000 − 쓴 생선. 생선 거리는 고양이가 화면에 떠 있는 동안
/// CPU·메모리 몫을 함께 센 값이다(FishMeter). 모은 생선 수는 따로 저장하지 않는다.
/// </summary>
public sealed class RewardStore
{
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
        Current = Load();
        NoteSkinsSeen();
    }

    public Inventory Current { get; }

    /// <summary>무언가를 사거나 나만의 색을 바꾸면 알린다. 생선이 느는 건 알리지 않으니 화면이 직접 읽는다.</summary>
    public event EventHandler? Changed;

    public double FishBodyLengths => _stats.FishBodyLengths;

    /// <summary>지금까지 모은 생선(쓴 것 포함).</summary>
    public long EarnedFish => (long)Math.Floor(FishBodyLengths / RewardCatalog.BodyLengthsPerFish);

    /// <summary>지금 쓸 수 있는 생선. 기록 파일이 깨져 줄어들었어도 음수로 보이지 않게 한다.</summary>
    public long Fish => Math.Max(0, EarnedFish - Current.SpentFish);

    /// <summary>다음 생선까지 0~1.</summary>
    public double NextFishProgress
    {
        get
        {
            double per = RewardCatalog.BodyLengthsPerFish;
            return FishBodyLengths % per / per;
        }
    }

    /// <summary>
    /// 최근 30분 평균으로 생선 1마리에 걸리는 초. 잰 지 1분이 안 됐으면 null,
    /// 그동안 고양이가 숨어 있었으면 무한대다.
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
    /// 꽝이면 생선 1~100개(RewardCatalog.RollGachaRefund)를 돌려준다. 허탈함을 조금이라도 덜려는 것이다.
    /// 다 모았거나 생선이 모자라면 null. 결과는 연출을 보기 전에 바로 저장한다(연출 중에 창을 닫아도 남게).
    /// </summary>
    /// <param name="forced">개발용: None이면 꽝, 남아 있는 스킨이면 그 스킨을 준다.</param>
    /// <param name="free">개발용: 생선을 쓰지 않는다. 꽝이어도 생선을 돌려주지 않는다(공짜 뽑기로 생선이 늘면 안 되니까).</param>
    public GachaResult? TryPull(RunnerSkin? forced = null, bool free = false)
    {
        var pool = RewardCatalog.AvailableSkins.Where(s => !OwnsSkin(s.Skin)).ToList();
        if (pool.Count == 0) return null;
        if (!free && !Spend(RewardCatalog.GachaPrice)) return null;

        bool win = forced is { } f
            ? f != RunnerSkin.None
            : Current.GachaMissStreak >= RewardCatalog.GachaPity || Random.Shared.NextDouble() < RewardCatalog.GachaWinChance;
        if (!win)
        {
            int refund = free ? 0 : RewardCatalog.RollGachaRefund();
            Current.SpentFish -= refund;
            Current.GachaMissStreak++;
            Save();
            return new GachaResult(RunnerSkin.None, refund);
        }

        var pick = pool.FirstOrDefault(s => s.Skin == forced) ?? PickWeighted(pool);
        Current.Skins.Add(RunnerSkins.ToId(pick.Skin)!);
        Current.GachaMissStreak = 0;
        Save();
        return new GachaResult(pick.Skin, 0);
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

    // ───────────────────────── 새 스킨(NEW) ─────────────────────────

    /// <summary>
    /// 이 빌드에서 보이는 스킨 중 처음 보는 것을 지금 시각으로 적는다. 앱을 켤 때 한 번.
    /// 처음 설치했으면 모든 스킨이 처음이라 NEW를 붙이지 않는다. NEW 표시가 생기기 전 파일이면
    /// 그 전에 공개한 스킨(SkinsBeforeNewMarks)만 새것이 아닌 것으로 친다.
    /// </summary>
    private void NoteSkinsSeen()
    {
        bool before = Current.SkinsFirstSeen is null;
        var seen = Current.SkinsFirstSeen ??= new(StringComparer.OrdinalIgnoreCase);
        bool changed = before;
        foreach (var info in RewardCatalog.AvailableSkins)
        {
            if (RunnerSkins.ToId(info.Skin) is not { } id || seen.ContainsKey(id)) continue;
            bool old = _isNew || (before && RewardCatalog.SkinsBeforeNewMarks.Contains(info.Skin));
            seen[id] = old ? DateTime.MinValue : DateTime.UtcNow;
            changed = true;
        }
        // 처음 설치면 아직 저장하지 않는다. 파일이 생기면 GrantStartingItems가 처음 실행인 줄 모른다. 그쪽이 저장한다.
        if (changed && !_isNew) Save();
    }

    private DateTime? FirstSeen(RunnerSkin skin) =>
        RunnerSkins.ToId(skin) is { } id && Current.SkinsFirstSeen?.TryGetValue(id, out var t) == true ? t : null;

    /// <summary>
    /// 처음 본 지 NewSkinDays일이 안 된 스킨인지. 가졌는지는 보지 않는다(뽑기 연출 중에는 화면이 정한다).
    /// </summary>
    public bool IsNewSkin(RunnerSkin skin) =>
        FirstSeen(skin) is { } t && t != DateTime.MinValue && DateTime.UtcNow - t < TimeSpan.FromDays(RewardCatalog.NewSkinDays);

    /// <summary>냥뽑기 탭에 빨간 점을 띄울지: 아직 없는 새 스킨이 있고, 그 스킨이 생긴 뒤로 탭을 열지 않았다.</summary>
    public bool HasUnopenedNewSkin
    {
        get
        {
            var opened = Current.GachaTabOpenedAt ?? DateTime.MinValue;
            return RewardCatalog.AvailableSkins.Any(s => IsNewSkin(s.Skin) && !OwnsSkin(s.Skin) && FirstSeen(s.Skin) > opened);
        }
    }

    /// <summary>냥뽑기 탭을 열었다. 빨간 점이 떠 있을 때만 적는다(탭을 열 때마다 파일을 쓰지 않게).</summary>
    public void MarkGachaTabOpened()
    {
        if (!HasUnopenedNewSkin) return;
        Current.GachaTabOpenedAt = DateTime.UtcNow;
        Save();
    }

    /// <summary>개발용: 아직 없는 스킨을 모두 방금 나온 새 스킨으로 만들고, 탭 빨간 점도 다시 켠다.</summary>
    public void DevResetNewSkins()
    {
        var seen = Current.SkinsFirstSeen ??= new(StringComparer.OrdinalIgnoreCase);
        foreach (var info in RewardCatalog.AvailableSkins.Where(s => !OwnsSkin(s.Skin)))
            if (RunnerSkins.ToId(info.Skin) is { } id) seen[id] = DateTime.UtcNow;
        Current.GachaTabOpenedAt = null;
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
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch (Exception)
        {
            // OneDrive 동기화 잠금 등으로 실패해도 메모리에는 남아 있고, 다음에 살 때 다시 쓴다.
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Inventory Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var inv = JsonSerializer.Deserialize<Inventory>(File.ReadAllText(_filePath), Options);
                if (inv is not null)
                {
                    inv.Colors ??= new();
                    inv.CustomColors ??= new();
                    inv.Runners ??= new();
                    inv.Skins ??= new();
                    if (inv.SkinsFirstSeen is not null)
                        inv.SkinsFirstSeen = new(inv.SkinsFirstSeen, StringComparer.OrdinalIgnoreCase);
                    inv.CustomColors = inv.CustomColors
                        .Where(h => ColorUtil.TryParse(h, out _)).Take(RewardCatalog.CustomSlotCount).ToList();
                    return inv;
                }
            }
        }
        catch (Exception)
        {
            // 깨진 파일은 남겨 둔다. 산 기록이 조용히 사라지면 아쉬우니까.
            try { File.Copy(_filePath, _filePath + ".broken", overwrite: true); }
            catch (Exception) { }
        }
        return new Inventory();
    }
}
