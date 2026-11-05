// Copyright (c) 2026 Hyuntae Jeong. All rights reserved.
// 이 파일은 Woodada 스페셜 스킨 디자인으로, MIT License 적용 대상이 아닙니다. 사용 조건은 같은 폴더의 LICENSE를 보세요.
// This file is a Woodada special skin design and is NOT covered by the MIT License. See LICENSE in this folder.

using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Woodada.Core;

namespace Woodada.Sprites;

internal enum PortalPhase { Idle, Active, Closing }

/// <summary>러너 한 마리의 포탈 진행 상태. 구슬을 언제 쐈고, 포탈이 어디 열렸고, 지나갔는지.</summary>
public sealed class PortalState
{
    internal PortalPhase Phase;
    /// <summary>다음에 쏘기까지 남은 시간(초). Idle일 때만 줄어든다.</summary>
    internal double Wait;
    /// <summary>쏘기 시작한 뒤 지난 시간, 닫히기 시작한 뒤 지난 시간(초).</summary>
    internal double Clock, CloseAge;
    /// <summary>
    /// 두 포탈의 가운데 선(트랙 좌표). 0은 입구(파랑), 1은 출구(주황).
    /// 고양이 몸 가운데가 입구 선을 넘으면 출구로 옮겨진다. 출구로는 들어가지 않는다.
    /// </summary>
    internal readonly double[] At = new double[2];
    /// <summary>구슬이 떠난 자리(트랙 좌표, 고양이 입 앞). 쏘는 순간 정해진다.</summary>
    internal readonly double[] FromX = new double[2];
    /// <summary>Clock 기준으로 구슬을 쏜 때와, 떨어져 포탈이 열린 때.</summary>
    internal readonly double[] FireAt = new double[2];
    internal readonly double[] LandAt = new double[2];
    internal readonly bool[] Fired = new bool[2];
    /// <summary>입구로 들어가 출구 쪽으로 옮겨졌는지.</summary>
    internal bool Entered;
    /// <summary>Clock 기준으로 코가 입구에 닿은 때, 몸 가운데가 넘어가 출구로 옮겨진 때. 아직이면 NaN. 통과 이펙트의 시계다.</summary>
    internal double TouchAt = double.NaN, PopAt = double.NaN;
    /// <summary>쏠 때 달리던 방향. 입구는 이 방향 앞에 있고, 나올 때도 이 방향으로 나온다.</summary>
    internal int Dir = 1;
    /// <summary>
    /// 구슬 발사 간격·비행 시간에 곱하는 배율(0.2~1). 입구가 가까워 금방 닿을 때만 1보다 작아져서,
    /// 고양이가 닿기 전에 두 구슬이 다 떨어지게 한다.
    /// </summary>
    internal double ShotPace = 1;
    /// <summary>불꽃이 튀는 각도·도는 점의 시작 위치를 포탈마다 다르게.</summary>
    internal double Seed;

    public PortalState() => Reset();

    public bool Active => Phase != PortalPhase.Idle;

    /// <summary>처음 놓이거나 다른 모니터로 순간이동했을 때, 스킨을 벗었을 때. 열린 포탈을 치우고 다시 기다린다.</summary>
    public void Reset(double wait = PortalTuning.LongestWait)
    {
        Phase = PortalPhase.Idle;
        Wait = wait;
        Entered = false;
        TouchAt = PopAt = double.NaN;
        Clock = CloseAge = 0;
        Fired[0] = Fired[1] = false;
    }
}

