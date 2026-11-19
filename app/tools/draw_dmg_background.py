"""macOS 설치용 DMG 창의 배경을 그린다: 파스텔 바탕화면 위 반투명 독을 고양이 두 마리가 달린다.

결과물: build/mac/dmg/background.png (660×420), build/mac/dmg/background@2x.png (1320×840)
    python tools/draw_dmg_background.py   (Pillow 필요)

그림을 바꾼 뒤에는 build/mac/make-dmg-template.sh로 DMG 틀을 다시 만들어야 릴리즈에 반영된다.

- Finder는 배경 그림이 있으면 창을 밝은 모양으로 보고 아이콘 이름을 검은 글자로 그린다(다크 모드여도).
  그래서 아이콘과 이름이 놓이는 위쪽은 밝게 둔다.
- Finder는 배경을 움직이지 않는다(GIF도 첫 장면만 보인다). 대신 속도선과 먼지로 달리는 느낌을 낸다.
- 아이콘 자리(가운데 좌표)는 make-dmg-template.sh와 같아야 한다: Woodada (170, 190), Applications (490, 190).
- 고양이는 RunCat365 프레임(src/Woodada/Assets/RunCat, Apache-2.0)에 색만 입혀 쓴다. 눈·귀 안쪽 구멍도 원본 그대로다.
  원본이 작아(고양이 높이 66px) 키울 때는 윤곽을 매끈하게 다듬어 계단이 지지 않게 한다.
"""

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont, ImageOps

ROOT = Path(__file__).resolve().parent.parent
CATS = ROOT / "src/Woodada/Assets/RunCat"
OUT = ROOT / "build/mac/dmg"

S = 2                      # 2배로 그리고 1배는 줄여 만든다
W, H = 660, 420            # 창 내용 크기(pt). 제목 막대는 Finder가 따로 그린다.
ICON_Y = 190
APP_X, APPS_X = 170, 490
DOCK_TOP = 362             # 고양이가 밟는 독 윗면
BIG_PT, SMALL_PT = 48, 28  # 검은 고양이·흰 고양이 높이(pt). 원본(66px)을 약 1.5배까지만 키운다.
# 서명이 없어 처음 열 때 Gatekeeper가 막는다. 끌어다 놓은 뒤엔 창을 띄울 방법이 없어서 배경 맨 위에 넘기는 길을 적어 둔다.
# 끌어다 놓으라는 문구는 화살표만으로 충분해서 빼고, 이 안내가 먼저 눈에 들어오게 한다.
# 배경은 맥 언어를 따라 바뀌지 않으므로 한국어·영어를 같이 쓴다.
# macOS 12는 메뉴 이름이 달라(시스템 환경설정 → 보안 및 개인 정보 보호 → 확인 없이 열기) 13 이상 기준으로 적는다.
GUIDE = [
    ("경고가 뜨면   ", "시스템 설정 → 개인정보 보호 및 보안 → 그래도 열기"),
    ("If blocked   ", "System Settings → Privacy & Security → Open Anyway"),
]
GUIDE_BOX = (90, 40, 570, 106)   # 아이콘(위끝 134pt)과 겹치지 않는 자리

DARK = (30, 30, 34)        # 앱 아이콘의 검은 반쪽
WHITE = (255, 255, 255)
EYE_DARK = (45, 42, 48)
INK = (90, 96, 120)        # 속도선·먼지
AMBER_FILL = (255, 236, 200, 235)   # 안내 카드: 경고 느낌은 주되 빨강처럼 겁주지 않는 주황
AMBER_LINE = (240, 170, 70, 255)
AMBER_GLOW = (255, 190, 90, 120)
AMBER_INK = (150, 90, 20)           # "경고가 뜨면" 같은 앞머리
WARN = (245, 150, 40)               # 경고 삼각형

# (파일, ttc 안 번호, 크기 배율). 맥 글꼴이 Finder 글씨와 어울려 맥에서 그린다. 맑은 고딕은 윈도우에서 미리 볼 때만 쓴다.
# Apple SD Gothic Neo는 같은 pt에서 글자가 작아 10% 키운다. 6 = Bold.
FONTS = [("/System/Library/Fonts/AppleSDGothicNeo.ttc", 6, 1.1), ("C:/Windows/Fonts/malgunbd.ttf", 0, 1.0)]


def px(v):
    return int(round(v * S))


def font(size_pt):
    for path, index, scale in FONTS:
        if Path(path).exists():
            return ImageFont.truetype(path, px(size_pt * scale), index=index)
    raise FileNotFoundError("한글 글꼴을 찾지 못했습니다: " + ", ".join(p for p, _, _ in FONTS))


