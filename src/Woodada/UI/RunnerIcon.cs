using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
        _color = ColorUtil.Parse(runner.Color, Colors.Black);
    }

    public override void Render(DrawingContext ctx)
    {
        var size = _sprite.Measure(Math.Max(4, Bounds.Height - 6));
        var box = new Rect((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, size.Width, size.Height);
        _sprite.Draw(ctx, box, new RunnerPose(1, 0.0, 0.3), _color, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
    }
}