/// <summary>
/// 포탈냥의 때·거리 값. 작업표시줄(Overlay)과 설정 창 미리보기는 보이는 폭이 달라서 따로 둔다.
/// 미리보기는 몇 초 보고 지나가는 곳이라, 바로 쏘고 자주 쏘며 입구·출구를 가까이 연다.
/// 길이는 몸 너비 대비, 시간은 초.
/// </summary>
/// <param name="FirstWait">스킨을 입은 뒤(또는 미리보기가 뜬 뒤) 처음 쏘기까지.</param>
/// <param name="MinWaitAtIdle">포탈이 닫히고 다음에 쏘기까지, CPU 0%일 때 범위.</param>
/// <param name="MinWaitAtFull">같은 것, CPU 100%일 때 범위. 사이는 √사용률로 잇는다.</param>
/// <param name="EntryLeadSeconds">입구까지 거리: 지금 속도로 이만큼 달리면 코가 입구에 닿는 자리.</param>
/// <param name="MinEntryLead">입구까지 코가 달릴 거리의 하한·상한.</param>
/// <param name="SecondShotDelay">파란 구슬을 쏘고 주황 구슬을 쏘기까지.</param>
/// <param name="MinFlight">구슬이 날아가는 시간. 가까우면 MinFlight, 멀면 MaxFlight.</param>
/// <param name="MinGap">출구가 입구와 떨어져야 하는 최소 거리.</param>
/// <param name="RetrySeconds">입구·출구를 열 자리가 없으면(벽이 가까우면) 이만큼 있다 다시 본다.</param>
/// <param name="Reach">출구를 고양이 앞뒤로 이만큼 안에만 연다. 제자리 달리기(끝없는 트랙)용. null이면 트랙 아무 데나.</param>
public sealed record PortalTuning(
    double FirstWait,
    double MinWaitAtIdle, double MaxWaitAtIdle, double MinWaitAtFull, double MaxWaitAtFull,
    double EntryLeadSeconds, double MinEntryLead, double MaxEntryLead,
    double SecondShotDelay, double MinFlight, double MaxFlight,
    double MinGap, double RetrySeconds, double? Reach)
{
    /// <summary>스킨을 입기 전까지 기다리는 값. 입는 순간 FirstWait로 바뀐다.</summary>
    public const double LongestWait = 11;

    /// <summary>
    /// 작업표시줄. 기다림: 평소(5~15%)가 대부분이라 직선으로 이으면 평소 빈도가 줄어서 √사용률 곡선을 쓴다.
    /// 평소 10%면 약 4.2~9.1초, 30%면 약 3.6~7.7초. 쏘고 통과하고 닫히기까지 2초쯤 걸리므로 가장 바쁠 때도 2.5초보다
    /// 짧게 하지 않는다(짧으면 포탈이 늘 떠 있다).
    /// 입구: 1.3초면 주황 구슬이 떨어지고(최대 0.35+0.40초) 조금 열린 뒤에 닿는다. 아주 느릴 때(초당 몸 0.4개)는 1.3초면
    /// 반 몸도 안 돼서 하한 1.2를 둔다(이때 약 3초). 상한 10은 가장 빠를 때(초당 몸 7개, 9.1개)보다 넉넉하다.
    /// </summary>
    public static readonly PortalTuning Overlay = new(
        FirstWait: 0.8,
        MinWaitAtIdle: 5, MaxWaitAtIdle: LongestWait, MinWaitAtFull: 2.5, MaxWaitAtFull: 5,
        EntryLeadSeconds: 1.3, MinEntryLead: 1.2, MaxEntryLead: 10,
        SecondShotDelay: 0.35, MinFlight: 0.18, MaxFlight: 0.40,
        MinGap: 2.0, RetrySeconds: 0.5, Reach: null);

    /// <summary>
    /// 고양이 카드의 작업표시줄 미리보기. 폭이 몸 6개 남짓이라 오버레이 값으로는 입구·출구 자리가 거의 안 난다.
    /// 입구를 0.6~1.8개 앞에, 출구를 1개만 떨어뜨려 연다. 그만큼 일찍 닿으니 주황 구슬도 빨리 쏘고 빨리 떨어진다.
    /// </summary>
    public static readonly PortalTuning Preview = new(
        FirstWait: 0.2,
        MinWaitAtIdle: 1.5, MaxWaitAtIdle: 3, MinWaitAtFull: 1.2, MaxWaitAtFull: 2.5,
        EntryLeadSeconds: 0.9, MinEntryLead: 0.6, MaxEntryLead: 1.8,
        SecondShotDelay: 0.15, MinFlight: 0.12, MaxFlight: 0.22,
        MinGap: 1.0, RetrySeconds: 0.15, Reach: null);

    /// <summary>
    /// 스킨 칸·뽑기 무대의 제자리 달리기. 고양이 좌우로 몸 1.5개쯤만 보여서 입구를 1.6개 안에 연다.
    /// 끝없는 트랙이라 출구도 몸 5개 안에만 연다(아무 데나 열면 영영 안 보인다).
    /// </summary>
    public static readonly PortalTuning Treadmill = Preview with { MaxEntryLead = 1.6, Reach = 5 };

    /// <param name="intensity">0~1 사용률. 포탈이 닫히는 순간 값으로 한 번만 정한다. 매 프레임 다시 재면 출렁일 때마다 들쭉날쭉하다.</param>
    public double NextWait(double intensity)
    {
        double k = Math.Sqrt(Math.Clamp(intensity, 0, 1));
        return PortalSkin.Rand(MinWaitAtIdle + (MinWaitAtFull - MinWaitAtIdle) * k, MaxWaitAtIdle + (MaxWaitAtFull - MaxWaitAtIdle) * k);
    }
}

/// <summary>
/// 포탈냥: 평소엔 그냥 달리다가 가끔 입에서 빛 구슬을 쏜다. 파란 구슬은 앞으로 날아가 약 1.3초 뒤 닿을 앞쪽에 입구를 열고,
/// 바로 이어 쏜 주황 구슬은 앞이든 뒤든 트랙 아무 데나 날아가 출구를 연다. 고양이가 입구로 들어가면 출구에서
/// 같은 방향으로 나온다. 지나가는 동안 들어간 부분은 입구에서 안 보이고, 나온 부분만 출구에서 보인다.
/// 겉모습·자리만 바뀌고 달린 거리·생선은 그대로다(순간이동한 거리는 세지 않는다).
/// 길이 값은 몸 높이(키)나 몸 너비 대비다. 값은 tools/skin_preview.py portal 미리보기로 맞춰 볼 수 있다.
/// </summary>
internal static class PortalSkin
{
    /// <summary>구슬이 이만큼(몸 너비) 이상 멀리 날아가면 가장 오래(PortalTuning.MaxFlight) 걸린다.</summary>
    private const double FarBodies = 20;

    /// <summary>열리는 데(살짝 넘쳤다 돌아옴), 닫히는 데 걸리는 시간(초).</summary>
    private const double OpenSeconds = 0.40;
    private const double CloseSeconds = 0.35;
    /// <summary>열릴 때 불꽃이 튀고 빛 고리가 퍼지는 시간(초).</summary>
    private const double BurstSeconds = 0.65;
    /// <summary>쏠 때 입 앞에서 번쩍이는 시간(초).</summary>
    private const double MuzzleSeconds = 0.14;

    /// <summary>포탈 키와 반폭(고양이 키 대비). 옆에서 비스듬히 본 문이라 좁고 길쭉하다.</summary>
    private const double PortalHeight = 1.3;
    private const double PortalHalfWidth = 0.17;
    /// <summary>구슬이 떠나는 입 높이(바닥에서, 키 대비)와, 입이 몸 가운데에서 앞으로 나온 정도(몸 너비 대비).</summary>
    private const double MouthHeight = 0.62;
    private const double MouthAhead = 0.42;

