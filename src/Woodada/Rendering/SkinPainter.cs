using Avalonia;
using Avalonia.Media;
using Woodada.Core;
using Woodada.Settings;
using Woodada.Sprites;

namespace Woodada.Rendering;

/// <summary>한 번 그릴 것이 무엇인지.</summary>
internal enum FrameLayer
{
    /// <summary>러너 몸(스킨 포함).</summary>
    Body,
    /// <summary>플래시 스킨 잔상: 한 색 실루엣을 Opacity만큼.</summary>
    Afterimage,
    /// <summary>플래시 스킨 불꽃 줄기. Box는 몸 자리다.</summary>
    FlashWisps,
}

/// <summary>그릴 것 하나. 대부분 러너 몸이고, 플래시 스킨은 잔상·불꽃 줄기가 앞에 더 붙는다.</summary>
/// <param name="LightBackground">밝은 작업표시줄·독 위인지. 스킨이 외곽선 톤을 고르는 데 쓴다.</param>
/// <param name="Usage">사용률(%). 불꽃 줄기 길이·진하기를 정한다.</param>
/// <param name="Seconds">불꽃이 일렁이는 시계.</param>
/// <param name="Phase">러너마다 다른 효과 위상.</param>
internal readonly record struct RunnerFrame(IRunnerSprite Sprite, Rect Box, RunnerPose Pose, Color Color,
    RunnerSkin Skin = RunnerSkin.None, bool LightBackground = false,
    FrameLayer Layer = FrameLayer.Body, double Opacity = 1, double Usage = 0, double Seconds = 0, double Phase = 0,
    GlitchShape? Glitch = null);

/// <summary>
/// 러너 한 마리를 스킨까지 입혀 그릴 것들(불꽃 줄기 · 잔상 · 몸)로 풀고, 그린다.
/// 오버레이와 설정 창(미리보기·스킨 칸·뽑기 무대)이 같은 코드로 그려서 어디서나 똑같은 모습이 나온다.
/// </summary>
internal static class SkinPainter
{
    /// <param name="originX">r.State.X에서 이만큼 뺀 자리에 그린다.</param>
    /// <param name="floorY">발이 닿는 선.</param>
    public static void AddRunner(List<RunnerFrame> frames, RunnerInstance r, double originX, double floorY, double seconds,
        bool lightBackground)
    {
        var size = r.BodySize;
        var skin = r.Skin;
        var color = ColorUtil.Parse(r.Config.Color, Colors.Orange);
        double lift = RunnerSkins.Lift(skin, (int)Math.Round(r.Height), seconds, r.EffectPhase);
        var box = new Rect(r.State.X - originX, floorY - size.Height - lift, size.Width, size.Height);
        var pose = new RunnerPose(r.State.Direction, r.State.GaitPhase, r.State.Intensity);
        if (skin == RunnerSkin.Flash)
        {
            // 아주 빠르면 몸이 달아오른다. 잔상의 몸 쪽 색도 이 색에서 이어지게 같은 색을 넘긴다.
            color = FlashSkin.BodyColor(color, r.Usage);
            // 플래시 효과는 몸 아래에 깔려야 하니 몸보다 먼저 넣는다.
            AddFlashTrail(frames, r, originX, box, pose, color, seconds);
        }
        GlitchShape? glitch = null;
        if (skin == RunnerSkin.Glitch)
        {
            GlitchSkin.Advance(r.Glitch, seconds, r.Usage);
            glitch = r.Glitch.Shape;
        }
        frames.Add(new RunnerFrame(r.Sprite, box, pose, color, skin, lightBackground, Glitch: glitch));
    }

    /// <summary>
    /// 플래시 스킨: 불꽃 줄기 → 잔상(꼬리 끝부터 몸 쪽으로) 순서로 넣는다. 잔상은 지나온 길을 따라 놓여서
    /// 벽에서 돌아서면 꼬리도 그 길을 따라 꺾인다.
    /// </summary>
    private static void AddFlashTrail(List<RunnerFrame> frames, RunnerInstance r, double originX, Rect box, RunnerPose pose,
        Color color, double seconds)
    {
        double usage = r.Usage;
        if (FlashSkin.Strength(usage) <= 0) return;

        frames.Add(new RunnerFrame(r.Sprite, box, pose, color, RunnerSkin.Flash,
            Layer: FrameLayer.FlashWisps, Usage: usage, Seconds: seconds, Phase: r.EffectPhase));

        double tail = FlashSkin.TailLength(box.Width, usage);
        for (int i = FlashSkin.AfterimageCount; i >= 1; i--)
        {
            double q = i / (double)FlashSkin.AfterimageCount;
            if (r.Behind(tail * q) is not { } s) continue;
            var ghostBox = new Rect(s.X - originX, box.Y, box.Width, box.Height);
            var ghostPose = new RunnerPose(s.Direction, s.GaitPhase, pose.Intensity);
            frames.Add(new RunnerFrame(r.Sprite, ghostBox, ghostPose, FlashSkin.TrailColor(q, color), RunnerSkin.Flash,
                Layer: FrameLayer.Afterimage, Opacity: FlashSkin.AfterimageOpacity(i, usage)));
        }
    }

    public static void Draw(DrawingContext ctx, IReadOnlyList<RunnerFrame> frames, double scaling)
    {
        foreach (var f in frames)
        {
            switch (f.Layer)
            {
                case FrameLayer.Afterimage:
                    f.Sprite.DrawSilhouette(ctx, f.Box, f.Pose, f.Color, f.Opacity, scaling);
                    break;
                case FrameLayer.FlashWisps:
                    FlashSkin.DrawWisps(ctx, f.Box, f.Pose.Facing, f.Usage, f.Seconds, f.Phase, f.Color, scaling);
                    break;
                case FrameLayer.Body when f.Skin == RunnerSkin.Glitch:
                    f.Sprite.DrawGlitch(ctx, f.Box, f.Pose, f.Color, scaling, f.Glitch);
                    break;
                default:
                    f.Sprite.Draw(ctx, f.Box, f.Pose, f.Color, scaling, f.Skin, f.LightBackground);
                    break;
            }
        }
    }
}
