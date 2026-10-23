"""Woodada.ico에서 macOS 앱 아이콘(Woodada.icns)을 만든다.

    python tools/make_icns.py

ico에 들어 있는 가장 큰 프레임(256px)을 기준으로 맥이 쓰는 크기를 모두 PNG로 담는다.
512·1024px은 ico에 없어서 256px을 늘려 만든다. iconutil 없이 돌아가도록 icns를 직접 쓴다.
"""

import io
import struct
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "src" / "Woodada" / "Assets" / "Icon" / "Woodada.ico"
TARGET = ROOT / "src" / "Woodada" / "Assets" / "Icon" / "Woodada.icns"

# icns 항목 종류 → 픽셀 크기. 이름에 @2x가 붙는 레티나용 항목도 같은 PNG 형식이다.
ENTRIES = [
    ("icp4", 16), ("icp5", 32), ("icp6", 64),
    ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024),
    ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512),
]


def frames(ico: Path) -> dict[int, Image.Image]:
    """ico 안의 프레임을 크기별로 꺼낸다."""
    found = {}
    with Image.open(ico) as im:
        for size in im.info["sizes"]:
            im.size = size
            found[size[0]] = im.convert("RGBA").copy()
    return found


def render(found: dict[int, Image.Image], size: int) -> bytes:
    # 같은 크기가 있으면 그대로, 없으면 그보다 큰 것 중 가장 작은 것을 줄이고, 그것도 없으면 가장 큰 것을 늘린다.
    bigger = [s for s in found if s >= size]
    base = found[min(bigger)] if bigger else found[max(found)]
    image = base if base.width == size else base.resize((size, size), Image.LANCZOS)
    out = io.BytesIO()
    image.save(out, format="PNG")
    return out.getvalue()


def main() -> None:
    found = frames(SOURCE)
    body = b""
    for kind, size in ENTRIES:
        png = render(found, size)
        body += kind.encode("ascii") + struct.pack(">I", 8 + len(png)) + png
    TARGET.write_bytes(b"icns" + struct.pack(">I", 8 + len(body)) + body)
    print(f"{TARGET.relative_to(ROOT)} ({TARGET.stat().st_size:,} bytes)")


if __name__ == "__main__":
    main()