def smooth_resize(mask, size, src_px_blur=0.35):
    """작은 실루엣을 키워도 계단이 지지 않게: 크게 늘려 살짝 흐린 뒤 반으로 잘라 매끈한 윤곽을 만들고, 줄여서 가장자리를 부드럽게 한다."""
    ss = 4
    tw, th = size
    big = mask.resize((tw * ss, th * ss), Image.BICUBIC)
    scale = tw * ss / mask.width
    big = big.filter(ImageFilter.GaussianBlur(src_px_blur * scale))
    big = big.point(lambda v: 255 if v >= 128 else 0)
    return big.resize((tw, th), Image.LANCZOS)


def cat_masks(frame, height_pt):
    """원본 프레임의 몸 알파와, 눈·귀 안쪽 구멍까지 메운 실루엣 알파를 목표 높이로 키워 돌려준다."""
    a = Image.open(CATS / f"cat_running_{frame}.png").convert("RGBA").getchannel("A")
    a = a.crop(a.point(lambda v: 255 if v > 8 else 0).getbbox())
    w, h = a.size
    # 바깥과 이어지지 않은 투명 구멍(눈, 귀 안쪽)을 메운 실루엣
    pad = ImageOps.expand(a.point(lambda v: 255 if v >= 128 else 0), 1, fill=0)
    ImageDraw.floodfill(pad, (0, 0), 128)
    holes = pad.point(lambda v: 255 if v == 0 else 0).crop((1, 1, w + 1, h + 1))
    filled = ImageChops.lighter(a, holes)
    th = px(height_pt)
    size = (round(w * th / h), th)
    return smooth_resize(a, size), smooth_resize(filled, size)


def paint_cat(canvas, frame, height_pt, left_pt, body, eye=None, shadow=None):
    """eye가 없으면 원본처럼 눈·귀 안쪽을 비워 바탕이 비친다. 밝은 고양이는 그 구멍을 eye 색으로 칠한다."""
    alpha, filled = cat_masks(frame, height_pt)
    x, y = px(left_pt), px(DOCK_TOP) - alpha.height
    if shadow:
        # 흰 고양이가 밝은 바탕에 묻히지 않게 아래로 살짝 번지는 그림자
        pad = px(8)
        soft = ImageOps.expand(filled, pad, fill=0).filter(ImageFilter.GaussianBlur(px(2.5)))
        soft = soft.point(lambda v: int(v * 0.55))
        canvas.paste(Image.new("RGB", soft.size, shadow), (x - pad, y - pad + px(1)), soft)
    if eye:
        canvas.paste(Image.new("RGB", filled.size, eye), (x, y), filled)
    canvas.paste(Image.new("RGB", alpha.size, body), (x, y), alpha)
    return x, y, alpha.width, alpha.height


