namespace Woodada.Stats;

/// <summary>프로필에 보여줄 거리 문구.</summary>
public static class DistanceText
{
    /// <summary>1m 미만은 cm, 1km 미만은 m, 그 이상은 km.</summary>
    public static string Format(double millimeters)
    {
        double m = millimeters / 1000;
        if (m < 1) return $"{millimeters / 10:0} cm";
        if (m < 1000) return $"{m:0.0} m";
        return $"{m / 1000:0.00} km";
    }

    /// <summary>
    /// 가까운 것부터 먼 것 순. 달린 거리가 넘어선 가장 먼 기준으로 비교한다.
    /// 다음 기준이 바로 앞 기준의 10배를 넘지 않게 해서, 숫자가 10을 넘지 않는다 (마지막 기준인 달은 빼고).
    /// 영어판은 한국 지명 대신 어디서나 알 만한 기준을 쓴다. 같은 규칙(10배 이하)을 지킨다.
    /// </summary>
    private static readonly (double Meters, Func<double, string> Describe)[] KoreanLandmarks =
    {
        (105, n => $"축구장 {n:0.#}개를 이어 달린 거리예요"),
        (555, n => $"롯데월드타워를 {n:0.#}번 오른 높이예요"),
        (1_947, n => $"한라산을 {n:0.#}번 오른 높이예요"),
        (8_849, n => $"에베레스트를 {n:0.#}번 오른 높이예요"),
        (42_195, n => $"마라톤을 {n:0.#}번 완주한 거리예요"),
        (150_000, n => $"서울에서 대전까지 {n:0.#}번 간 거리예요"),
        (400_000, n => $"서울에서 부산까지 {n:0.#}번 간 거리예요"),
        (1_160_000, n => $"서울에서 도쿄까지 {n:0.#}번 간 거리예요"),
        (3_700_000, n => $"서울에서 방콕까지 {n:0.#}번 간 거리예요"),
        (11_000_000, n => $"서울에서 뉴욕까지 {n:0.#}번 간 거리예요"),
        (40_075_000, n => $"지구를 {n:0.#}바퀴 돈 거리예요"),
        (384_400_000, n => $"달까지 {n:0.##}번 간 거리예요"),
    };

    private static readonly (double Meters, Func<double, string> Describe)[] EnglishLandmarks =
    {
        (105, n => $"That's {Count(n, "soccer field", "soccer fields")} end to end"),
        (828, n => $"Like climbing the Burj Khalifa {Times(n)}"),
        (3_776, n => $"Like climbing Mount Fuji {Times(n)}"),
        (8_849, n => $"Like climbing Everest {Times(n)}"),
        (42_195, n => $"Like running {Count(n, "marathon", "marathons")}"),
        (344_000, n => $"Like going from London to Paris {Times(n)}"),
        (1_434_000, n => $"Like going from London to Rome {Times(n)}"),
        (3_944_000, n => $"Like going from New York to Los Angeles {Times(n)}"),
        (10_850_000, n => $"Like going from New York to Tokyo {Times(n)}"),
        (40_075_000, n => $"Like going around the Earth {Times(n)}"),
        (384_400_000, n => $"Like going to the Moon {Times(n, "0.##")}"),
    };

    private static string Times(double n, string format = "0.#")
    {
        string text = n.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        return text == "1" ? "once" : text == "2" ? "twice" : $"{text} times";
    }

    private static string Count(double n, string one, string many)
    {
        string text = n.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        return $"{text} {(text == "1" ? one : many)}";
    }

    public static string Compare(double millimeters)
    {
        var landmarks = Loc.Korean ? KoreanLandmarks : EnglishLandmarks;
        double meters = millimeters / 1000;
        if (meters < landmarks[0].Meters)
            return meters <= 0
                ? Loc.T("아직 출발 전이에요", "Hasn't left the starting line yet")
                : Loc.T($"축구장 끝까지 {meters / landmarks[0].Meters:0%} 왔어요", $"{meters / landmarks[0].Meters:0%} of the way across a soccer field");

        var (refMeters, describe) = landmarks.Last(l => meters >= l.Meters);
        return describe(meters / refMeters);
    }
}
