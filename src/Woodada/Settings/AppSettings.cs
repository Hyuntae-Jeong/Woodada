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

    /// <summary>러너가 달릴 모니터(Screen.DeviceName, 예: \.\DISPLAY2). null이면 주 모니터.</summary>
    public string? MonitorDeviceName { get; set; }

    public List<RunnerConfig> Runners { get; set; } = new();

    public static AppSettings CreateDefault() => new()
    {
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
    /// <summary>러너 높이(px, 물리 픽셀).</summary>
    public int Size { get; set; } = 28;
}

public static class ColorUtil
{
    public static bool TryParse(string? hex, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try
        {
            color = ColorTranslator.FromHtml(hex);
            return !color.IsEmpty;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static Color Parse(string? hex, Color fallback) => TryParse(hex, out var c) ? c : fallback;

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Scale(Color c, double f) => Color.FromArgb(c.A,
        (int)Math.Clamp(c.R * f, 0, 255), (int)Math.Clamp(c.G * f, 0, 255), (int)Math.Clamp(c.B * f, 0, 255));

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    /// <summary>검정 고양이 눈이 안 보이는 걸 막으려고 밝기를 본다.</summary>
    public static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
}
