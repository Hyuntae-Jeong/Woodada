using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>프로필 표의 러너 아이콘. 설정한 색으로 멈춰 선 자세를 그린다.</summary>
internal sealed class RunnerIcon : Control
{
    private readonly IRunnerSprite _sprite;
    private readonly Color _color;

    public RunnerIcon(RunnerConfig runner)
    {
        _sprite = SpriteRegistry.Get(runner.SpriteId);
        _color = ColorUtil.Parse(runner.Color, Color.Black);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var size = _sprite.Measure(Math.Max(4, Height - 6));
        var box = new RectangleF((Width - size.Width) / 2, (Height - size.Height) / 2, size.Width, size.Height);
        _sprite.Draw(e.Graphics, box, new RunnerPose(1, 0.0, 0.3), _color);
    }
}
