"""토끼 깡충 뛰기 5프레임을 RunCat 고양이 그림체(검은 둥근 실루엣 + 투명 눈 구멍)로 그린다.

결과물: src/Woodada/Assets/Rabbit/rabbit_running_{0..4}.png
    python tools/draw_rabbit.py src/Woodada/Assets/Rabbit   (Pillow 필요)

귀는 등 쪽으로 눕히고, 뒷다리는 둥근 엉덩이에서 큰 발이 바로 나오는 만화형(paddle)을 쓴다.
비교용으로 다른 뒷다리 모양도 남겨 뒀다: 두 번째 인자로 z(허벅지·정강이·발 관절형) 또는
pair(관절형 다리 두 개를 엇갈리게)를 주면 그 모양으로 그린다.

앱은 PNG의 알파만 쓰고 색은 설정값으로 칠한다. 눈은 머리 쪽(오른쪽)의 작은 투명 구멍이어야
FrameSprite가 찾아서 흰색으로 채운다. 두 눈이 붙거나 머리 테두리에 닿으면 눈으로 인식되지 않는다.

실제 바운드 순서: 웅크리기(뒷발이 앞발 앞에) → 박차기(다리가 한 줄로 쭉) → 공중(뒤로 길게)
→ 앞발 착지(뒷다리는 접혀 몸 밑으로 넘어오는 중) → 뒷발 착지(앞발보다 앞, 등이 C자)
"""
import math, sys
from PIL import Image, ImageDraw, ImageFilter, ImageChops

W, H = 150, 110
SS = 8
GROUND = H - 1.0

# 프레임별 자세 (깡충 뛰기 한 주기)
#   lift: 바닥에서 뜬 높이, tilt: +면 머리 쪽이 들림, L: 엉덩이~가슴 거리, arch: -면 등이 둥글게
#   hind=(허벅지, 정강이, 발), front=앞다리 — 모두 화면 각도 (0=앞, 90=아래, 180=뒤)
#   ear=(방향 보정, 휨)
FRAMES = [
    # 0: 웅크리기 — 뒷발이 앞발보다 앞에 납작하게 놓이고, 등이 C자로 둥글다. 앞발은 들어 앞으로.
    dict(lift=0,  tilt=6,   L=18, arch=-8, hind=(40, 150, 2),    front=55,  ear=(6, 10)),
    # 1: 박차기 — 허벅지·정강이·발이 한 줄로 펴지며 발끝으로 민다. 몸은 앞이 들린다.
    dict(lift=0,  tilt=20,  L=28, arch=-1, hind=(118, 128, 132), front=30,  ear=(-4, -14)),
    # 2: 공중 — 몸이 가장 길고, 뒷다리는 뒤로 거의 수평, 앞다리는 착지하러 앞아래로.
    dict(lift=13, tilt=3,   L=32, arch=3,  hind=(150, 168, 176), front=72,  ear=(-10, -20)),
    # 3: 앞발 착지 — 앞이 내려가 앞발로 버티고, 뒷다리는 접혀서 배 밑으로 넘어오는 중.
    dict(lift=0,  tilt=-16, L=26, arch=-3, hind=(52, 118, 32),   front=98,  ear=(4, 18)),
    # 4: 뒷발 착지 — 뒷발이 앞발 앞쪽에 내려앉고 앞발은 뒤로 떨어진다.
    dict(lift=0,  tilt=-6,  L=20, arch=-7, hind=(30, 140, 8),    front=128, ear=(8, 14)),
]

# 귀 기본 방향(화면 각도, -90 = 위)
EAR_BASE = -158  # 귀를 등 쪽으로 눕힌다


class Canvas:
    def __init__(self):
        self.img = Image.new("L", (W * SS, H * SS), 0)
        self.d = ImageDraw.Draw(self.img)

    def circle(self, p, r, fill=255):
        x, y = p
        self.d.ellipse([(x - r) * SS, (y - r) * SS, (x + r) * SS, (y + r) * SS], fill=fill)

    def capsule(self, p, q, r1, r2=None):
        r2 = r1 if r2 is None else r2
        n = max(2, int(math.dist(p, q) / 0.4))
        for i in range(n + 1):
            t = i / n
            self.circle((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t), r1 + (r2 - r1) * t)

    def curve(self, pts, rf):
        """2차 베지어를 따라 rf(t) 반지름의 원을 찍는다."""
        (x0, y0), (x1, y1), (x2, y2) = pts
        n = 90
        for i in range(n + 1):
            t = i / n
            x = (1 - t) ** 2 * x0 + 2 * (1 - t) * t * x1 + t * t * x2
            y = (1 - t) ** 2 * y0 + 2 * (1 - t) * t * y1 + t * t * y2
            self.circle((x, y), rf(t))


def unit(deg):
    r = math.radians(deg)
    return math.cos(r), math.sin(r)


