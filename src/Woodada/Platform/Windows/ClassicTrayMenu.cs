using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Woodada.Platform.Windows;

/// <summary>
/// Windows 트레이 메뉴를 예전(WinForms ContextMenuStrip) 모양으로 그리는 스타일.
/// Avalonia는 트레이 메뉴를 자기 창(TrayPopupRoot) 안의 MenuFlyoutPresenter로 그린다.
/// 그 창 안에만 걸리므로 설정 창 같은 다른 메뉴는 Fluent 모양 그대로다.
/// </summary>
internal static class ClassicTrayMenu
{
    private const string PopupRootTypeName = "Avalonia.Win32.TrayIconImpl+TrayPopupRoot, Avalonia.Win32";
    private const string TrayClass = "woodada-tray-menu";

    private static readonly IBrush Back = new SolidColorBrush(Color.FromRgb(242, 242, 242));
    private static readonly IBrush Edge = new SolidColorBrush(Color.FromRgb(151, 151, 151));
    private static readonly IBrush Highlight = new SolidColorBrush(Color.FromRgb(204, 232, 255));
    private static readonly IBrush HighlightBorder = new SolidColorBrush(Color.FromRgb(153, 209, 255));
    private static readonly IBrush Ink = Brushes.Black;

    private const double ItemHeight = 22;
    private const double TextLeft = 32;   // 왼쪽 체크 칸 너비만큼 띄운다
    private const double TextRight = 24;

    /// <summary>
    /// 트레이 메뉴 창에 표시를 붙이는 처리를 걸고 스타일을 돌려준다.
    /// 트레이 메뉴 창 형식을 찾지 못하면(Avalonia 내부가 바뀐 경우) null. 그때는 기본 모양으로 둔다.
    /// </summary>
    public static IStyle? Create()
    {
        var popupRoot = Type.GetType(PopupRootTypeName, throwOnError: false);
        if (popupRoot is null) return null;

        // 트레이 메뉴 창과 안쪽 메뉴는 스타일에서 그냥 Window·MenuFlyoutPresenter로 보인다.
        // 그래서 창에 메뉴가 들어가는 순간(아직 화면에 뜨기 전) 클래스를 붙여 그 창만 가려낸다.
        ContentControl.ContentProperty.Changed.AddClassHandler<Window>((window, _) =>
        {
            if (window.GetType() == popupRoot) window.Classes.Add(TrayClass);
        });

        var inTray = Selectors.OfType<Window>(null).Class(TrayClass).Descendant();
        var menu = inTray.OfType<MenuFlyoutPresenter>();
        var item = inTray.OfType<MenuItem>();

        return new Styles
        {
            new Style(_ => menu)
            {
                Setters = { new Setter(TemplatedControl.TemplateProperty, MenuTemplate()) },
            },
            new Style(_ => item)
            {
                Setters =
                {
                    new Setter(TemplatedControl.TemplateProperty, ItemTemplate()),
                    new Setter(TemplatedControl.FontSizeProperty, 12.0),
                    new Setter(TemplatedControl.FontWeightProperty, FontWeight.Normal),
                    new Setter(TemplatedControl.ForegroundProperty, Ink),
                },
            },
            // 예전 메뉴처럼 맨 위 항목(설정…)을 굵게 써서 기본 동작임을 보인다.
            new Style(_ => item.NthChild(0, 1))
            {
                Setters = { new Setter(TemplatedControl.FontWeightProperty, FontWeight.Bold) },
            },
        };
    }

    private static IControlTemplate MenuTemplate() =>
        new FuncControlTemplate<MenuFlyoutPresenter>((_, scope) => new Border
        {
            Background = Back,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            Child = new ItemsPresenter { Name = "PART_ItemsPresenter" }.RegisterInNameScope(scope),
        });

    private static IControlTemplate ItemTemplate() =>
        new FuncControlTemplate<MenuItem>((menuItem, scope) =>
        {
            // 마우스를 올린 줄: 하늘색 칸 + 파란 테두리.
            var hover = new Border
            {
                Background = Highlight,
                BorderBrush = HighlightBorder,
                BorderThickness = new Thickness(1),
                [!Visual.IsVisibleProperty] = menuItem[!MenuItem.IsSelectedProperty],
            };

            // 켜진 체크 항목: 왼쪽 칸에 체크 상자.
            var check = new Border
            {
                Width = ItemHeight - 2,
                Height = ItemHeight - 2,
                Margin = new Thickness(2, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Highlight,
                BorderBrush = HighlightBorder,
                BorderThickness = new Thickness(1),
                [!Visual.IsVisibleProperty] = menuItem[!MenuItem.IsCheckedProperty],
                Child = new Avalonia.Controls.Shapes.Path
                {
                    Data = Geometry.Parse("M 0.5,4.5 L 3.5,7.5 L 9.5,0.5"),
                    Stroke = Ink,
                    StrokeThickness = 1.6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var header = new ContentPresenter
            {
                Name = "PART_HeaderPresenter",
                Margin = new Thickness(TextLeft, 0, TextRight, 0),
                VerticalAlignment = VerticalAlignment.Center,
                [!ContentPresenter.ContentProperty] = menuItem[!HeaderedSelectingItemsControl.HeaderProperty],
                [!ContentPresenter.ForegroundProperty] = menuItem[!TemplatedControl.ForegroundProperty],
            }.RegisterInNameScope(scope);

            return new Panel
            {
                Height = ItemHeight,
                Background = Brushes.Transparent, // 글자 밖 빈 곳도 마우스를 받게
                Children = { hover, check, header },
            };
        });
}
