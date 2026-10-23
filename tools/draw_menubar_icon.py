"""macOS 메뉴 막대 아이콘을 그린다: 맨 아래 밑줄(독) 위를 달리는 고양이.

결과물: src/Woodada/Assets/Icon/MenuBarTemplate.png
    python tools/draw_menubar_icon.py   (Pillow 필요)

검정 + 투명만 쓰는 템플릿 이미지다. macOS가 메뉴 막대 색에 맞춰 흰색이나 검은색으로 칠한다.

Avalonia는 메뉴 막대 아이콘을 가로 22pt(메뉴 막대 두께만 한 정사각 칸) × 세로 18.7pt(메뉴 글자 크기 × 4/3)에
넣는다. 세로는 비율을 지켜 이 높이로 맞추고, 가로가 22pt를 넘으면 눌러서 납작하게 만든다.
그래서 그 칸 전체(레티나 2배로 44×37px)를 캔버스로 잡는다. 맨 아래에 칸 너비만 한 밑줄을 긋고,
그 위에 고양이를 칸 너비에 꽉 차게 놓는다.
고양이는 RunCat365 프레임(Assets/RunCat/cat_running_0.png, Apache-2.0)을 줄여 쓰고, 눈 구멍은 그대로 둔다.
0번 프레임이 가장 덜 넓적해서 이 칸에 가장 크게 들어간다.
"""

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src/Woodada/Assets/RunCat/cat_running_0.png"
OUT = ROOT / "src/Woodada/Assets/Icon/MenuBarTemplate.png"

WIDTH = 44     # 22pt × 2
HEIGHT = 37    # 18.7pt × 2
LINE = 3       # 밑줄 두께(1.5pt)
GAP = 1        # 발과 밑줄 사이
SS = 8         # 밑줄의 둥근 끝을 매끈하게 그리려고 크게 그려서 줄인다


def main():
    cat = Image.open(SRC).convert("RGBA")
    solid = cat.getchannel("A").point(lambda a: 255 if a > 8 else 0)
    cat_alpha = cat.getchannel("A").crop(solid.getbbox())

    # 칸에 들어가는 가장 큰 크기. 0번 프레임은 가로가 먼저 찬다.
    room_h = HEIGHT - LINE - GAP
    scale = min(WIDTH / cat_alpha.width, room_h / cat_alpha.height)
    cat_w, cat_h = round(cat_alpha.width * scale), round(cat_alpha.height * scale)

    big = Image.new("L", (WIDTH * SS, HEIGHT * SS), 0)
    ImageDraw.Draw(big).rounded_rectangle(
        [0, (HEIGHT - LINE) * SS, WIDTH * SS - 1, HEIGHT * SS - 1], radius=LINE * SS / 2, fill=255)
    line = big.resize((WIDTH, HEIGHT), Image.LANCZOS)

    # 고양이 발이 밑줄 바로 위에 닿게 놓는다.
    body = Image.new("L", (WIDTH, HEIGHT), 0)
    body.paste(cat_alpha.resize((cat_w, cat_h), Image.LANCZOS), ((WIDTH - cat_w) // 2, room_h - cat_h))

    alpha = ImageChops.lighter(line, body)
    black = Image.new("L", (WIDTH, HEIGHT), 0)
    Image.merge("RGBA", (black, black, black, alpha)).save(OUT)
    print(f"{OUT.relative_to(ROOT)}: {WIDTH}x{HEIGHT}px (고양이 {cat_w}x{cat_h}px)")


if __name__ == "__main__":
    main()
