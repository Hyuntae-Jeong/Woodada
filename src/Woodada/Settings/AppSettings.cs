using Avalonia.Media;
using Woodada.Core;

namespace Woodada.Settings;

/// <summary>settings.json 루트.</summary>
public sealed class AppSettings
{
    public const int MinSize = 16;
    public const int MaxSize = 72;

    /// <summary>까망. 색이 깨졌을 때도 이 색으로 돌린다. 무료 색이라 잠금에 걸리지 않는다.</summary>
    public const string DefaultColor = "#1E1E22";

    /// <summary>오버레이 갱신 주기. 상주 앱이라 60보다 30이 CPU에 덜 부담된다.</summary>
    public int FramesPerSecond { get; set; } = 30;

    /// <summary>전체 화면 앱(게임·발표)이 떠 있으면 숨김.</summary>
    public bool HideInFullScreen { get; set; } = true;

    /// <summary>
    /// 러너가 달릴 모니터(IPlatform.ScreenKey: Windows는 \.\DISPLAY2 같은 장치 이름, macOS는 display:ID).
    /// null이거나 지금 없는 모니터면 주 모니터. 이름은 예전 설정 파일과 맞추려고 그대로 둔다.
    /// </summary>
    public string? MonitorDeviceName { get; set; }

    public List<RunnerConfig> Runners { get; set; } = new();

    /// <summary>
    /// 처음 설치했을 때 "시작 시 실행"을 기본으로 켜 주는 일을 이미 했는지. 한 번 하고 나면 사용자가 끈 걸 다시 켜지 않는다.
    /// 이 값이 없는 예전 설정 파일은 이미 쓰던 사용자라 true로 읽혀, 지금 고른 대로 둔다. 새 설치만 CreateDefault의 false로 시작한다.
    /// </summary>
    public bool StartupDefaultApplied { get; set; } = true;

    public static AppSettings CreateDefault() => new()
    {
        StartupDefaultApplied = false,
        Runners =
        {
            // 처음부터 갖고 있는 색은 까망·하양뿐이다. 나머지는 생선으로 잠금 해제한다(RewardCatalog).
            new RunnerConfig { Id = "cpu",    Metric = MetricKind.Cpu,    SpriteId = "cat", Color = DefaultColor, Size = 28, Enabled = true },
            new RunnerConfig { Id = "memory", Metric = MetricKind.Memory, SpriteId = "cat", Color = "#ECE7DF", Size = 28, Enabled = true },
        },
    };

    /// <summary>
    /// 예전 버전 파일이거나 손으로 고친 파일이어도 CPU·메모리 러너가 반드시 하나씩 있도록 보정한다.
    /// </summary>
    public void Normalize()
    {
        Runners ??= new();
        var defaults = CreateDefault();
        foreach (var d in defaults.Runners)
        {
            if (!Runners.Any(r => r.Id == d.Id)) Runners.Add(d);
        }
        foreach (var r in Runners)
        {
            r.Size = Math.Clamp(r.Size, MinSize, MaxSize);
            // 출시 전 러너는 입을 수 없다. 개발용으로 써 본 설정 파일에 남아 있어도 고양이로 돌린다.
            if (string.IsNullOrWhiteSpace(r.SpriteId) || Sprites.SpriteRegistry.IsUpcoming(r.SpriteId)) r.SpriteId = "cat";
            if (!ColorUtil.TryParse(r.Color, out _)) r.Color = DefaultColor;
            // 스페셜 스킨은 아직 출시 전이라 개발용 빌드에서만 입는다. 모르는 Id도 지운다.
            if (!AppInfo.IsDev || Sprites.RunnerSkins.Parse(r.Skin) == Sprites.RunnerSkin.None) r.Skin = null;
        }
        FramesPerSecond = Math.Clamp(FramesPerSecond, 10, 60);
    }

    public AppSettings Clone() => SettingsStore.Deserialize(SettingsStore.Serialize(this));
}

/// <summary>러너 한 마리 설정.</summary>
public sealed class RunnerConfig
{
    /// <summary>고정 식별자. 같은 지표에 러너를 여러 마리 둘 수 있게 Metric과 분리했다.</summary>
    public string Id { get; set; } = "";
    public MetricKind Metric { get; set; }
    /// <summary>스프라이트 종류("cat", "dachshund", "rabbit" …). 목록은 SpriteRegistry에 있다.</summary>
    public string SpriteId { get; set; } = "cat";
    public bool Enabled { get; set; } = true;
    public string Color { get; set; } = AppSettings.DefaultColor;
    /// <summary>스페셜 스킨 Id("ghost" …). null이면 색만 칠한다. 없을 때는 파일에 쓰지 않는다.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Skin { get; set; }
    /// <summary>러너 높이. 화면 단위라 Windows는 물리 픽셀, macOS는 포인트(레티나에서도 같은 크기로 보인다).</summary>
    public int Size { get; set; } = 28;
}

public static class ColorUtil
{
    /// <summary>"#RRGGBB" 같은 색 문자열. 설정 파일에는 늘 이 형식으로 저장한다.</summary>
    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        return !string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.Trim(), out color);
    }

    public static Color Parse(string? hex, Color fallback) => TryParse(hex, out var c) ? c : fallback;

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>검정 고양이 눈이 안 보이는 걸 막으려고 밝기를 본다.</summary>
    public static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>색상환에서 두 색상(0~360°) 사이 거리(0~180°).</summary>
    public static double HueDistance(double a, double b)
    {
        double d = Math.Abs(a - b) % 360;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>
    /// 스킨 효과 색과 겹칠 만큼 색이 뚜렷한지. 까망·하양·회색처럼 무채색이거나 아주 밝거나 어두우면 아니다.
    /// </summary>
    public static bool IsChromatic(Color c, double minSaturation)
    {
        var hsl = c.ToHsl();
        return hsl.S >= minSaturation && hsl.L is >= 0.15 and <= 0.85;
    }
}