    /// <summary>
    /// 벽에서 떨어뜨리는 여유(몸 너비 대비). 포탈을 지나는 동안 몸이 벽에 닿아 돌아서면 반쯤 들어간 채 꼬이므로,
    /// 지나는 동안(가운데가 포탈 선 ± 반 몸) 몸 전체가 트랙 안에 있게 하고 이만큼 더 띄운다.
    /// </summary>
    private const double WallMargin = 0.3;

    /// <summary>파랑(입구)·주황(출구). 테두리 색과, 안쪽·발광에 쓰는 같은 계열 색.</summary>
    private static readonly Color[] RimColors = { Color.FromRgb(0x3F, 0xA9, 0xFF), Color.FromRgb(0xFF, 0x8C, 0x1A) };
    private static readonly Color[] CoreColors = { Color.FromRgb(0xB8, 0xE2, 0xFF), Color.FromRgb(0xFF, 0xD8, 0xA8) };

    /// <summary>열릴 때 튀는 불꽃 개수, 닫힐 때 개수.</summary>
    private const int BurstSparks = 12;
    private const int CloseSparks = 6;
    /// <summary>열려 있는 동안 테두리를 따라 도는 빛 점 개수와 빠르기(바퀴/초).</summary>
    private const int OrbitDots = 3;
    private const double OrbitSpeed = 0.55;
    /// <summary>안쪽으로 빨려 들어가는 물결 개수와 빠르기(번/초).</summary>
    private const int Ripples = 3;
    private const double RippleSpeed = 0.9;
    /// <summary>통과할 때 포탈이 환해졌다 가라앉는 시간(초). 몸이 걸쳐 있는 동안은 PassingFlare만큼 늘 환하다.</summary>
    private const double FlareSeconds = 0.5;
    private const double PassingFlare = 0.6;
    /// <summary>코가 입구에 닿을 때 빨려 드는 불꽃, 출구로 튀어나올 때 앞으로 뿜는 불꽃 개수와 시간(초).</summary>
    private const int SuckSparks = 9;
    private const int PopSparks = 11;
    private const double PassSeconds = 0.55;
    /// <summary>출구에서 튀어나올 때 앞으로 뻗는 빛줄기 개수.</summary>
    private const int PopStreaks = 3;
    /// <summary>날아가는 구슬 뒤에 끌리는 꼬리 점 개수와 간격(비행 진행률).</summary>
    private const int ShotTrail = 5;
    private const double ShotTrailStep = 0.07;

    /// <summary>오버레이 띠에서 몸 위로 남겨 둘 여유(화면 단위). 포탈이 키보다 크고 발광이 위로 번진다.</summary>
    public static int Headroom(int height) => (int)Math.Ceiling(height * (PortalHeight * 1.15 - 1)) + 2;

    /// <summary>포탈 하나가 차지하는 사각형. 바닥(floorY)에 서 있고 가운데 선이 centerX다.</summary>
    public static Rect PortalBox(double centerX, double floorY, double bodyHeight)
    {
        double h = bodyHeight * PortalHeight, w = bodyHeight * PortalHalfWidth * 2;
        return new Rect(centerX - w / 2, floorY - h, w, h);
    }

