using Avalonia;
using Avalonia.Input;
using Avalonia.Media;

namespace Woodada.UI;

internal enum TileState
{
    /// <summary>선택한 고양이가 지금 입고 있다.</summary>
    Equipped,
    /// <summary>갖고 있다. 누르면 입힌다.</summary>
    Owned,
    /// <summary>잠겨 있고 생선이 충분하다.</summary>
    Buyable,
    /// <summary>잠겨 있고 생선이 모자라다.</summary>
    TooExpensive,
    /// <summary>아직 살 수 없다(앞 칸을 먼저 열어야 함, 준비 중 등).</summary>
    Unavailable,
    /// <summary>그림까지 나온 출시 예정 러너. 보여 주기만 하고 데려올 수는 없다.</summary>
    ComingSoon,
}

/// <summary>상점 칸 하나. 위에는 그림, 아래에는 이름과(잠겼으면) 생선 가격.</summary>
internal sealed class ShopTile : ClickableControl
{
    private TileState _state;
    private bool _previewing;
    private Action<DrawingContext, Rect, double>? _art;

    public ShopTile()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>그림 영역에 그린다(상자, 렌더 배율). 잠긴 칸이면 그 위에 흐린 막과 자물쇠를 얹는다.</summary>
    public Action<DrawingContext, Rect, double>? Art
    {
        get => _art;
        set
        {
            _art = value;
            InvalidateVisual();
        }
    }

    public string Caption { get; set; } = "";

    public int Price { get; set; }

