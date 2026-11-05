using System.Globalization;
using System.Text.Json;
using Woodada.Storage;

namespace Woodada.Stats;

/// <summary>러너 한 마리가 지금까지 달린 양. 같은 이동을 세 가지 단위로 함께 쌓는다.</summary>
public sealed class RunnerTotals
{
    /// <summary>화면에서 움직인 물리 픽셀.</summary>
    public double Pixels { get; set; }

    /// <summary>몸 길이 단위로 센 이동. 크기를 키워도 늘지 않으므로 보상 계산에 쓰기 좋다.</summary>
    public double BodyLengths { get; set; }

    /// <summary>모니터 실제 크기로 환산한 거리(mm). 프로필 표시용.</summary>
    public double Millimeters { get; set; }
}

/// <summary>stats.json 루트.</summary>
public sealed class RunStats
{
    /// <summary>러너 Id(cpu, memory …)별 누적.</summary>
    public Dictionary<string, RunnerTotals> Runners { get; set; } = new();

    /// <summary>날짜(yyyy-MM-dd, 현지 시간) → 러너 Id → 그날 달린 거리(mm).</summary>
    public Dictionary<string, Dictionary<string, double>> Days { get; set; } = new();

    /// <summary>
    /// rewards.json을 한 번이라도 썼는지. 서명된 이 파일에 남겨 두어서, 누가 rewards.json을 지워
    /// 쓴 생선을 돌려받으려 하면 알아챈다(RewardStore).
    /// </summary>
    public bool RewardsStarted { get; set; }
}

/// <summary>
/// %APPDATA%\Woodada\stats.json 읽기/쓰기.
/// 프레임마다 메모리에만 더하고, 파일에는 <see cref="SaveInterval"/>마다와 종료할 때 쓴다.
/// 파일에는 서명을 붙인다(<see cref="DataSeal"/>). 앱 밖에서 고친 파일은 처음 상태로 돌린다.
/// </summary>
public sealed class StatsStore
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

    /// <summary>서명에 섞는 파일 종류.</summary>
    public const string Purpose = "stats";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = null, // 러너 Id·날짜 키는 그대로 둔다
    };

    private readonly string _filePath;
    private DateTime _lastSave = DateTime.UtcNow;
    private bool _dirty;

    public StatsStore()
    {
        _filePath = Path.Combine(AppInfo.DataFolder, "stats.json");
        (Current, LoadedAs) = Load();
        // 처음 상태로 돌렸거나 옛 파일을 받아 줬으면 바로 서명해 둔다. 미뤘다가 rewards.json만 먼저 서명되면,
        // 다음 실행 때 서명 안 된 이 파일을 "누가 서명을 지운 것"으로 오해한다.
        if (LoadedAs is SealState.Tampered or SealState.Legacy)
        {
            _dirty = true;
            Save();
        }
    }

    public RunStats Current { get; }

    /// <summary>켤 때 디스크에 있던 파일의 상태. 앱이 다시 쓰기 전 모습이다.</summary>
    public SealState LoadedAs { get; }

    /// <summary>앱 밖에서 고친 흔적이 있어 처음 상태로 돌렸는지.</summary>
    public bool WasReset => LoadedAs == SealState.Tampered;

    /// <summary>rewards.json을 처음 썼을 때 RewardStore가 부른다.</summary>
    public void MarkRewardsStarted()
    {
        if (Current.RewardsStarted) return;
        Current.RewardsStarted = true;
        _dirty = true;
        Save();
    }

    /// <summary>최근 30분 속도. 시간은 프레임 루프가 <see cref="PaceMeter.Tick"/>으로 넣는다.</summary>
    public PaceMeter Pace { get; } = new();

    public static string TodayKey => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void Add(string runnerId, double pixels, double bodyLengths, double millimeters)
    {
        if (pixels <= 0) return;

        if (!Current.Runners.TryGetValue(runnerId, out var totals))
            Current.Runners[runnerId] = totals = new RunnerTotals();
        totals.Pixels += pixels;
        totals.BodyLengths += bodyLengths;
        totals.Millimeters += millimeters;
        Pace.Add(bodyLengths);

        // 자정을 넘기면 다음 프레임부터 새 날짜에 쌓인다.
        string today = TodayKey;
        if (!Current.Days.TryGetValue(today, out var day))
            Current.Days[today] = day = new Dictionary<string, double>();
        day[runnerId] = day.GetValueOrDefault(runnerId) + millimeters;

        _dirty = true;
    }

    public RunnerTotals Total(string runnerId) => Current.Runners.GetValueOrDefault(runnerId) ?? new RunnerTotals();

    public double TodayMillimeters(string runnerId) =>
        Current.Days.TryGetValue(TodayKey, out var day) ? day.GetValueOrDefault(runnerId) : 0;

    public double TodayMillimeters() =>
        Current.Days.TryGetValue(TodayKey, out var day) ? day.Values.Sum() : 0;

    public double TotalMillimeters() => Current.Runners.Values.Sum(r => r.Millimeters);

    /// <summary>프레임 루프에서 불러도 된다. 바뀐 게 있고 저장한 지 오래됐을 때만 쓴다.</summary>
    public void SaveIfDue()
    {
        if (_dirty && DateTime.UtcNow - _lastSave >= SaveInterval) Save();
    }

    public void Save()
    {
        _lastSave = DateTime.UtcNow;
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            // 쓰는 도중 꺼져도 기존 기록이 반쯤 잘리지 않도록 임시 파일에 쓰고 교체한다.
            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, DataSeal.Seal(Purpose, JsonSerializer.Serialize(Current, Options)));
            File.Move(tmp, _filePath, overwrite: true);
            _dirty = false;
        }
        catch (Exception)
        {
            // OneDrive 동기화 잠금 등으로 실패하면 다음 주기에 다시 쓴다.
        }
    }

    private (RunStats, SealState) Load()
    {
        try
        {
            var (state, json) = DataSeal.Open(Purpose, _filePath);
            if (state == SealState.Legacy &&
                !DataSeal.AcceptLegacy(DataSeal.Peek(Rewards.RewardStore.Purpose, Path.Combine(AppInfo.DataFolder, "rewards.json"))))
                state = SealState.Tampered;

            switch (state)
            {
                case SealState.Missing:
                    return (new RunStats(), state);
                case SealState.Tampered:
                    // 덮어쓰기 전에 남겨 둔다. 오탐이면 이 파일로 되살릴 수 있다.
                    DataSeal.Quarantine(_filePath, ".tampered");
                    return (new RunStats(), state);
                case SealState.Sealed or SealState.Legacy:
                    var stats = JsonSerializer.Deserialize<RunStats>(json!, Options);
                    if (stats is null) break;
                    stats.Runners ??= new();
                    stats.Days ??= new();
                    return (stats, state);
            }
        }
        catch (Exception)
        {
        }
        // 깨진 파일은 덮어쓰기 전에 남겨 둔다. 달린 기록이 조용히 0이 되면 아쉬우니까.
        // 깨진 기록은 처음부터 다시 쌓을 뿐이라 생선을 더 얻을 길이 없으니, 알리지 않고 넘어간다.
        if (File.Exists(_filePath)) DataSeal.Quarantine(_filePath, ".broken");
        return (new RunStats(), SealState.Broken);
    }
}
