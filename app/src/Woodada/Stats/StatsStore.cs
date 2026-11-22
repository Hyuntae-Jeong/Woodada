using System.Globalization;
using System.Text.Json;

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
    /// 생선으로 칠 몸 길이(FishMeter). 러너 기록과 따로 쌓는다. 꺼 둔 고양이 몫도 들어가니
    /// 러너 기록에 넣으면 달리지도 않은 고양이 거리가 늘어난다.
    /// 이 값이 없는 예전 파일은 읽을 때 러너 기록 합계에서 시작한다(<see cref="StatsStore"/>).
    /// </summary>
    public double? FishBodyLengths { get; set; }
}

/// <summary>
/// %APPDATA%\Woodada\stats.json 읽기/쓰기.
/// 프레임마다 메모리에만 더하고, 파일에는 <see cref="SaveInterval"/>마다와 종료할 때 쓴다.
/// </summary>
public sealed class StatsStore
{
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

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
        Current = Load();
    }

    public RunStats Current { get; }

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

        // 자정을 넘기면 다음 프레임부터 새 날짜에 쌓인다.
        string today = TodayKey;
        if (!Current.Days.TryGetValue(today, out var day))
            Current.Days[today] = day = new Dictionary<string, double>();
        day[runnerId] = day.GetValueOrDefault(runnerId) + millimeters;

        _dirty = true;
    }

    /// <summary>생선으로 칠 몸 길이 합계.</summary>
    public double FishBodyLengths => Current.FishBodyLengths ?? 0;

    public void AddFish(double bodyLengths)
    {
        if (bodyLengths <= 0) return;
        Current.FishBodyLengths = FishBodyLengths + bodyLengths;
        Pace.Add(bodyLengths);
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
            SafeFile.WriteAllText(_filePath, JsonSerializer.Serialize(Current, Options));
            _dirty = false;
        }
        catch (Exception)
        {
            // OneDrive 동기화 잠금 등으로 실패하면 다음 주기에 다시 쓴다.
        }
    }

    /// <summary>
    /// 깨진 파일은 stats.json.broken으로 남기고 바로 전에 저장한 stats.json.bak에서 이어 간다.
    /// 둘 다 못 읽으면 0부터 시작한다. 예전에는 여기서 바로 0이 돼 모은 생선이 사라졌다(이슈 #5).
    /// </summary>
    private RunStats Load()
    {
        var stats = SafeFile.Load(_filePath, json => JsonSerializer.Deserialize<RunStats>(json, Options));
        if (stats is null) return new RunStats { FishBodyLengths = 0 };
        stats.Runners ??= new();
        stats.Days ??= new();
        // 생선을 따로 쌓기 전에는 러너 기록 합계가 곧 생선이었다. 거기서 이어 가야 모은 생선이 줄지 않는다.
        stats.FishBodyLengths ??= stats.Runners.Values.Sum(r => r.BodyLengths);
        return stats;
    }
}