    public TileState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            InvalidateVisual();
        }
    }

    public bool IsLocked => _state is TileState.Buyable or TileState.TooExpensive or TileState.Unavailable;

    /// <summary>사기 전에 입어 보는 중인 칸. 잠긴 채로 두되 그림을 덜 가리고 강조 테두리를 두른다.</summary>
    public bool Previewing
    {
        get => _previewing;
        set
        {
            if (_previewing == value) return;
            _previewing = value;
            InvalidateVisual();
        }
    }

    public void Redraw() => InvalidateVisual();

    protected override void Paint(DrawingContext ctx)
    {
        var outer = new Rect(1.5, 1.5, Bounds.Width - 3, Bounds.Height - 3);
        if (outer.Width <= 0 || outer.Height <= 0) return;

        if (_state == TileState.ComingSoon)
        {
            PaintComingSoon(ctx, outer);
            return;
        }

        Color fill = IsLocked ? GameArt.Locked : GameArt.Card;
        if (Hover && _state != TileState.Unavailable) fill = IsLocked ? GameArt.LockedHover : GameArt.AccentSoft;
        Color border = _previewing ? GameArt.Accent : _state switch
        {
            TileState.Equipped => GameArt.Accent,
            TileState.Buyable => GameArt.Fish,
            _ => GameArt.CardBorder,
        };
        double borderWidth = _previewing ? 2.5 : _state switch
        {
            TileState.Equipped => 2.5,
            TileState.Buyable => 1.6,
            _ => 1,
        };
        GameArt.FillRound(ctx, outer, 8, fill, border, borderWidth);

        bool showPrice = IsLocked;
        const double textLine = 15;
        double bottomText = showPrice ? textLine * 2 : textLine;
        var art = new Rect(outer.X + 4, outer.Y + 5, outer.Width - 8, Math.Max(1, outer.Height - bottomText - 9));

        _art?.Invoke(ctx, art, RenderScaling);
        if (IsLocked)
        {
            // 그림을 흐리게 덮고 오른쪽 위에 자물쇠.
            byte veilAlpha = (byte)(_previewing ? 40 : _state == TileState.Buyable ? 90 : 150);
            ctx.FillRectangle(GameArt.Brush(Settings.ColorUtil.WithAlpha(GameArt.Locked, veilAlpha)), art);
            const double lockSize = 11;
            GameArt.DrawLock(ctx, new Rect(outer.Right - lockSize - 6, outer.Y + 6, lockSize * 0.8, lockSize),
                _state == TileState.Buyable ? GameArt.FishDark : GameArt.SubText);
        }
        if (_state == TileState.Equipped)
        {
            // 오른쪽 위 체크 배지
            const double d = 15;
            var badge = new Rect(outer.Right - d - 4, outer.Y + 4, d, d);
            ctx.DrawEllipse(GameArt.Brush(GameArt.Accent), null, badge);
            var check = new StreamGeometry();
            using (var g = check.Open())
            {
                g.BeginFigure(new Point(badge.X + d * 0.27, badge.Y + d * 0.52), false);
                g.LineTo(new Point(badge.X + d * 0.44, badge.Y + d * 0.68));
                g.LineTo(new Point(badge.X + d * 0.74, badge.Y + d * 0.34));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(GameArt.Brush(GameArt.OnAccent), 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), check);
        }

        var captionRect = new Rect(outer.X, art.Bottom + 2, outer.Width, textLine);
        var captionColor = _state == TileState.Unavailable ? GameArt.SubText : GameArt.Text;
        GameArt.DrawCentered(ctx, GameArt.Format(Caption, GameArt.FontSmall, captionColor,
            _state == TileState.Equipped ? FontWeight.Bold : FontWeight.Normal), captionRect);

        if (showPrice)
        {
            var weight = _state == TileState.Buyable ? FontWeight.Bold : FontWeight.Normal;
            string price = GameArt.FormatCount(Price);
            const double h = 12;
            var size = GameArt.MeasurePrice(price, GameArt.FontSmall, weight, h);
            var origin = new Point(outer.X + (outer.Width - size.Width) / 2, captionRect.Bottom + (textLine - h) / 2);
            var textColor = _state == TileState.Buyable ? GameArt.FishDark : GameArt.SubText;
            var fishColor = _state == TileState.Buyable ? GameArt.Fish : GameArt.FishMuted;
            GameArt.DrawPrice(ctx, price, GameArt.FontSmall, weight, origin, h, textColor, fishColor);
        }
    }

    /// <summary>
    /// 출시 예정 칸: 자물쇠 대신 강조색 점선 테두리와 "COMING SOON" 배지, 아래에 "곧 만나요!".
    /// 그림은 살짝만 흐리게 해서 어떤 친구가 오는지 보이게 하고, 마우스를 올리면 흐림을 걷는다.
    /// </summary>
    private void PaintComingSoon(DrawingContext ctx, Rect outer)
    {
        GameArt.FillRound(ctx, outer, 8, Hover ? GameArt.AccentSoft : GameArt.Card);
        var dash = new Pen(GameArt.Brush(GameArt.Accent), 1.4, new DashStyle(new double[] { 4, 3 }, 0));
        ctx.DrawRectangle(null, dash, outer, 8, 8);

        const double textLine = 15;
        var art = new Rect(outer.X + 4, outer.Y + 5, outer.Width - 8, Math.Max(1, outer.Height - textLine * 2 - 9));
        _art?.Invoke(ctx, art, RenderScaling);
        if (!Hover) ctx.FillRectangle(GameArt.Brush(Settings.ColorUtil.WithAlpha(GameArt.Card, 70)), art);

        // 왼쪽 위 배지
        var badgeText = GameArt.Format("COMING SOON", 9, GameArt.OnAccent, FontWeight.Bold);
        var badge = new Rect(art.X + 4, art.Y + 4, badgeText.Width + 10, badgeText.Height + 4);
        GameArt.FillRound(ctx, badge, badge.Height / 2, GameArt.Accent);
        GameArt.DrawCentered(ctx, badgeText, badge);

        var captionRect = new Rect(outer.X, art.Bottom + 2, outer.Width, textLine);
        GameArt.DrawCentered(ctx, GameArt.Format(Caption, GameArt.FontSmall, GameArt.Text), captionRect);
        var soonRect = new Rect(outer.X, captionRect.Bottom, outer.Width, textLine);
        GameArt.DrawCentered(ctx, GameArt.Format("곧 만나요!", GameArt.FontSmall, GameArt.Accent, FontWeight.Bold), soonRect);
    }
}
