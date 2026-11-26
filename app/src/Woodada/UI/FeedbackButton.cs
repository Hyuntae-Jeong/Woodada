using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace Woodada.UI;

/// <summary>
/// "의견 보내기 ▾" 버튼. 누르면 보낼 곳을 고르는 작은 메뉴가 뜬다. 일반 탭 버전 줄과 공지 탭 맨 아래에 같은 것을 둔다.
/// 지금은 모두 GitHub(계정 필요)로 가고, 항목 앞 GitHub 아이콘으로 그걸 알린다. 계정 없이 보내는 구글 폼을 만들면 맨 위에 한 줄 더한다.
/// </summary>
internal static class FeedbackButton
{
    public static GameButton Create()
    {
        var button = new GameButton(Loc.T("의견 보내기 ▾", "Send feedback ▾")) { VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => Menu().ShowAt(button);
        return button;
    }

    private static MenuFlyout Menu() => new()
    {
        // 버튼이 창 아래쪽에 있어 메뉴는 위로 펼친다.
        Placement = PlacementMode.TopEdgeAlignedRight,
        Items =
        {
            // 어디로 가는지는 앞의 GitHub 아이콘으로 알린다.
            Item(Loc.T("버그 제보", "Report a bug"), ExternalLinks.BugReportUrl, BrandIcons.GitHub),
            Item(Loc.T("기능 제안", "Suggest a feature"), ExternalLinks.FeatureRequestUrl, BrandIcons.GitHub),
            Item(Loc.T("질문·아이디어 나누기", "Questions & ideas"), ExternalLinks.DiscussionsUrl, BrandIcons.GitHub),
        },
    };

    private static MenuItem Item(string header, Func<string> url, BrandIcon icon)
    {
        var item = new MenuItem
        {
            Header = header,
            FontSize = GameArt.FontBody,
            Icon = new PathIcon { Data = icon.Geometry, Width = 14, Height = 14, Foreground = GameArt.Brush(GameArt.Text) },
        };
        item.Click += (_, _) => ExternalLinks.Open(url());
        return item;
    }
}
