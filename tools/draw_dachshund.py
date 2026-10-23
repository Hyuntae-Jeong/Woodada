"""닥스훈트 달리기 5프레임을 RunCat 고양이 그림체(검은 둥근 실루엣 + 투명 눈 구멍)로 그린다.

결과물: src/Woodada/Assets/Dog/dog_running_{0..4}.png
    python tools/draw_dachshund.py src/Woodada/Assets/Dog   (Pillow 필요)

앱은 이 PNG의 알파만 쓰고 색은 설정값으로 칠한다. 눈은 머리 쪽(오른쪽)의 작은 투명 구멍이어야
FrameSprite가 찾아서 흰색으로 채운다.
"""
import math, sys
from PIL import Image, ImageDraw, ImageFilter

W, H = 170, 100      # 출력 캔버스 (고양이는 120x100)
SS = 8               # 슈퍼샘플링 배율
GROUND = 99.0        # 땅에 닿은 프레임의 발 바닥선
HS = 1.3             # 머리(두개골·주둥이·눈·귀) 배율. 작은 크기에서도 긴 코가 보이도록 키웠다.

# 프레임별 자세. 각도: 다리는 수직 기준 +가 앞(오른쪽), tilt는 +가 머리 들기.
# ear=(방향, 휨): 방향은 화면 각도(180=뒤, 90=아래, 180보다 크면 위로 젖혀짐).
#   휨은 귀 끝이 늦게 따라오는 정도라 귀가 올라갈 땐 끝이 처지고(-), 내려갈 땐 끝이 들린다(+).
FRAMES = [
    # 0: 모으기 — 몸이 짧아지고 등이 둥글게. 뒷다리는 배 밑으로, 앞다리는 뒤로. 꼬리는 높이.
    dict(lift=0, L=56, tilt=3,  arch=-8, front=(-22, -2), back=(42, 24), tail=(-135, 30), ear=(165, 30), head=0),
    # 1: 앞발 착지 — 앞다리 뻗어 내리고 뒷다리는 따라오는 중.
    dict(lift=0, L=60, tilt=-4, arch=-4, front=(32, 10),  back=(20, 0),  tail=(-160, 18), ear=(186, -24), head=-5),
    # 2: 뒷발 밀기 — 앞다리는 몸 밑, 뒷다리는 뒤로 차고, 머리 든다.
    dict(lift=0, L=66, tilt=5,  arch=0,  front=(-10, 14), back=(-26, -44), tail=(-160, -14), ear=(208, -34), head=5),
    # 3: 공중 — 앞뒤로 쭉 뻗고 몸이 가장 길다. 귀가 뒤로 날린다.
    dict(lift=7, L=72, tilt=6,  arch=3,  front=(58, 38), back=(-52, -34), tail=(-168, -8), ear=(222, -40), head=2),
    # 4: 내려오기 — 아직 길지만 앞다리를 내린다.
    dict(lift=4, L=68, tilt=-2, arch=2,  front=(44, 26), back=(-42, -22), tail=(-163, 10), ear=(196, 34), head=-3),
]


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

    def curve(self, pts, r1, r2):
        """2차 베지어를 따라 반지름이 변하는 원을 찍는다. r2가 함수면 t(0~1)→반지름."""
        (x0, y0), (x1, y1), (x2, y2) = pts
        n = 80
        for i in range(n + 1):
            t = i / n
            x = (1 - t) ** 2 * x0 + 2 * (1 - t) * t * x1 + t * t * x2
            y = (1 - t) ** 2 * y0 + 2 * (1 - t) * t * y1 + t * t * y2
            self.circle((x, y), r2(t) if callable(r2) else r1 + (r2 - r1) * t)

    def ellipse(self, c, a, b, ang_deg):
        th = math.radians(ang_deg)
        pts = []
        for i in range(72):
            u = 2 * math.pi * i / 72
            ex, ey = a * math.cos(u), b * math.sin(u)
            pts.append(((c[0] + ex * math.cos(th) - ey * math.sin(th)) * SS,
                        (c[1] + ex * math.sin(th) + ey * math.cos(th)) * SS))
        self.d.polygon(pts, fill=255)


def unit(deg):
    r = math.radians(deg)
    return math.cos(r), math.sin(r)


