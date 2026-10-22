using System.Drawing.Drawing2D;

namespace Woodada.UI;

/// <summary>둥근 흰 카드. Highlighted면 테두리를 강조색으로 굵게 그린다.</summary>
internal class CardPanel : Panel
{
    private bool _highlighted;

    public CardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = GameArt.Background;
        Padding = new Padding(12, 10, 12, 10);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Highlighted
    {
        get => _highlighted;
        set
        {
            if (_highlighted == value) return;
            _highlighted = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;
        float inset = 1.5f * s;
        var r = new RectangleF(inset, inset, Width - inset * 2, Height - inset * 2);
        GameArt.FillRound(g, r, 10 * s, GameArt.Card,
            _highlighted ? GameArt.Accent : GameArt.CardBorder, _highlighted ? 2.5f * s : 1f * s);
    }
}

/// <summary>위쪽 탭 대신 쓰는 알약 모양 버튼.</summary>
internal sealed class NavPill : Control
{
    private bool _selected;
    private bool _hover;

    public NavPill(string text)
    {
        Text = text;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        Size = new Size(96, 32);
        Margin = new Padding(0, 0, 6, 0);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        if (_selected) GameArt.FillRound(g, r, Height / 2f, GameArt.Accent);
        else if (_hover) GameArt.FillRound(g, r, Height / 2f, GameArt.AccentSoft);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, _selected ? GameArt.OnAccent : GameArt.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