def build(p, dy, style):
    cv = Canvas()
    tilt = math.radians(-p["tilt"])
    ct, st = math.cos(tilt), math.sin(tilt)
    L = p["L"]
    rump = (62.0 - L / 2, 62.0 + dy)  # 처음 잴 때 발이 캔버스 밖으로 잘리지 않게 높게 시작

    def B(x, y, origin=rump):
        return origin[0] + x * ct - y * st, origin[1] + x * st + y * ct

    def ang(a):  # 몸 기울기를 반영한 화면 각도
        return a - p["tilt"]

    # 몸통: 엉덩이가 크고 가슴으로 갈수록 좁아지는 달걀
    n = 50
    for i in range(n + 1):
        t = i / n
        r = 14.0 - 4.5 * t
        cv.circle(B(L * t, p["arch"] * 4 * t * (1 - t) - 1.5 * t), r)
    cv.circle(B(3, 2), 12.0)          # 통통한 엉덩이

    # 꼬리: 작고 둥근 솜뭉치
    cv.circle(B(-13, -6), 5.0)

    # 뒷다리
    def hind_leg(joint, th, sh, ft, k=1.0):
        def step(p0, a, l):
            u = unit(a)
            return p0[0] + u[0] * l * k, p0[1] + u[1] * l * k
        if style == "paddle":
            # 둥근 엉덩이 가장자리(뒤꿈치)에서 큰 발이 바로 나온다. 정강이는 엉덩이 안에 숨는다.
            hock = step(joint, th, 12)
            toe = step(hock, ft, 19)
            cv.capsule(hock, toe, 4.3 * k, 4.0 * k)
            cv.circle(toe, 4.6 * k)                       # 큼직한 발끝
        else:
            # 허벅지(굵게, 엉덩이에 묻힘) → 정강이(가늘게) → 뒤꿈치 → 길고 납작한 발
            knee = step(joint, th, 12)
            hock = step(knee, sh, 12)
            toe = step(hock, ft, 16)
            cv.capsule(joint, knee, 6.6 * k, 4.6 * k)
            cv.capsule(knee, hock, 3.9 * k, 2.9 * k)
            cv.circle(hock, 3.3 * k)                      # 뒤꿈치
            cv.capsule(hock, toe, 3.0 * k, 2.7 * k)
            cv.circle((toe[0] + 0.6, toe[1]), 3.0 * k)    # 발끝

    th, sh_, ft = p["hind"]
    if style == "pair":
        # 먼 다리: 관절이 조금 앞·위에 있고, 동작이 살짝 늦게 따라온다(각도 차이).
        hind_leg(B(7, 2), th - 26, sh_ - 18, ft - 12, k=0.92)
    hind_leg(B(2, 5), th, sh_, ft)

    # 앞다리: 짧고 가늘다
    sh = B(L - 2, 5)
    w = unit(p["front"])
    paw = (sh[0] + w[0] * 15, sh[1] + w[1] * 15)
    cv.capsule(sh, paw, 3.4, 2.9)
    cv.circle((paw[0] + 0.8, paw[1]), 3.2)

    # 머리: 동그란 머리 + 볼록한 주둥이
    head = B(L + 7, -12)
    cv.circle(head, 11.5)
    cv.circle(B(L + 15, -9), 7.0)
    cv.circle(B(L + 19.5, -9.5), 3.4)  # 코

    img = cv.img.filter(ImageFilter.GaussianBlur(1.2 * SS))
    img = img.point(lambda v: 255 if v >= 128 else 0)

    # 귀 두 개: 뿌리는 좁고 가운데가 넓은 긴 잎. 먼 귀는 살짝 앞에서 나와 각도가 다르고,
    # 가까운 귀와 사이에 가는 틈을 둬 두 장으로 보이게 한다. 블러 뒤에 그려 끝이 뭉개지지 않게 한다.
    base_ang = EAR_BASE
    da, curl = p["ear"]

    def ear(canvas, bx, by, a, k, grow=0.0):
        base = B(L + 7 + bx, -12 + by)
        a = ang(a)
        e1, e2 = unit(a), unit(a + curl)
        seg = 13.0 * k
        q1 = (base[0] + e1[0] * seg, base[1] + e1[1] * seg)
        q2 = (q1[0] + e2[0] * seg, q1[1] + e2[1] * seg)
        canvas.curve((base, q1, q2), lambda t: (3.0 + 2.6 * math.sin(math.pi * min(t * 0.85, 1.0))) * k + grow)

    spread = 12
    far, near, ring = Canvas(), Canvas(), Canvas()
    ear(far, 2.5, -8.5, base_ang + da + spread, 0.92)
    ear(near, -2.5, -8.0, base_ang + da, 1.0)
    ear(ring, -2.5, -8.0, base_ang + da, 1.0, grow=1.3)
    gap = ImageChops.multiply(ImageChops.subtract(ring.img, near.img), far.img)
    gap = ImageChops.subtract(gap, img)
    img = ImageChops.lighter(img, far.img)
    img = ImageChops.subtract(img, gap)
    img = ImageChops.lighter(img, near.img)

    # 다리와 몸 사이에 갇힌 작은 틈은 메운다. 앱이 갇힌 구멍을 눈으로 오해하지 않게, 눈만 구멍으로 남긴다.
    probe = img.copy()
    ImageDraw.floodfill(probe, (0, 0), 128)
    img = probe.point(lambda v: 0 if v == 128 else 255)

    # 눈 구멍 (고양이처럼 크고 둥근 두 눈)
    d = ImageDraw.Draw(img)
    # 두 눈이 붙으면 앱이 하나의 큰 구멍으로 보고 눈으로 채우지 않으므로 사이를 충분히 띄운다.
    for ex, ey in [(-2.4, -1.4), (6.6, -1.9)]:
        x, y = B(L + 7 + ex, -12 + ey)
        r = 3.1
        d.ellipse([(x - r) * SS, (y - r) * SS, (x + r) * SS, (y + r) * SS], fill=0)

    small = img.resize((W, H), Image.BOX)
    return small.filter(ImageFilter.GaussianBlur(0.45))


def render(p, style):
    probe = build(p, 0, style)
    bottom = probe.getbbox()[3]
    dy = (GROUND + 1 - p["lift"]) - bottom
    return build(p, dy, style)


if __name__ == "__main__":
    out = sys.argv[1]
    style = sys.argv[2] if len(sys.argv) > 2 else "paddle"
    for i, p in enumerate(FRAMES):
        a = render(p, style)
        rgba = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        rgba.putalpha(a)
        rgba.save(f"{out}/rabbit_running_{i}.png")
        print(i, a.getbbox())