    /// <summary>
    /// 한 프레임 진행한다. 러너가 움직인 직후에 불러서, 몸 가운데가 입구 선을 넘었으면 출구로 옮긴다.
    /// </summary>
    /// <param name="prevCenter">이번에 움직이기 전 몸 가운데(트랙 좌표).</param>
    public static void Advance(PortalState s, RunnerState state, double prevCenter, double dt, TrackBounds track, double bodyWidth,
        PortalTuning tune)
    {
        double w = bodyWidth;
        double c = state.X + w / 2;
        switch (s.Phase)
        {
            case PortalPhase.Idle:
                s.Wait -= dt;
                if (s.Wait > 0) break;
                if (TryAim(s, state, track, w, tune))
                {
                    s.Phase = PortalPhase.Active;
                    s.Clock = 0;
                    s.Entered = false;
                    s.TouchAt = s.PopAt = double.NaN;
                    s.Dir = state.Direction;
                    s.Seed = Random.Shared.NextDouble() * 2 * Math.PI;
                    s.Fired[0] = s.Fired[1] = false;
                    // 코가 입구에 닿기까지 걸릴 시간의 70% 안에 주황 구슬까지 떨어지게 발사 간격·비행 시간을 줄인다.
                    double arrive = (Math.Abs(s.At[0] - c) - w * 0.5) / Math.Max(1e-6, state.Speed);
                    s.ShotPace = Math.Clamp(arrive * 0.7 / (tune.SecondShotDelay + tune.MaxFlight), 0.2, 1);
                    s.FireAt[0] = 0;
                    s.FireAt[1] = tune.SecondShotDelay * s.ShotPace;
                }
                else s.Wait = tune.RetrySeconds; // 앞에 벽이 가까우면 돌아선 뒤 다시 본다
                break;

            case PortalPhase.Active:
                s.Clock += dt;
                // 쏠 때가 된 구슬을 지금 입 앞에서 떠나보낸다. 고양이는 계속 달리니 두 구슬의 출발점이 조금 다르다.
                for (int i = 0; i < 2; i++)
                {
                    if (s.Fired[i] || s.Clock < s.FireAt[i]) continue;
                    s.Fired[i] = true;
                    s.FromX[i] = c + s.Dir * w * MouthAhead;
                    double far = Math.Clamp(Math.Abs(s.At[i] - s.FromX[i]) / (w * FarBodies), 0, 1);
                    s.LandAt[i] = s.FireAt[i] + (tune.MinFlight + (tune.MaxFlight - tune.MinFlight) * Math.Sqrt(far)) * s.ShotPace;
                }

                if (!s.Entered)
                {
                    // 작업표시줄이 줄어 포탈이 트랙 밖으로 밀렸거나, 입구에 닿기 전에 돌아섰으면(설정 변경 등) 닫는다.
                    if (!InTrack(s.At[0], track, w) || !InTrack(s.At[1], track, w) || state.Direction != s.Dir)
                    {
                        StartClosing(s);
                        break;
                    }
                    double p = s.At[0];
                    if (double.IsNaN(s.TouchAt) && (c - p) * s.Dir > -w / 2) s.TouchAt = s.Clock;
                    // 몸 가운데가 입구 선을 넘었다(딱 닿은 것도). 같은 자리에 서 있던 건 넘은 게 아니다.
                    if (prevCenter != c && (prevCenter - p) * (c - p) <= 0)
                    {
                        s.Entered = true;
                        s.PopAt = s.Clock;
                        // 쏜 직후 CPU가 확 올라 빨라지면 주황 구슬이 떨어지기 전에 입구에 닿을 수 있다.
                        // 빈 자리에서 튀어나오지 않게 아직 날아가는 구슬은 이 순간 출구 자리에 떨어뜨린다.
                        if (!s.Fired[1])
                        {
                            s.Fired[1] = true;
                            s.FireAt[1] = s.Clock;
                            s.FromX[1] = s.At[1]; // 날아가는 모습은 건너뛴다
                        }
                        if (s.LandAt[1] > s.Clock) s.LandAt[1] = s.Clock;
                        // 넘어간 만큼은 출구 너머에 그대로 둔다. 그래야 속도가 끊기지 않는다.
                        state.X += s.At[1] - p;
                    }
                }
                else if ((c - s.At[1]) * s.Dir >= w / 2 || state.Direction != s.Dir)
                {
                    // 몸이 다 나왔으면 닫는다. 그 사이 방향이 바뀌었으면 지나가기를 그만둔다.
                    StartClosing(s);
                }
                break;

            case PortalPhase.Closing:
                s.Clock += dt;
                s.CloseAge += dt;
                if (s.CloseAge >= CloseSeconds)
                {
                    s.Reset(tune.NextWait(state.Intensity)); // 바쁠수록 다음 발사가 빨리 온다
                }
                break;
        }
    }

    private static void StartClosing(PortalState s)
    {
        s.Phase = PortalPhase.Closing;
        s.CloseAge = 0;
    }

    /// <summary>
    /// 포탈 선이 놓일 수 있는 범위(트랙 좌표). 트랙은 몸 왼쪽 끝 기준이라 몸 가운데는 [MinX+w/2, MaxX+w/2]를 오가고,
    /// 지나는 동안 가운데가 선 ± w/2를 지나므로 선은 [MinX+w, MaxX] 안에 있어야 몸이 벽에 안 닿는다.
    /// </summary>
    private static (double Lo, double Hi) Allowed(TrackBounds track, double w) =>
        (track.MinX + w * (1 + WallMargin), track.MaxX - w * WallMargin);

    private static bool InTrack(double p, TrackBounds track, double w)
    {
        var (lo, hi) = Allowed(track, w);
        return p >= lo && p <= hi;
    }

