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
        // Load가 깨진 본 파일을 .broken으로 옮기면 예전 .broken을 덮으니, 먼저 꺼내 둔다.
        var lost = TakeLostRecords();
        Current = Load();
        if (lost is not null)
        {
            Merge(Current, lost);
            _dirty = true;
            Save();
        }
    }

    /// <summary>
    /// 예전 버전은 stats.json을 읽다 실패하면 stats.json.broken으로 복사해 두고 기록을 0부터 다시 시작했다(이슈 #5).
    /// 그 파일이 멀쩡히 읽히면(백신 등이 잠깐 잡고 있었던 경우) 꺼내서 지금 기록에 더한다.
    /// 다시 더하지 않게 stats.json.broken.merged로 이름을 바꾸고, 바꾸지 못하면 더하지 않는다.
    /// 정전으로 NUL만 남은 파일은 읽히지 않으니 그대로 둔다.
    /// </summary>
    private RunStats? TakeLostRecords()
    {
        var broken = SafeFile.BrokenPath(_filePath);
        try
        {
            if (!File.Exists(broken)) return null;
            var lost = JsonSerializer.Deserialize<RunStats>(File.ReadAllText(broken), Options);
            if (lost is null) return null;
            File.Move(broken, broken + ".merged", overwrite: true);
            return lost;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 잃었던 기록을 더한다. .broken이 생길 때마다 기록이 0부터 다시 시작했으니, 더하면 원래 값이 된다.
    /// 날짜별 거리도 그 시점 앞뒤로 나뉘어 있어 같은 날끼리 더하면 된다.
    /// </summary>
    private static void Merge(RunStats into, RunStats lost)
    {
        foreach (var (id, t) in lost.Runners ?? new())
        {
            if (t is null) continue;
            if (!into.Runners.TryGetValue(id, out var totals))
                into.Runners[id] = totals = new RunnerTotals();
            totals.Pixels += t.Pixels;
            totals.BodyLengths += t.BodyLengths;
            totals.Millimeters += t.Millimeters;
        }
        foreach (var (date, runners) in lost.Days ?? new())
        {
            if (runners is null) continue;
            if (!into.Days.TryGetValue(date, out var day))
                into.Days[date] = day = new Dictionary<string, double>();
            foreach (var (id, mm) in runners)
                day[id] = day.GetValueOrDefault(id) + mm;
        }
        // 생선 거리를 따로 쌓기 전 파일이면 Load와 같이 러너 기록 합계가 생선이다.
        double lostFish = lost.FishBodyLengths ?? (lost.Runners ?? new()).Values.Sum(r => r?.BodyLengths ?? 0);
        into.FishBodyLengths = (into.FishBodyLengths ?? 0) + Math.Max(0, lostFish);
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