def motion(canvas, box):
    """고양이 뒤쪽 속도선 세 줄과 발밑 먼지."""
    x, y, w, h = box
    layer = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    t = max(px(2), h // 22)
    for frac, length in ((0.30, 0.55), (0.52, 0.85), (0.74, 0.40)):
        ly = y + int(h * (1 - frac))
        lx1 = x - px(6)
        d.rounded_rectangle([lx1 - int(h * length), ly - t // 2, lx1, ly + t // 2], radius=t // 2, fill=INK + (90,))
    gy = px(DOCK_TOP) - px(3)
    for dx, r in ((-px(10), px(3.2)), (-px(20), px(2.2)), (-px(28), px(1.4))):
        d.ellipse([x + dx - r, gy - r, x + dx + r, gy + r], fill=INK + (90,))
    canvas.paste(layer, (0, 0), layer)


def background():
    im = Image.new("RGB", (px(W), px(H)))
    d = ImageDraw.Draw(im)
    stops = [(0, (250, 236, 222)), (0.55, (236, 226, 236)), (1, (205, 216, 240))]
    for yy in range(px(H)):
        t = yy / (px(H) - 1)
        for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
            if p0 <= t <= p1:
                k = (t - p0) / (p1 - p0)
                d.line([(0, yy), (px(W), yy)], fill=tuple(round(a + (b - a) * k) for a, b in zip(c0, c1)))
                break
    return im


def arrow(d, color):
    t = px(4)
    x0, x1, yy = px(APP_X + 88), px(APPS_X - 88), px(ICON_Y)
    head = px(13)
    d.rounded_rectangle([x0, yy - t // 2, x1 - px(4), yy + t // 2], radius=t // 2, fill=color)
    d.line([(x1 - head, yy - head), (x1, yy), (x1 - head, yy + head)], fill=color, width=t, joint="curve")
    for p in ((x1 - head, yy - head), (x1 - head, yy + head), (x1, yy)):
        d.ellipse([p[0] - t // 2, p[1] - t // 2, p[0] + t // 2, p[1] + t // 2], fill=color)


def dock(im):
    """반투명 흰 판과 작은 앱 아이콘들. 실제 독을 흉내 낸다."""
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.rounded_rectangle([px(70), px(DOCK_TOP), px(W - 70), px(DOCK_TOP + 44)], radius=px(16),
                        fill=(255, 255, 255, 120), outline=(255, 255, 255, 210), width=px(1))
    colors = [(255, 179, 153), (255, 214, 140), (163, 214, 170), (140, 190, 240),
              (196, 170, 236), (240, 160, 190), (170, 170, 180)]
    size, gap = 28, 12
    x = (W - (len(colors) * size + (len(colors) - 1) * gap)) / 2
    for c in colors:
        d.rounded_rectangle([px(x), px(DOCK_TOP + 8), px(x + size), px(DOCK_TOP + 8 + size)], radius=px(7), fill=c + (150,))
        x += size + gap
    im.paste(layer, (0, 0), layer)


def warn_icon(im, cx, cy, size):
    """둥근 주황 삼각형에 흰 느낌표."""
    layer = Image.new("RGBA", im.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    h = size * 0.88
    pts = [(px(cx), px(cy - h / 2)), (px(cx + size / 2), px(cy + h / 2)), (px(cx - size / 2), px(cy + h / 2))]
    d.polygon(pts, fill=WARN)
    d.line(pts + [pts[0]], fill=WARN, width=px(3), joint="curve")  # 모서리를 둥글게
    for p in pts:
        d.ellipse([p[0] - px(1.5), p[1] - px(1.5), p[0] + px(1.5), p[1] + px(1.5)], fill=WARN)
    t = px(size * 0.13)
    d.rounded_rectangle([px(cx) - t // 2, px(cy - h * 0.14), px(cx) + t // 2, px(cy + h * 0.2)], radius=t // 2, fill=WHITE)
    dot_y = px(cy + h * 0.29)
    d.ellipse([px(cx) - t // 2, dot_y - t // 2, px(cx) + t // 2, dot_y + t // 2], fill=WHITE)
    im.paste(layer, (0, 0), layer)


def guide_card(im):
    """맨 위 주황 카드: 경고 아이콘 + 한국어·영어 한 줄씩. 앞머리는 주황 글씨, 메뉴 경로는 진하게."""
    box = [px(v) for v in GUIDE_BOX]
    glow = Image.new("RGBA", im.size, (0, 0, 0, 0))
    ImageDraw.Draw(glow).rounded_rectangle(box, radius=px(14), fill=AMBER_GLOW)
    glow = glow.filter(ImageFilter.GaussianBlur(px(7)))
    im.paste(glow, (0, 0), glow)
    card = Image.new("RGBA", im.size, (0, 0, 0, 0))
    ImageDraw.Draw(card).rounded_rectangle(box, radius=px(14), fill=AMBER_FILL, outline=AMBER_LINE, width=px(1.5))
    im.paste(card, (0, 0), card)

    d = ImageDraw.Draw(im)
    lines = [((GUIDE[0][0], font(12)), (GUIDE[0][1], font(12.5))),
             ((GUIDE[1][0], font(11.5)), (GUIDE[1][1], font(12)))]
    icon, gap = 22, 12
    text_w = max(sum(d.textlength(t, font=f) for t, f in line) for line in lines) / px(1)
    left = (W - (icon + gap + text_w)) / 2   # 아이콘과 글을 한 덩어리로 가운데에
    warn_icon(im, left + icon / 2, (GUIDE_BOX[1] + GUIDE_BOX[3]) / 2, icon)
    # 두 줄을 기준선(baseline)으로 25pt 간격에 두고, 실제 글자 윤곽 전체가 카드 세로 가운데에 오게 옮긴다.
    # 글꼴마다 위아래 여백이 달라서 윗선 기준으로 놓으면 글꼴을 바꿀 때 글이 위나 아래로 쏠린다.
    pitch = px(25)
    ink = [d.textbbox((0, i * pitch), t, font=f, anchor="ls") for i, line in enumerate(lines) for t, f in line]
    top, bottom = min(b[1] for b in ink), max(b[3] for b in ink)
    base = px((GUIDE_BOX[1] + GUIDE_BOX[3]) / 2) - (top + bottom) / 2
    for i, line in enumerate(lines):
        x = px(left + icon + gap)
        for (text, f), color in zip(line, (AMBER_INK, DARK)):
            d.text((x, base + i * pitch), text, font=f, fill=color, anchor="ls")
            x += d.textlength(text, font=f)


def main():
    im = background()
    d = ImageDraw.Draw(im)
    guide_card(im)
    arrow(d, (140, 132, 150))
    dock(im)
    # 큰 검은 고양이가 작은 흰 고양이를 쫓는다.
    motion(im, paint_cat(im, 3, BIG_PT, 120, DARK))
    motion(im, paint_cat(im, 1, SMALL_PT, 440, WHITE, eye=EYE_DARK, shadow=INK))

    OUT.mkdir(parents=True, exist_ok=True)
    im.save(OUT / "background@2x.png")
    im.resize((W, H), Image.LANCZOS).save(OUT / "background.png")
    print(f"{(OUT / 'background.png').relative_to(ROOT)}, background@2x.png")


if __name__ == "__main__":
    main()
