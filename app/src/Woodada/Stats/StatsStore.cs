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
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Options));
            File.Move(tmp, _filePath, overwrite: true);
            _dirty = false;
        }
        catch (Exception)
        {
            // OneDrive 동기화 잠금 등으로 실패하면 다음 주기에 다시 쓴다.
        }
    }

    private RunStats Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var stats = JsonSerializer.Deserialize<RunStats>(File.ReadAllText(_filePath), Options);
                if (stats is not null)
                {
                    stats.Runners ??= new();
                    stats.Days ??= new();
                    return stats;
                }
            }
        }
        catch (Exception)
        {
            // 깨진 파일은 덮어쓰기 전에 남겨 둔다. 달린 기록이 조용히 0이 되면 아쉬우니까.
            try { File.Copy(_filePath, _filePath + ".broken", overwrite: true); }
            catch (Exception) { }
        }
        return new RunStats();
    }
}
