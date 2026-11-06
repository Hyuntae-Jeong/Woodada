using System.Diagnostics;
using Avalonia;
using Avalonia.Media;
using Woodada.Core;
using Woodada.Rendering;
using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.UI;

/// <summary>
/// 제자리에서 달리는 러너(스킨 칸, 도감, 뽑기 무대). 아주 긴 트랙을 오른쪽으로 계속 달리게 하고 그릴 때만
/// 가운데로 당겨 온다. 그래서 플래시 잔상처럼 지나온 길이 필요한 효과도 오버레이와 똑같이 나온다.
/// </summary>
internal sealed class TreadmillRunner
{
    // 가장 빨라도 초당 몸 길이 7개라, 이 길이를 다 달리려면 몇 주가 걸린다.
    private static readonly Runway Endless = new(0, 1_000_000_000, 0, HangBelow: false);

    private readonly RunnerInstance _runner = new(new RunnerConfig(), SpriteRegistry.Get("cat"));
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<RunnerFrame> _frames = new();
    private readonly YarnPlay _yarn = new() { Tuning = YarnTuning.Treadmill };
    private readonly RunnerInstance[] _runners;
    private double _last = double.NaN;

    public TreadmillRunner()
    {
        _runner.State.Direction = 1;
        // 칸이 좁고 트랙이 끝없어서, 포탈을 고양이 가까이에 자주 연다.
        _runner.PortalTuning = PortalTuning.Treadmill;
        _runners = new[] { _runner };
    }

    /// <param name="centerX">몸 가운데가 올 X. 털실냥은 앞에 털실이 굴러가니 몸을 조금 뒤로 물려서 둘이 함께 이 자리에 오게 한다.</param>
    /// <param name="floorY">발이 닿는 선.</param>
    /// <param name="usage">이 사용률(%)로 달린다. 효과의 세기도 이것으로 정해진다.</param>
    public void Draw(DrawingContext ctx, double centerX, double floorY, double height, RunnerSkin skin, Color color,
        double usage, double scaling)
    {
        // 그릴 때마다 지난 시간만큼 달린다. 칸이 한동안 가려져 있었으면 한 번에 멀리 가지 않게 끊는다.
        double now = _clock.Elapsed.TotalSeconds;
        double dt = double.IsNaN(_last) ? 0 : Math.Min(0.25, now - _last);
        _last = now;

        _runner.HeightOverride = height;
        _runner.Config.Skin = RunnerSkins.ToId(skin);
        _runner.Config.Color = ColorUtil.ToHex(color);
        _runner.Update(dt, usage, Endless);
        _yarn.Step(dt, _runners, Endless);

        _frames.Clear();
        double width = _runner.BodySize.Width;
        if (skin == RunnerSkin.Yarn) centerX -= width * 0.4;
        double originX = _runner.State.X + width / 2 - centerX;
        SkinPainter.AddYarn(_frames, _yarn, front: false, originX, floorY, lightBackground: false);
        SkinPainter.AddRunner(_frames, _runner, originX, floorY, now, lightBackground: false);
        SkinPainter.AddYarn(_frames, _yarn, front: true, originX, floorY, lightBackground: false);
        SkinPainter.Draw(ctx, _frames, scaling);
    }
}
