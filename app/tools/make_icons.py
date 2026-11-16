"""앱 아이콘 원본(Woodada.png, 1024px)에서 아이콘 파일을 모두 만든다.

    python tools/make_icons.py   (Pillow 필요)

- src/Woodada/Assets/Icon/Woodada.ico   exe·창·Windows 트레이 (16~256px)
- src/Woodada/Assets/Icon/Woodada.icns  macOS .app (16~1024px)
- docs/icon.png, ../docs/assets/icon.png  README와 홈페이지 (512px)

크기마다 원본을 BOX로 줄인다(픽셀 면적 평균이라 작은 크기에서도 윤곽이 뭉개지지 않는다).
128px보다 작은 크기는 모양을 칸에 꽉 채우고 1px 테두리를 다시 긋는다(small 참고).
icns는 iconutil 없이 돌아가도록 직접 쓴다.
macOS 메뉴 막대 아이콘(MenuBarTemplate.png)은 모양이 달라 tools/draw_menubar_icon.py가 따로 만든다.
홈페이지·도감 공유 이미지에 들어간 아이콘은 이 스크립트 뒤에 tools/cat_og.py를 돌려 다시 찍는다.
"""

import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ICON_DIR = ROOT / "src" / "Woodada" / "Assets" / "Icon"
SOURCE = ICON_DIR / "Woodada.png"
ICO = ICON_DIR / "Woodada.ico"
ICNS = ICON_DIR / "Woodada.icns"
WEB = [ROOT / "docs" / "icon.png", ROOT.parent / "docs" / "assets" / "icon.png"]
WEB_SIZE = 512

# Windows가 쓰는 크기: 탐색기·작업표시줄·Alt+Tab·트레이와 화면 배율(100~250%)별 크기.
ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]

# icns 항목 종류 → 픽셀 크기. 이름에 @2x가 붙는 레티나용 항목도 같은 PNG 형식이다.
ICNS_ENTRIES = [
    ("icp4", 16), ("icp5", 32), ("icp6", 64),
    ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
    ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512),
]


def scaled(source: Image.Image, size: int) -> Image.Image:
    if size < SMALL:
        return small(source, size)
    return source if source.width == size else source.resize((size, size), Image.BOX)


# 128px보다 작으면 그대로 줄였을 때 테두리가 1px도 안 돼 어두운 작업표시줄에서 검정 반쪽이 바탕에 묻힌다.
# 그래서 원본의 바깥 여백을 잘라 모양을 칸에 꽉 채우고, 맨 바깥 1px에 회색 테두리를 다시 긋는다.
SMALL = 128
MARGIN, RADIUS = 8, 152          # 원본(1024) 기준 바깥 여백, 여백을 뺀 모양의 모서리 반지름
BORDER = (96, 96, 104, 255)


def small(source: Image.Image, size: int) -> Image.Image:
    inner = 1024 - 2 * MARGIN
    body = source.crop((MARGIN, MARGIN, 1024 - MARGIN, 1024 - MARGIN)).resize((size, size), Image.BOX)
    k = 8                                         # 테두리를 매끈하게 그리려고 크게 그려서 줄인다
    ring = Image.new("RGBA", (size * k, size * k), (0, 0, 0, 0))
    ImageDraw.Draw(ring).rounded_rectangle(
        (0, 0, size * k - 1, size * k - 1), radius=round(RADIUS / inner * size * k), outline=BORDER, width=k)
    body.alpha_composite(ring.resize((size, size), Image.BOX))
    return body


def png_bytes(image: Image.Image) -> bytes:
    out = io.BytesIO()
    image.save(out, format="PNG", optimize=True)
    return out.getvalue()


def write_icns(source: Image.Image) -> None:
    body = b""
    for kind, size in ICNS_ENTRIES:
        png = png_bytes(scaled(source, size))
        body += kind.encode("ascii") + struct.pack(">I", 8 + len(png)) + png
    ICNS.write_bytes(b"icns" + struct.pack(">I", 8 + len(body)) + body)


def write_ico(source: Image.Image) -> None:
    # 크기마다 직접 줄인 그림을 넘긴다. 넘기지 않으면 Pillow가 가장 큰 그림을 알아서 줄인다.
    frames = [scaled(source, s) for s in ICO_SIZES]
    frames[-1].save(ICO, format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA")
    if source.size != (1024, 1024):
        raise SystemExit(f"{SOURCE.name}는 1024×1024여야 합니다: {source.size}")
    write_ico(source)
    write_icns(source)
    for path in WEB:
        scaled(source, WEB_SIZE).save(path, optimize=True)
    for path in [ICO, ICNS, *WEB]:
        print(f"{path.relative_to(ROOT.parent)} ({path.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
