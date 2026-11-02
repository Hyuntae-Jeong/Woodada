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
    /// <summary>얼음 스킨 서리·눈가루. Box는 몸 자리고, 바닥에 남은 것들은 Frost에서 읽는다.</summary>
    IceFrost,
}

/// <summary>
/// 그릴 것 하나. 대부분 러너 몸이고, 플래시 스킨은 잔상·불꽃 줄기가, 얼음 스킨은 서리가 앞에 더 붙는다.
/// 줄줄이 스킨의 새끼들은 작은 몸(Body)으로 엄마보다 먼저 들어간다.
/// </summary>
/// <param name="LightBackground">밝은 작업표시줄·독 위인지. 스킨이 외곽선 톤을 고르는 데 쓴다.</param>
/// <param name="Usage">사용률(%). 불꽃 줄기 길이·진하기를 정한다.</param>
/// <param name="Seconds">불꽃이 일렁이는 시계.</param>
/// <param name="Phase">러너마다 다른 효과 위상.</param>
/// <param name="Frost">얼음 스킨 서리. 그리는 쪽과 진행시키는 쪽이 모두 UI 스레드라 복사하지 않고 그대로 넘긴다.</param>
/// <param name="OriginX">서리처럼 트랙 좌표로 기억한 것을 그릴 때 빼는 값.</param>
internal readonly record struct RunnerFrame(IRunnerSprite Sprite, Rect Box, RunnerPose Pose, Color Color,
    RunnerSkin Skin = RunnerSkin.None, bool LightBackground = false,
    FrameLayer Layer = FrameLayer.Body, double Opacity = 1, double Usage = 0, double Seconds = 0, double Phase = 0,
    GlitchShape? Glitch = null, IceState? Frost = null, double OriginX = 0);

/// <summary>
/// 러너 한 마리를 스킨까지 입혀 그릴 것들(불꽃 줄기 · 잔상 · 서리 · 새끼 · 몸)로 풀고, 그린다.
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
        if (skin == RunnerSkin.Ice)
        {
            // 서리는 바닥에 남으니 몸보다 먼저 넣어 몸 아래에 깔리게 한다.
            IceSkin.Advance(r.Ice, seconds, r.Usage, r.State, size);
            frames.Add(new RunnerFrame(r.Sprite, box, pose, color, RunnerSkin.Ice, lightBackground,
                Layer: FrameLayer.IceFrost, Seconds: seconds, Frost: r.Ice, OriginX: originX));
        }
        if (skin == RunnerSkin.Kittens) AddKittens(frames, r, originX, floorY, box, pose, color, seconds);
        frames.Add(new RunnerFrame(r.Sprite, box, pose, color, skin, lightBackground, Glitch: glitch));
    }

    /// <summary>
    /// 줄줄이 스킨: 새끼들을 맨 끝부터 엄마 쪽으로 넣는다(엄마 가까운 새끼가 위에 오게). 새끼는 엄마가 지나온 길 위에 놓여서
    /// 벽에서 엄마가 돌아서면 새끼들도 그 자리까지 가서 돌아선다. 같은 스프라이트라 닥스훈트면 강아지들이 따라온다.
    /// </summary>
    private static void AddKittens(List<RunnerFrame> frames, RunnerInstance r, double originX, double floorY, Rect box,
        RunnerPose pose, Color color, double seconds)
    {
        var kitten = r.Sprite.Measure(r.Height * KittenSkin.Scale);
        var state = r.Kittens;
        KittenSkin.Advance(state, seconds, r.Usage, r.Odometer, box.Width, kitten.Width);
        for (int i = KittenSkin.MaxKittens - 1; i >= 0; i--)
        {
            double presence = state.Presence[i];
            if (presence <= 0.01) continue;
            if (r.Behind(KittenSkin.Distance(i, presence, box.Width, kitten.Width)) is not { } s) continue;
            // 지나온 길은 엄마 몸 왼쪽 끝 자리라, 몸 가운데끼리 맞춘다.
            double centerX = s.X + box.Width / 2 - originX;
            double lift = KittenSkin.Hop(state, i, r.Usage) * kitten.Height;
            var kitBox = new Rect(centerX - kitten.Width / 2, floorY - kitten.Height - lift, kitten.Width, kitten.Height);
            frames.Add(new RunnerFrame(r.Sprite, kitBox, new RunnerPose(s.Direction, state.Phase[i], pose.Intensity),
                KittenSkin.ColorFor(color, i), Opacity: Math.Min(1, presence * 1.4)));
        }
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
                case FrameLayer.IceFrost:
                    IceSkin.DrawFrost(ctx, f.Frost!, f.OriginX, f.Box.Bottom, f.Box.Height, f.LightBackground, f.Seconds, scaling);
                    break;
                case FrameLayer.Body when f.Skin == RunnerSkin.Glitch:
                    f.Sprite.DrawGlitch(ctx, f.Box, f.Pose, f.Color, scaling, f.Glitch);
                    break;
                case FrameLayer.Body when f.Opacity < 1:
                    // 합류하거나 처지는 중인 새끼
                    using (ctx.PushOpacity(f.Opacity)) f.Sprite.Draw(ctx, f.Box, f.Pose, f.Color, scaling, f.Skin, f.LightBackground);
                    break;
                default:
                    f.Sprite.Draw(ctx, f.Box, f.Pose, f.Color, scaling, f.Skin, f.LightBackground);
                    break;
            }
        }
    }
}
