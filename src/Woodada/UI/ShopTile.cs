using System.Drawing.Drawing2D;

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
}

/// <summary>상점 칸 하나. 위에는 그림, 아래에는 이름과(잠겼으면) 생선 가격.</summary>
internal sealed class ShopTile : Control
{
    private TileState _state;
    private bool _hover;

    public ShopTile()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
    }

    /// <summary>그림 영역에 그린다. 잠긴 칸이면 그 위에 흐린 막과 자물쇠를 얹는다.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Action<Graphics, RectangleF>? Art { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Caption { get; set; } = "";

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Price { get; set; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public TileState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            Invalidate();
        }
    }

    public bool IsLocked => _state is TileState.Buyable or TileState.TooExpensive or TileState.Unavailable;

    private bool _previewing;

    /// <summary>사기 전에 입어 보는 중인 칸. 잠긴 채로 두되 그림을 덜 가리고 강조 테두리를 두른다.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Previewing
    {
        get => _previewing;
        set
        {
            if (_previewing == value) return;
            _previewing = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;
        var outer = new RectangleF(1.5f * s, 1.5f * s, Width - 3 * s, Height - 3 * s);

        Color fill = IsLocked ? GameArt.Locked : GameArt.Card;
        if (_hover && _state != TileState.Unavailable) fill = IsLocked ? GameArt.LockedHover : GameArt.AccentSoft;
        Color border = _previewing ? GameArt.Accent : _state switch
        {
            TileState.Equipped => GameArt.Accent,
            TileState.Buyable => GameArt.Fish,
            _ => GameArt.CardBorder,
        };
        float borderWidth = _previewing ? 2.5f * s : _state switch
        {
            TileState.Equipped => 2.5f * s,
            TileState.Buyable => 1.6f * s,
            _ => 1f * s,
        };
        GameArt.FillRound(g, outer, 8 * s, fill, border, borderWidth);

        bool showPrice = IsLocked;
        float textLine = 15 * s;
        float bottomText = showPrice ? textLine * 2 : textLine;
        var art = new RectangleF(outer.Left + 4 * s, outer.Top + 5 * s, outer.Width - 8 * s, outer.Height - bottomText - 9 * s);

        Art?.Invoke(g, art);
        if (IsLocked)
        {
            // 그림을 흐리게 덮고 오른쪽 위에 자물쇠.
            int veilAlpha = _previewing ? 40 : _state == TileState.Buyable ? 90 : 150;
            using (var veil = new SolidBrush(Color.FromArgb(veilAlpha, GameArt.Locked)))
                g.FillRectangle(veil, art);
            float lockSize = 11 * s;
            GameArt.DrawLock(g, new RectangleF(outer.Right - lockSize - 6 * s, outer.Top + 6 * s, lockSize * 0.8f, lockSize),
                _state == TileState.Buyable ? GameArt.FishDark : GameArt.SubText);
        }
        if (_state == TileState.Equipped)
        {
            // 오른쪽 위 체크 배지
            float d = 15 * s;
            var badge = new RectangleF(outer.Right - d - 4 * s, outer.Top + 4 * s, d, d);
            using (var b = new SolidBrush(GameArt.Accent)) g.FillEllipse(b, badge);
            using var pen = new Pen(GameArt.OnAccent, 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[]
            {
                new PointF(badge.Left + d * 0.27f, badge.Top + d * 0.52f),
                new PointF(badge.Left + d * 0.44f, badge.Top + d * 0.68f),
                new PointF(badge.Left + d * 0.74f, badge.Top + d * 0.34f),
            });
        }

        using var captionFont = new Font("Segoe UI", 8.25f, _state == TileState.Equipped ? FontStyle.Bold : FontStyle.Regular);
        var captionRect = new Rectangle((int)outer.Left, (int)(art.Bottom + 2 * s), (int)outer.Width, (int)textLine);
        var captionColor = _state == TileState.Unavailable ? GameArt.SubText : GameArt.Text;
        TextRenderer.DrawText(g, Caption, captionFont, captionRect, captionColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (showPrice)
        {
            using var priceFont = new Font("Segoe UI", 8.25f, _state == TileState.Buyable ? FontStyle.Bold : FontStyle.Regular);
            string price = GameArt.FormatCount(Price);
            float h = 12 * s;
            var size = GameArt.MeasurePrice(g, price, priceFont, h);
            var origin = new PointF(outer.Left + (outer.Width - size.Width) / 2, captionRect.Bottom + (textLine - h) / 2);
            var textColor = _state == TileState.Buyable ? GameArt.FishDark : GameArt.SubText;
            var fishColor = _state == TileState.Buyable ? GameArt.Fish : GameArt.FishMuted;
            GameArt.DrawPrice(g, price, priceFont, origin, h, textColor, fishColor);
        }
    }
}
