using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Woodada.Rewards;

namespace Woodada.UI;

/// <summary>
/// 냥뽑기 탭 도감의 한 칸: 작은 무대 + 이름 + 등급. 아직 없는 스킨은 실루엣과 "???"만 보이고 등급도 숨긴다.
/// 모은 스킨은 마우스를 올리면 무대에 "프로필 보기" 덮개가 뜨고, 누르면 홈페이지의 그 스킨 프로필(docs/cats/)이 열린다.
/// 없는 스킨은 정체를 미리 보지 않도록 누를 수 없다.
/// </summary>
internal sealed class DexSlot : CardPanel
{
    private readonly SkinRunnerView _view;
    private readonly TextBlock _name = new() { FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
    private readonly TextBlock _tier = new() { FontSize = GameArt.FontSmall, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, FontWeight = FontWeight.SemiBold };
    private bool _linkable;
    private bool _hover;

    public DexSlot(SkinInfo info)
    {
        Info = info;
        _view = new SkinRunnerView { Skin = info.Skin, Height = 54 };
        Padding = new Thickness(6);
        Margin = new Thickness(4);
        Child = new StackPanel { Children = { _view, _name, _tier } };
    }

    public SkinInfo Info { get; }

    /// <summary>가졌는지에 맞춰 칸을 다시 채운다.</summary>
    public void Show(bool owned)
    {
        _view.Owned = owned;
        _name.Text = owned ? Info.Name : "???";
        // 칸 높이는 UniformGrid가 맞춰 주므로 등급 줄이 빠져도 칸이 들쭉날쭉하지 않다.
        _tier.IsVisible = owned;
        _tier.Text = $"{new string('★', Info.Stars)} {Info.Tier}";
        _tier.Foreground = GameArt.Brush(GameArt.TierInk(Info.Stars));
        // 홈페이지에 프로필이 있는 건 공개한 스킨뿐이다. 개발용 빌드에서만 보이는 스킨은 누를 수 없다.
        _linkable = owned && Info.Released;
        Cursor = _linkable ? new Cursor(StandardCursorType.Hand) : null;
        ApplyHover();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        ApplyHover();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        ApplyHover();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_linkable || e.InitialPressMouseButton != MouseButton.Left) return;
        if (!new Rect(Bounds.Size).Contains(e.GetPosition(this))) return;
        // 주소는 누를 때 만든다. 창을 연 뒤 언어를 바꿔도 지금 언어로 열린다.
        if (ExternalLinks.SkinProfileUrl(Info.Skin) is { } url) ExternalLinks.Open(url);
    }

    private void ApplyHover()
    {
        bool on = _hover && _linkable;
        BorderBrush = GameArt.Brush(on ? GameArt.Accent : GameArt.CardBorder);
        _view.LinkHover = on;
        _view.InvalidateVisual();
    }
}