    /// <summary>
    /// 두 포탈 자리를 정한다. 입구는 지금 속도로 EntryLeadSeconds 뒤 닿을 앞, 출구는 앞뒤 아무 데나. 다만 출구는 고양이와
    /// 입구 사이(고양이가 입구까지 달려갈 길)를 피한다. 거기 열리면 주황으로 먼저 지나가 버린다.
    /// 좁은 트랙(설정 창 미리보기)에선 입구가 멀면 출구 둘 자리가 안 남는다. 그때는 입구를 하한까지 조금씩 당겨 가며 다시 본다.
    /// </summary>
    private static bool TryAim(PortalState s, RunnerState state, TrackBounds track, double w, PortalTuning tune)
    {
        double c = state.X + w / 2;
        int dir = state.Direction;
        var (lo, hi) = Allowed(track, w);
        // 출구 둘 범위. 끝없는 트랙이면 고양이 가까이로 좁힌다.
        double exitLo = lo, exitHi = hi;
        if (tune.Reach is { } r)
        {
            exitLo = Math.Max(exitLo, c - r * w);
            exitHi = Math.Min(exitHi, c + r * w);
        }

        // 코(몸 가운데에서 반 몸 앞)가 입구에 닿기까지 달릴 거리. 앞 벽까지 남은 자리보다 멀 수는 없다.
        double room = (dir > 0 ? hi - c : c - lo) - w * 0.5;
        // 가까이 당기면 금방 닿지만, 그때는 Advance가 구슬을 그만큼 빨리 쏘고 빨리 떨어뜨린다(ShotPace).
        double floor = tune.MinEntryLead * w;
        double want = Math.Min(Math.Clamp(state.Speed * tune.EntryLeadSeconds, tune.MinEntryLead * w, tune.MaxEntryLead * w), room);
        if (want < floor) return false;

        const int Steps = 4;
        for (int k = 0; k <= Steps; k++)
        {
            double lead = want - (want - floor) * k / Steps;
            double entry = c + dir * (w * 0.5 + lead);
            // 고양이 꼬리 뒤부터 입구 너머 MinGap까지는 출구를 두지 않는다. 남은 양쪽 구간 중 길이에 비례해 고른다.
            double a = c - dir * w, b = entry + dir * tune.MinGap * w;
            double pathFrom = Math.Min(a, b), pathTo = Math.Max(a, b);
            double left = Math.Max(0, Math.Min(exitHi, pathFrom) - exitLo);
            double right = Math.Max(0, exitHi - Math.Max(exitLo, pathTo));
            if (left + right <= 0) continue;
            double pick = Random.Shared.NextDouble() * (left + right);
            s.At[0] = entry;
            s.At[1] = pick < left ? exitLo + pick : Math.Max(exitLo, pathTo) + (pick - left);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 지금 몸이 포탈에 걸쳐 있으면, 입구와 출구의 포탈 선(트랙 좌표)과 달리는 방향.
    /// 옮겨지기 전(코가 입구에 들어감)과 뒤(꼬리가 아직 입구에 남음) 모두 해당한다. 걸쳐 있지 않으면 null.
    /// </summary>
    public static (double Entry, double Exit, int Dir)? Transit(PortalState s, RunnerState state, double bodyWidth)
    {
        if (s.Phase != PortalPhase.Active || state.Direction != s.Dir) return null;
        double w = bodyWidth, c = state.X + w / 2;
        // 몸 가운데가 포탈 선을 얼마나 넘었나. 옮겨지기 전엔 입구 기준(아직 음수), 뒤엔 출구 기준(0부터 양수).
        double past = (c - (s.Entered ? s.At[1] : s.At[0])) * s.Dir;
        bool straddling = s.Entered ? past < w / 2 : past > -w / 2;
        return straddling ? (s.At[0], s.At[1], s.Dir) : null;
    }

    /// <summary>포탈 i가 얼마나 열렸는지(0~1, 열릴 때 살짝 1을 넘는다). 구슬이 아직 날아가는 중이면 0.</summary>
    private static double Openness(PortalState s, int i)
    {
        if (s.Phase == PortalPhase.Idle || !s.Fired[i] || s.Clock < s.LandAt[i]) return 0;
        double open = EaseOutBack(Math.Clamp((s.Clock - s.LandAt[i]) / OpenSeconds, 0, 1));
        return s.Phase == PortalPhase.Closing ? open * (1 - EaseInCubic(Math.Clamp(s.CloseAge / CloseSeconds, 0, 1))) : open;
    }

    /// <summary>통과 사건(입구는 코가 닿을 때, 출구는 튀어나올 때) 뒤 지난 시간(초). 아직이면 NaN.</summary>
    private static double SincePass(PortalState s, int i)
    {
        double at = i == 0 ? s.TouchAt : s.PopAt;
        return double.IsNaN(at) ? double.NaN : s.Clock - at;
    }

    /// <summary>통과하면서 포탈이 환해진 정도(0~1). 닿은 순간 1로 번쩍이고, 몸이 걸쳐 있는 동안은 PassingFlare 아래로 안 내려간다.</summary>
    private static double Flare(PortalState s, int i, bool passing)
    {
        double since = SincePass(s, i);
        double flash = double.IsNaN(since) ? 0 : 1 - Math.Clamp(since / FlareSeconds, 0, 1);
        return Math.Max(passing ? PassingFlare : 0, flash);
    }

    /// <summary>
    /// 테두리의 뒤쪽 반이 포탈 선의 어느 쪽(+1 오른쪽, -1 왼쪽)인지. 고양이 몸이 보이는 쪽을 뒤로 둔다.
    /// 입구는 고양이가 달려오는 쪽, 출구는 튀어나가는 쪽이다. 그래야 몸이 뒤쪽 테두리를 가리고, 앞쪽 테두리는
    /// 몸이 사라진 쪽에 있어서 고리 안으로 들어가는 것처럼 보인다. 테두리가 몸을 다 덮으면 몸이 포탈 뒤에 있는 것 같다.
    /// </summary>
    private static int BackSide(PortalState s, int index) => index == 0 ? -s.Dir : s.Dir;

    /// <summary>
    /// 테두리(바깥 테 + 안쪽 흰 테 + 통과 중 달아오름)와 그 위를 도는 빛 점 중 한쪽 반만 그린다.
    /// </summary>
    /// <param name="back">true면 뒤쪽 반(몸보다 먼저), false면 앞쪽 반(몸보다 나중).</param>
    private static void DrawRimHalf(DrawingContext ctx, Rect box, PortalState s, int index, bool passing, double seconds,
        double scaling, double k, bool back)
    {
        var center = box.Center;
        double h = box.Height;
        double rx = box.Width / 2 * Math.Min(k, 1.15), ry = box.Height / 2 * k;
        int side = back ? BackSide(s, index) : -BackSide(s, index);
        // 포탈 선에서 그 쪽으로 넉넉한 사각형. 테 굵기·빛 점이 삐져나와도 잘리지 않게 위아래·옆으로 크게 잡는다.
        double far = box.Width * 4 + h;
        var half = side > 0
            ? new Rect(center.X, box.Y - far, far, box.Height + far * 2)
            : new Rect(center.X - far, box.Y - far, far, box.Height + far * 2);
        var res = Resources(index);

        using (ctx.PushClip(half))
        {
            ctx.DrawEllipse(null, RimPen(index, h, scaling, inner: false), center, rx, ry);
            ctx.DrawEllipse(null, RimPen(index, h, scaling, inner: true), center, rx * 0.86, ry * 0.95);
            // 통과하는 동안 테두리가 하얗게 달아오른다.
            double flare = Flare(s, index, passing);
            if (flare > 0.01)
                using (ctx.PushOpacity(flare))
                    ctx.DrawEllipse(null, RimPen(index, h, scaling, inner: true), center, rx, ry);
        }

        // 빛 점은 지금 있는 쪽 반에서만 그린다. 테두리를 따라 돌며 몸 뒤로 갔다가 앞으로 나온다.
        double dot = h * 0.035;
        for (int j = 0; j < OrbitDots; j++)
        {
            double a = (seconds * OrbitSpeed + j / (double)OrbitDots) * 2 * Math.PI * (index == 0 ? 1 : -1) + s.Seed;
            double cos = Math.Cos(a);
            if ((cos >= 0 ? 1 : -1) != side) continue;
            ctx.DrawEllipse(res.Spark, null, new Point(center.X + rx * cos, center.Y + ry * Math.Sin(a)), dot, dot);
        }
    }

    /// <summary>
    /// 고양이 뒤에 깔리는 부분: 바깥 발광, 안쪽 빛, 안으로 빨려 드는 물결, 테두리의 뒤쪽 반. 통과하는 동안은 더 환하고 넓게 번진다.
    /// </summary>
    /// <param name="box">다 열렸을 때 포탈이 차지하는 사각형(PortalBox).</param>
    /// <param name="passing">고양이 몸이 지금 포탈에 걸쳐 있는지.</param>
    public static void DrawBack(DrawingContext ctx, Rect box, PortalState s, int index, bool passing, double seconds, double scaling)
    {
        double k = Openness(s, index);
        if (k <= 0.01) return;
        var center = box.Center;
        double rx = box.Width / 2 * Math.Min(k, 1.15), ry = box.Height / 2 * k;
        double pulse = 0.85 + 0.15 * Math.Sin(seconds * 5 + s.Seed + index);
        var res = Resources(index);

        // 발광: 바깥으로 갈수록 옅은 타원을 겹친다. 가우시안 블러 대신 가볍게.
        using (ctx.PushOpacity(Math.Min(1, k) * pulse))
        {
            for (int j = 3; j >= 1; j--)
                ctx.DrawEllipse(res.Glow, null, center, rx * (1 + 0.55 * j), ry * (1 + 0.05 * j));
            // 안쪽: 테두리 색을 옅게 채우고 가운데를 밝게.
            ctx.DrawEllipse(res.Fill, null, center, rx, ry);
            ctx.DrawEllipse(res.Core, null, center, rx * 0.55, ry * 0.8);
        }

        // 통과: 바깥으로 발광이 한 겹 더 넓게 번지고, 안쪽이 환해진다.
        double flare = Flare(s, index, passing) * Math.Min(1, k);
        if (flare > 0.01)
        {
            using (ctx.PushOpacity(flare))
            {
                ctx.DrawEllipse(res.Glow, null, center, rx * 3.4, ry * 1.25);
                ctx.DrawEllipse(res.Glow, null, center, rx * 2.4, ry * 1.12);
                ctx.DrawEllipse(res.Core, null, center, rx * 0.9, ry * 0.95);
            }
        }

        // 물결: 테두리에서 가운데로 줄어들며 진해졌다 사라진다. 빨려 드는 느낌.
        var pen = RipplePen(index, box.Height, scaling);
        for (int j = 0; j < Ripples; j++)
        {
            double t = Frac(seconds * RippleSpeed + j / (double)Ripples + s.Seed);
            double scale = 1 - t * 0.85;
            using (ctx.PushOpacity(Math.Min(1, k) * Math.Sin(t * Math.PI) * 0.7))
                ctx.DrawEllipse(null, pen, center, rx * scale, ry * scale);
        }

        DrawRimHalf(ctx, box, s, index, passing, seconds, scaling, k, back: true);
    }

    /// <summary>
    /// 고양이 앞에 덮이는 부분: 날아가는 구슬, 테두리의 앞쪽 반, 열릴 때 터지는 불꽃과 빛 고리,
    /// 닫힐 때 흩어지는 불꽃, 통과 이펙트.
    /// </summary>
    /// <param name="shotDx">구슬이 떠난 자리가 포탈 가운데 선에서 얼마나 떨어졌는지(포탈 키 대비).
    /// 화면 좌표가 아니라 비율이라 오버레이가 Box를 논리 단위로 바꿔도 그대로 맞는다.</param>
    /// <param name="passing">고양이 몸이 지금 포탈에 걸쳐 있는지.</param>
    public static void DrawFront(DrawingContext ctx, Rect box, PortalState s, int index, double shotDx, bool passing, double seconds,
        double scaling)
    {
        double k = Openness(s, index);
        var center = box.Center;
        double h = box.Height;
        var res = Resources(index);

        DrawShot(ctx, box, s, index, shotDx, res);

        if (k > 0.01) DrawRimHalf(ctx, box, s, index, passing, seconds, scaling, k, back: false);

        DrawPass(ctx, box, s, index, scaling, res);

        if (!s.Fired[index] || s.Clock < s.LandAt[index]) return;
        double age = s.Clock - s.LandAt[index];
        if (s.Phase == PortalPhase.Closing)
        {
            DrawSparks(ctx, center, h, res, s.Seed + 1 + index, CloseSparks, s.CloseAge / CloseSeconds, reach: 0.4);
        }
        else if (age < BurstSeconds)
        {
            double t = age / BurstSeconds;
            // 빛 고리: 옆으로 넓게 퍼지며 옅어진다. 위로는 조금만 커진다. 오버레이 띠(Headroom)가 낮아서 위로 퍼지면 잘린다.
            double e = EaseOutCubic(t);
            using (ctx.PushOpacity((1 - t) * 0.9))
                ctx.DrawEllipse(null, RimPen(index, h, scaling, inner: false), center, box.Width / 2 * (1 + 0.9 * e) * 1.4, box.Height / 2 * (0.9 + 0.2 * e));
            DrawSparks(ctx, center, h, res, s.Seed + index * 2.3, BurstSparks, t, reach: 0.75);
        }
    }

    /// <summary>
    /// 통과 이펙트. 입구는 코가 닿는 순간 빛 고리가 퍼지고 불꽃이 가운데로 빨려 든다.
    /// 출구는 튀어나오는 순간 빛 고리가 퍼지고, 달리는 방향으로 불꽃과 빛줄기를 뿜는다.
    /// </summary>
    private static void DrawPass(DrawingContext ctx, Rect box, PortalState s, int index, double scaling, PortalResources res)
    {
        double since = SincePass(s, index);
        if (double.IsNaN(since) || since >= PassSeconds) return;
        double t = since / PassSeconds, h = box.Height;
        var center = box.Center;

        // 열릴 때 빛 고리처럼 옆으로만 넓게 퍼진다(위로 퍼지면 오버레이 띠 밖으로 잘린다).
        double e = EaseOutCubic(t);
        using (ctx.PushOpacity((1 - t) * 0.8))
            ctx.DrawEllipse(null, RimPen(index, h, scaling, inner: true), center, box.Width / 2 * (1 + 0.6 * e) * 1.3, box.Height / 2 * (0.85 + 0.15 * e));

        if (index == 0)
        {
            // 빨려 듦: 바깥에서 가운데로 모이며 작아진다.
            double pull = EaseOutCubic(t);
            using var _ = ctx.PushOpacity(Math.Sin(Math.PI * Math.Min(1, t * 1.2)));
            for (int i = 0; i < SuckSparks; i++)
            {
                double rnd = Hash(i, s.Seed + 5);
                double a = i * 2 * Math.PI / SuckSparks + s.Seed + rnd;
                double dist = h * (0.45 + 0.35 * rnd) * (1 - pull);
                var p = new Point(center.X + Math.Cos(a) * dist, center.Y + Math.Sin(a) * dist * 0.75);
                double size = h * 0.04 * (1 - pull * 0.6);
                ctx.DrawEllipse(i % 3 == 0 ? res.SparkWhite : res.Spark, null, p, size, size);
            }
            return;
        }

        // 뿜음: 달리는 방향으로 부채꼴로 튄다. 빛줄기는 앞으로 쭉 뻗었다가 꼬리부터 사라진다.
        double travel = EaseOutCubic(t), forward = s.Dir >= 0 ? 0 : Math.PI;
        using (ctx.PushOpacity(1 - t * t))
        {
            for (int i = 0; i < PopSparks; i++)
            {
                double rnd = Hash(i, s.Seed + 9);
                double a = forward + (i / (double)(PopSparks - 1) - 0.5) * 1.7 + (rnd - 0.5) * 0.3;
                double dist = h * (0.5 + 0.6 * rnd) * travel;
                var p = new Point(center.X + Math.Cos(a) * dist, center.Y + Math.Sin(a) * dist * 0.6);
                double size = h * 0.045 * (1 - t * 0.6);
                ctx.DrawEllipse(i % 3 == 0 ? res.SparkWhite : res.Spark, null, p, size, size);
            }
            var streak = StreakPen(index, h, scaling);
            for (int j = 0; j < PopStreaks; j++)
            {
                double rnd = Hash(j, s.Seed + 13);
                double y = center.Y + (j - (PopStreaks - 1) / 2.0) * h * 0.18;
                double reach = h * (0.9 + 0.5 * rnd);
                double head = reach * travel, tail = reach * Math.Max(0, t * 1.6 - 0.4);
                if (head - tail < 1) continue;
                ctx.DrawLine(streak, new Point(center.X + s.Dir * tail, y), new Point(center.X + s.Dir * head, y));
            }
        }
    }

    /// <summary>
    /// 입 앞에서 번쩍이고, 빛 구슬이 꼬리를 끌며 살짝 포물선을 그려 포탈 자리로 날아간다.
    /// 출발은 입 높이, 도착은 포탈 가운데 높이다.
    /// </summary>
    private static void DrawShot(DrawingContext ctx, Rect box, PortalState s, int index, double shotDx, PortalResources res)
    {
        if (s.Phase != PortalPhase.Active || !s.Fired[index]) return;
        double since = s.Clock - s.FireAt[index], flight = s.LandAt[index] - s.FireAt[index];
        double h = box.Height, body = h / PortalHeight;
        var from = new Point(box.Center.X + shotDx * h, box.Bottom - body * MouthHeight);
        var to = box.Center;

        if (since < MuzzleSeconds)
        {
            double m = since / MuzzleSeconds;
            using (ctx.PushOpacity(1 - m))
            {
                double r = body * (0.10 + 0.18 * EaseOutCubic(m));
                ctx.DrawEllipse(res.Glow, null, from, r * 1.8, r * 1.8);
                ctx.DrawEllipse(res.SparkWhite, null, from, r * 0.6, r * 0.6);
            }
        }
        if (since >= flight) return;

        double arc = Math.Min(body * 0.25, Math.Abs(to.X - from.X) * 0.08);
        Point At(double t) => new(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t - arc * Math.Sin(Math.PI * t));

        double now = since / flight, size = body * 0.075;
        for (int j = ShotTrail; j >= 1; j--)
        {
            double t = now - j * ShotTrailStep;
            if (t < 0) continue;
            double q = 1 - j / (double)(ShotTrail + 1);
            using (ctx.PushOpacity(0.7 * q))
                ctx.DrawEllipse(res.Spark, null, At(t), size * (0.4 + 0.6 * q), size * (0.4 + 0.6 * q));
        }
        var head = At(now);
        ctx.DrawEllipse(res.Glow, null, head, size * 2.6, size * 2.6);
        ctx.DrawEllipse(res.Fill, null, head, size * 1.5, size * 1.5);
        ctx.DrawEllipse(res.SparkWhite, null, head, size * 0.8, size * 0.8);
    }

    /// <summary>가운데에서 사방으로 튀었다가 작아지며 사라지는 불꽃. 각도·거리는 seed로 정해져서 따로 기억하지 않는다.</summary>
    private static void DrawSparks(DrawingContext ctx, Point center, double h, PortalResources res, double seed, int count, double t,
        double reach)
    {
        t = Math.Clamp(t, 0, 1);
        double travel = EaseOutCubic(t);
        using var _ = ctx.PushOpacity(1 - t * t);
        for (int i = 0; i < count; i++)
        {
            double rnd = Hash(i, seed);
            double a = i * 2 * Math.PI / count + seed + rnd * 0.5;
            double dist = h * reach * (0.6 + 0.4 * rnd) * travel;
            // 위아래로는 덜 튄다. 띠가 낮아서 위로 멀리 가면 잘린다.
            var p = new Point(center.X + Math.Cos(a) * dist, center.Y + Math.Sin(a) * dist * 0.6);
            double size = h * 0.045 * (1 - t * 0.7);
            ctx.DrawEllipse(i % 3 == 0 ? res.SparkWhite : res.Spark, null, p, size, size);
        }
    }

    // ───────── 붓·펜 캐시 ─────────
    // 매 프레임 붓·펜을 새로 만들면 화면 합성 쪽 객체까지 함께 생겨 메모리가 크게 출렁인다(플래시 불꽃 줄기와 같은 문제).
    // 진하기는 PushOpacity로 바꾸고, 붓은 색마다 하나씩 처음에 만들어 둔다.

    private sealed record PortalResources(IImmutableBrush Glow, IImmutableBrush Fill, IImmutableBrush Core, IImmutableBrush Spark,
        IImmutableBrush SparkWhite);

    private static readonly PortalResources[] BrushSets = Enumerable.Range(0, 2).Select(i => new PortalResources(
        new ImmutableSolidColorBrush(RimColors[i], 0.16),
        new ImmutableSolidColorBrush(RimColors[i], 0.45),
        new ImmutableSolidColorBrush(CoreColors[i], 0.55),
        new ImmutableSolidColorBrush(CoreColors[i], 0.95),
        new ImmutableSolidColorBrush(Colors.White, 0.95))).ToArray();

    private static PortalResources Resources(int index) => BrushSets[index & 1];

    private readonly record struct PenKey(int Index, int Kind, double Thickness);

    // 오버레이 그리기는 UI 스레드에서만 하므로 잠금 없이 쓴다
    private static readonly Dictionary<PenKey, ImmutablePen> PenCache = new();

    private static ImmutablePen RimPen(int index, double height, double scaling, bool inner)
    {
        double scale = scaling > 0 ? scaling : 1;
        // 장치 픽셀로 반올림해서 크기가 조금씩 다른 칸마다 펜이 끝없이 늘지 않게 한다.
        double px = Math.Max(inner ? 1 : 1.5, Math.Round(height * scale * (inner ? 0.03 : 0.065)));
        return Pen(new PenKey(index & 1, inner ? 1 : 0, px / scale),
            inner ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF) : RimColors[index & 1]);
    }

    private static ImmutablePen StreakPen(int index, double height, double scaling)
    {
        double scale = scaling > 0 ? scaling : 1;
        double px = Math.Max(1, Math.Round(height * scale * 0.03));
        return Pen(new PenKey(index & 1, 3, px / scale), CoreColors[index & 1], PenLineCap.Round);
    }

    private static ImmutablePen RipplePen(int index, double height, double scaling)
    {
        double scale = scaling > 0 ? scaling : 1;
        double px = Math.Max(1, Math.Round(height * scale * 0.025));
        return Pen(new PenKey(index & 1, 2, px / scale), CoreColors[index & 1]);
    }

    private static ImmutablePen Pen(PenKey key, Color color, PenLineCap cap = PenLineCap.Flat)
    {
        if (PenCache.TryGetValue(key, out var pen)) return pen;
        if (PenCache.Count >= 256) PenCache.Clear(); // 펜은 불변이라 Dispose할 것이 없다
        return PenCache[key] = new ImmutablePen(new ImmutableSolidColorBrush(color), key.Thickness, lineCap: cap);
    }

    // ───────── 수식 ─────────

    private static double EaseOutBack(double t)
    {
        const double c1 = 1.70158, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(t - 1, 3) + c1 * Math.Pow(t - 1, 2);
    }

    private static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    private static double EaseInCubic(double t) => t * t * t;
    private static double Frac(double v) => v - Math.Floor(v);
    /// <summary>i·seed로 정해지는 0~1 값. 불꽃마다 각도·거리를 흔들되 프레임마다 같게.</summary>
    private static double Hash(int i, double seed) => Frac(Math.Sin(i * 12.9898 + seed * 78.233) * 43758.5453);

    internal static double Rand(double min, double max) => min + Random.Shared.NextDouble() * (max - min);
}
