"""홈페이지 러너 그림 가이드(docs/draw/)에 쓰는 그림을 만든다.

    python tools/draw_guide_figures.py   (app 폴더에서, Pillow 필요)

만드는 것 (docs/assets/draw/):
- dog_running_{0..4}.png, rabbit_running_{0..4}.png: 앱 그림 그대로 복사(페이지에서 달리기 애니메이션에 쓴다).
- app-<러너>-<색>.png: 앱이 그 색으로 칠해 보여 주는 모습(색 + 흰 눈). 몸 범위로 잘랐다.
- eye-drawn.png / eye-app.png: 닥스훈트 머리를 4배로 키운 것. 투명 눈 구멍 → 흰 눈.
- hole-drawn.png / hole-app.png: 지금 토끼 머리를 4배로 키운 나쁜 예. 두 귀 사이 막힌 틈도 흰 눈처럼 칠해진다.

눈 찾기는 앱의 FrameSprite.FindEyes와 같은 규칙이다:
테두리에 닿지 않는 투명(알파 < 128) 영역 중, 넓이가 잘라낸 그림의 1% 이하이고 가로 중심이 55%보다 오른쪽인 것.
"""
import os
import shutil
from collections import deque
from PIL import Image

APP = os.path.join(os.path.dirname(__file__), "..")
ASSETS = os.path.join(APP, "src", "Woodada", "Assets")
OUT = os.path.join(APP, "..", "docs", "assets", "draw")

RUNNERS = {
    "dog": os.path.join(ASSETS, "Dog", "dog_running_{}.png"),
    "rabbit": os.path.join(ASSETS, "Rabbit", "rabbit_running_{}.png"),
}
# 설정 창 기본 색(RewardCatalog.Presets) 중 몇 개.
COLORS = {"black": (0x1E, 0x1E, 0x22), "cheese": (0xF4, 0xA2, 0x61), "mackerel": (0x6D, 0x8B, 0xA8), "cherry": (0xD9, 0x62, 0x6B)}
EYE = (255, 255, 255)


def union_crop(frames):
    """앱처럼 다섯 장의 실루엣 범위를 합친 사각형으로 자른다."""
    box = None
    for f in frames:
        b = f.getchannel("A").point(lambda a: 255 if a > 0 else 0).getbbox()
        box = b if box is None else (min(box[0], b[0]), min(box[1], b[1]), max(box[2], b[2]), max(box[3], b[3]))
    return [f.crop(box) for f in frames]


def find_eyes(img):
    w, h = img.size
    a = img.getchannel("A").load()
    seen = [[False] * w for _ in range(h)]
    eye = [[False] * w for _ in range(h)]
    for sy in range(h):
        for sx in range(w):
            if seen[sy][sx] or a[sx, sy] >= 128:
                continue
            comp, border, q = [], False, deque([(sx, sy)])
            seen[sy][sx] = True
            while q:
                x, y = q.popleft()
                comp.append((x, y))
                if x in (0, w - 1) or y in (0, h - 1):
                    border = True
                for nx, ny in ((x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)):
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny][nx] and a[nx, ny] < 128:
                        seen[ny][nx] = True
                        q.append((nx, ny))
            if border or len(comp) > w * h / 100:
                continue
            if sum(x for x, _ in comp) / len(comp) < w * 0.55:
                continue
            for x, y in comp:
                eye[y][x] = True
    # 앱처럼 1px 넓힌다.
    grown = [row[:] for row in eye]
    for y in range(h):
        for x in range(w):
            if eye[y][x]:
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        if 0 <= x + dx < w and 0 <= y + dy < h:
                            grown[y + dy][x + dx] = True
    return grown


def tint(img, color):
    """앱이 보여 주는 모습: 몸은 고른 색, 눈 구멍은 흰색."""
    w, h = img.size
    eye = find_eyes(img)
    a = img.getchannel("A").load()
    out = Image.new("RGBA", (w, h))
    px = out.load()
    for y in range(h):
        for x in range(w):
            al = a[x, y]
            if eye[y][x]:
                t = al / 255
                px[x, y] = tuple(round(c * t + e * (1 - t)) for c, e in zip(color, EYE)) + (255,)
            else:
                px[x, y] = color + (al,)
    return out


def zoom(img, box, k=4):
    part = img.crop(box)
    return part.resize((part.width * k, part.height * k), Image.NEAREST)


def head_box(img, frac_w=0.42, frac_h=0.75):
    """오른쪽 위 머리 부분."""
    w, h = img.size
    return (int(w * (1 - frac_w)), 0, w, int(h * frac_h))


def main():
    os.makedirs(OUT, exist_ok=True)
    cropped = {}
    for name, pattern in RUNNERS.items():
        frames = []
        for i in range(5):
            src = pattern.format(i)
            shutil.copyfile(src, os.path.join(OUT, os.path.basename(src)))
            frames.append(Image.open(src).convert("RGBA"))
        cropped[name] = union_crop(frames)
        for cname, rgb in COLORS.items():
            tint(cropped[name][3], rgb).save(os.path.join(OUT, f"app-{name}-{cname}.png"))

    dog = cropped["dog"][3]
    box = head_box(dog)
    zoom(dog, box).save(os.path.join(OUT, "eye-drawn.png"))
    zoom(tint(dog, COLORS["cheese"]), box).save(os.path.join(OUT, "eye-app.png"))

    # 지금 토끼 3번 프레임은 두 귀 사이 틈이 막혀 있어, 앱이 그 틈까지 눈으로 보고 흰 줄로 칠한다. 나쁜 예로 쓴다.
    rabbit = cropped["rabbit"][3]
    box = head_box(rabbit, 0.45, 0.8)
    zoom(rabbit, box).save(os.path.join(OUT, "hole-drawn.png"))
    zoom(tint(rabbit, COLORS["cheese"]), box).save(os.path.join(OUT, "hole-app.png"))
    print("saved to", os.path.abspath(OUT))


if __name__ == "__main__":
    main()
