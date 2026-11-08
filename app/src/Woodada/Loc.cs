namespace Woodada;

public enum AppLanguage
{
    English,
    Korean,
}

/// <summary>
/// 화면 문구의 언어. settings.json의 language("en"·"ko")를 따른다.
/// 문구는 쓰는 자리에 한국어·영어를 나란히 적고 T(ko, en)로 지금 언어 쪽을 고른다.
/// 업데이트 기록(update.log)이나 예외 메시지처럼 사용자가 보지 않는 문구는 번역하지 않는다.
/// </summary>
public static class Loc
{
    public static AppLanguage Current { get; private set; } = AppLanguage.English;

    public static bool Korean => Current == AppLanguage.Korean;

    public static string T(string ko, string en) => Korean ? ko : en;

    /// <summary>바뀌었으면 true. 메뉴·설정 창은 이걸 보고 새 언어로 다시 그린다.</summary>
    public static bool Use(string? id)
    {
        var language = Parse(id);
        if (language == Current) return false;
        Current = language;
        return true;
    }

    public const string EnglishId = "en";
    public const string KoreanId = "ko";

    public static AppLanguage Parse(string? id) =>
        string.Equals(id, KoreanId, StringComparison.OrdinalIgnoreCase) ? AppLanguage.Korean : AppLanguage.English;

    public static string ToId(AppLanguage language) => language == AppLanguage.Korean ? KoreanId : EnglishId;

    /// <summary>새로 설치했을 때 쓸 언어. 컴퓨터 화면 언어가 한국어면 한국어, 그 밖에는 영어.</summary>
    public static string SystemDefaultId()
    {
        string? name = null;
        try
        {
            if (OperatingSystem.IsMacOS()) name = Platform.Mac.MacNative.PreferredLanguage();
        }
        catch (Exception)
        {
            // 못 읽으면 아래 .NET 값으로 정한다.
        }
        if (string.IsNullOrEmpty(name)) name = System.Globalization.CultureInfo.CurrentUICulture.Name;
        // "ko", "ko-KR", "ko_KR"처럼 앞 언어 부분만 본다. "kok"(콘칸어) 같은 다른 언어와 헷갈리지 않게.
        var primary = name.Split('-', '_')[0];
        return string.Equals(primary, KoreanId, StringComparison.OrdinalIgnoreCase) ? KoreanId : EnglishId;
    }
}

/// <summary>
/// 두 언어로 적어 둔 이름(색·스킨·러너). 목록은 앱이 뜰 때 한 번 만들지만 언어는 도중에 바뀔 수 있어서,
/// 문자열로 쓰일 때마다 지금 언어 쪽을 꺼낸다.
/// </summary>
public readonly record struct LocText(string Ko, string En)
{
    public override string ToString() => Loc.T(Ko, En);

    public static implicit operator string(LocText text) => text.ToString();
}