def build(p, dy):
    cv = Canvas()
    tilt = math.radians(-p["tilt"])  # 화면 y가 아래라 반대로
    ct, st = math.cos(tilt), math.sin(tilt)

    L = p["L"]
    rump = (112.0 - L, 68.0 + dy)  # 가슴 위치를 고정해 머리가 앞뒤로 덜 흔들리게

    def B(x, y, origin=rump):
        """몸 좌표계(엉덩이 기준, x 앞, y 아래) → 화면."""
        return origin[0] + x * ct - y * st, origin[1] + x * st + y * ct

    # 몸통: 소시지. 아치는 가운데를 위/아래로 휜다.
    n = 60
    for i in range(n + 1):
        t = i / n
        x = L * t
        y = p["arch"] * 4 * t * (1 - t)
        r = 10.2 + 2.0 * t * t - 0.8 * math.sin(math.pi * t)  # 허리는 살짝 잘록, 가슴 쪽이 두껍다
        cv.circle(B(x, y), r)
    chest = B(L, 0)
    cv.circle(B(L - 5, 4), 10.5)        # 닥스훈트 특유의 깊은 가슴
    cv.circle(B(2, 1), 10.0)            # 둥근 엉덩이

    # 다리: 짧고 뭉툭하게. 바깥 다리를 먼저, 안쪽 다리 관절은 살짝 어긋나게.
    LEG = 17.0

    def leg(joint, ang, rr=3.7):
        a = math.radians(ang)
        dx, dy_ = math.sin(a) * LEG, math.cos(a) * LEG
        foot = (joint[0] + dx * ct - dy_ * st, joint[1] + dx * st + dy_ * ct)
        cv.capsule(joint, foot, rr, rr * 0.9)
        cv.circle((foot[0] + 1.0, foot[1] - 0.2), rr * 1.0)  # 발

    for k, ang in enumerate(p["front"]):
        leg(B(L - 6 + k * 6, 4), ang)
    for k, ang in enumerate(p["back"]):
        leg(B(3 + k * 6, 3), ang)

    # 목과 머리
    hp = math.radians(-p["head"])
    head = B(L + 15 + 9 * (HS - 1), -19 - 12 * (HS - 1))  # 머리가 커진 만큼 앞·위로
    cv.capsule(B(L - 4, -4), head, 9.5, 8.5)

    def Hd(x, y):
        x, y = x * HS, y * HS
        x2, y2 = x * math.cos(hp) - y * math.sin(hp), x * math.sin(hp) + y * math.cos(hp)
        return B(x2, y2, origin=head)

    # 두개골: 살짝 납작한 타원
    hx, hy = Hd(-1, -1)
    cv.ellipse((hx, hy), 11.8 * HS, 10.6 * HS, -p["tilt"] - p["head"])

    # 귀: 크고 늘어진 잎 모양. 뿌리는 좁고 끝으로 갈수록 넓어지며, 끝이 한 박자 늦게 따라와 휘어진다.
    base = Hd(-6, -7)
    e_ang, e_curl = p["ear"]
    e1 = unit(e_ang - p["tilt"])
    e2 = unit(e_ang - p["tilt"] + e_curl)
    q1 = (base[0] + e1[0] * 10 * HS, base[1] + e1[1] * 10 * HS)
    q2 = (q1[0] + e2[0] * 10 * HS, q1[1] + e2[1] * 10 * HS)
    cv.curve((base, q1, q2), 0, lambda t: (4.4 + 2.9 * math.sin(math.pi * t * 0.75)) * HS)  # 가운데가 넓은 잎 모양

    # 꼬리: 엉덩이 위에서 시작해 가늘어진다.
    t0 = B(-4, -6)
    ang, curl = p["tail"]
    a1 = ang - p["tilt"]
    d1 = unit(a1)
    d2 = unit(a1 + curl)
    p1 = (t0[0] + d1[0] * 13, t0[1] + d1[1] * 13)
    p2 = (p1[0] + d2[0] * 13, p1[1] + d2[1] * 13)
    cv.curve((t0, p1, p2), 2.9, 2.1)

    # 둥근 덩어리 느낌: 흐리게 한 뒤 다시 잘라 이음매를 부드럽게 메운다.
    img = cv.img.filter(ImageFilter.GaussianBlur(1.2 * SS))
    img = img.point(lambda v: 255 if v >= 128 else 0)

    # 주둥이: 두개골에서 코끝까지 좁아지는 쐐기. 윗선은 이마에서 코로 길게 내려가고 아랫턱은 거의 곧다.
    # 블러 뒤에 그려야 코끝이 뭉개지지 않는다.
    snout = Canvas()
    snout.img = img
    snout.d = ImageDraw.Draw(img)
    snout.capsule(Hd(2, 0.5), Hd(13, 3.0), 8.6 * HS, 6.0 * HS)
    snout.capsule(Hd(13, 3.0), Hd(24, 5.0), 6.0 * HS, 2.4 * HS)
    snout.circle(Hd(24.6, 4.0), 2.9 * HS)            # 코끝

    # 눈 구멍 (고양이처럼 크고 둥근 두 눈)
    d = ImageDraw.Draw(img)
    for ex, ey in [(-3.0, -3.2), (5.5, -3.6)]:
        x, y = Hd(ex, ey)
        r = 3.5 * HS ** 0.6   # 눈은 머리만큼 키우면 너무 커서 덜 키운다
        d.ellipse([(x - r) * SS, (y - r) * SS, (x + r) * SS, (y + r) * SS], fill=0)

    small = img.resize((W, H), Image.BOX)
    return small.filter(ImageFilter.GaussianBlur(0.45))


def render(p):
    # 한 번 그려서 발 바닥 위치를 재고, 바닥선 - lift에 맞춰 다시 그린다.
    probe = build(p, 0)
    bottom = probe.getbbox()[3]
    dy = (GROUND + 1 - p["lift"]) - bottom
    return build(p, dy)


if __name__ == "__main__":
    out = sys.argv[1]
    for i, p in enumerate(FRAMES):
        a = render(p)
        rgba = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        rgba.putalpha(a)
        rgba.save(f"{out}/dog_running_{i}.png")
        print(i, a.getbbox())
