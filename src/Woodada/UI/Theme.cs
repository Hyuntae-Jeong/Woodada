namespace Woodada.UI;

/// <summary>
/// 설정 창의 색 묶음. 화면 코드는 색을 직접 쓰지 않고 GameArt를 통해 여기서 읽는다.
/// 기본값은 연회색 바탕 + 생선과 같은 파랑 강조. 창을 만들 때 읽으므로 바꾼 뒤 새로 연 창부터 반영된다.
/// </summary>
public sealed class ThemePalette
{
    public string Name { get; init; } = "화이트 · 파랑";

    public Color Background { get; init; } = Color.FromArgb(246, 247, 249);
    public Color Card { get; init; } = Color.White;
    public Color CardBorder { get; init; } = Color.FromArgb(226, 230, 236);
    public Color Accent { get; init; } = Color.FromArgb(63, 132, 208);
    /// <summary>강조색 위 글자·체크 표시.</summary>
    public Color OnAccent { get; init; } = Color.White;
    public Color AccentSoft { get; init; } = Color.FromArgb(231, 240, 250);
    public Color Text { get; init; } = Color.FromArgb(43, 48, 56);
    public Color SubText { get; init; } = Color.FromArgb(123, 132, 146);
    public Color Good { get; init; } = Color.FromArgb(76, 160, 110);

    /// <summary>생선(재화) 색. 테마가 바뀌어도 생선은 파랑 계열로 둔다.</summary>
    public Color Fish { get; init; } = Color.FromArgb(92, 155, 213);
    /// <summary>생선 색과 짝을 이루는 글자색(살 수 있는 가격, 기록 탭의 생선 줄).</summary>
    public Color FishDark { get; init; } = Color.FromArgb(55, 108, 160);
    /// <summary>생선이 모자라 살 수 없는 가격표의 생선.</summary>
    public Color FishMuted { get; init; } = Color.FromArgb(170, 186, 202);

    public Color Locked { get; init; } = Color.FromArgb(238, 241, 245);
    public Color LockedHover { get; init; } = Color.FromArgb(229, 233, 239);

    /// <summary>맨 위 생선 띠의 가로 그라데이션.</summary>
    public Color HeaderFrom { get; init; } = Color.FromArgb(220, 234, 248);
    public Color HeaderTo { get; init; } = Color.FromArgb(241, 246, 252);
    public Color ProgressTrack { get; init; } = Color.FromArgb(150, 255, 255, 255);

    /// <summary>색 칸·러너 칸의 그림 받침. 고양이 색이 받침과 비슷하면 Alt를 깐다.</summary>
    public Color SwatchBack { get; init; } = Color.FromArgb(238, 241, 245);
    public Color SwatchBackAlt { get; init; } = Color.FromArgb(201, 208, 218);
    public Color Mystery { get; init; } = Color.FromArgb(227, 231, 237);

    /// <summary>고양이 카드의 미리보기(작업표시줄 흉내). 고양이 색이 바탕과 비슷하면 Alt 세트를 쓴다.</summary>
    public Color PreviewBack { get; init; } = Color.FromArgb(248, 249, 251);
    public Color PreviewBar { get; init; } = Color.FromArgb(236, 239, 243);
    public Color PreviewLine { get; init; } = Color.FromArgb(213, 218, 225);
    public Color PreviewText { get; init; } = Color.FromArgb(111, 119, 132);
    public Color PreviewAltBack { get; init; } = Color.FromArgb(210, 216, 224);
    public Color PreviewAltBar { get; init; } = Color.FromArgb(188, 196, 207);
    public Color PreviewAltLine { get; init; } = Color.FromArgb(167, 176, 189);
}

public static class Theme
{
    public static ThemePalette Current { get; set; } = new();
}
