#!/usr/bin/env python3
"""고양이 도감·프로필 페이지의 공유 미리보기 이미지(1200×630)를 만든다.

    python app/tools/cat_og.py

- docs/assets/og/cats.png      도감 첫 화면 (세 마리)
- docs/assets/og/<id>.png      고양이마다 한 장

글(이름·등급·한마디)은 cat_pages.py의 CATS에서 가져오고, 고양이와 무대는 docs/cats/cats.js의
drawArcade·drawPoster로 그린다(앱 스킨 값 그대로). 잠깐 로컬 서버를 띄우고 헤드리스 Chrome/Edge로 찍는다.
Chrome이나 Edge가 필요하다. 다른 곳에 있으면 환경 변수 WOODADA_BROWSER에 실행 파일 경로를 넣는다.
"""
from __future__ import annotations

import functools
import html
import io
import os
import subprocess
import sys
import tempfile
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from cat_pages import CATS, TIERS  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
DOCS = ROOT / "docs"
OUT = DOCS / "assets" / "og"
W, H = 1200, 630

BROWSERS = [
    os.environ.get("WOODADA_BROWSER", ""),
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
]

# 이름 글자 색(위→아래). docs/cats/cats.css의 --name-a~d와 같다.
NAME_COLORS = {
    "ghost": ("#ffffff", "#e4f1ff", "#8ec5ff", "#4f8fe0"),
    "flash": ("#fff6c2", "#ffd319", "#ff8a1f", "#ff3e3e"),
    "glitch": ("#ffd1f0", "#ff3ea5", "#c07bff", "#2de2e6"),
    "all": ("#fff6c2", "#ffd319", "#ff8a1f", "#ff3e3e"),
}

PAGE = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<link rel="stylesheet" href="https://cdn.jsdelivr.net/gh/orioncactus/pretendard@v1.3.9/dist/web/variable/pretendardvariable-dynamic-subset.min.css">
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@fontsource/black-han-sans@5.2.8/index.css">
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@fontsource/press-start-2p@5.2.7/index.css">
<style>
  html, body { margin: 0; width: 1200px; height: 630px; overflow: hidden; background: #07060f; }
  body { position: relative; font-family: "Pretendard Variable", Pretendard, sans-serif; color: #f5ecff; }
  canvas { position: absolute; inset: 0; width: 1200px; height: 630px; }
  .frame { position: absolute; inset: 14px; border: 4px solid #ff3ea5; box-shadow: 0 0 0 4px #07060f, 0 0 0 8px #2de2e6, inset 0 0 50px rgba(255, 62, 165, .28); pointer-events: none; }
  .scan { position: absolute; inset: 0; background: repeating-linear-gradient(0deg, rgba(0, 0, 0, .18) 0 1px, transparent 1px 3px); }
  .text { position: absolute; left: 66px; top: 58px; }
  .top { margin: 0; font: 400 17px/1 "Press Start 2P", monospace; color: #ffd319; text-shadow: 3px 3px 0 #b3105f; letter-spacing: .04em; }
  .tier { display: inline-block; margin-top: 30px; font: 400 16px/1 "Press Start 2P", monospace; color: #07060f; background: #ffd319; padding: 9px 12px; }
  .name {
    margin: 14px 0 0; font: 400 {{NAME_SIZE}}px/1.05 "Black Han Sans", sans-serif; letter-spacing: -.01em;
    background: linear-gradient(180deg, {{C1}} 0%, {{C2}} 35%, {{C3}} 62%, {{C4}} 100%);
    -webkit-background-clip: text; background-clip: text; color: transparent;
    filter: drop-shadow(5px 5px 0 #5b0d6b);
  }
  .ko { margin: 8px 0 0; font: 400 46px/1.1 "Black Han Sans", sans-serif; color: #2de2e6; }
  .quote { position: absolute; right: 64px; top: 52px; margin: 0; white-space: nowrap; background: #fff; color: #07060f; font: 400 28px/1.3 "Black Han Sans", sans-serif; padding: 13px 20px; box-shadow: 6px 6px 0 #ff3ea5; }
  .brand { position: absolute; left: 66px; bottom: 50px; display: flex; align-items: center; gap: 16px; text-shadow: 0 2px 8px #07060f; }
  .brand img { width: 56px; height: 56px; border-radius: 13px; box-shadow: 0 0 0 3px #07060f; }
  .brand b { font-size: 34px; font-weight: 800; letter-spacing: -.01em; }
  .brand span { font: 400 14px/1 "Press Start 2P", monospace; color: #2de2e6; margin-left: 4px; }
</style>
</head>
<body data-root="/">
<canvas id="c" width="1200" height="630"></canvas>
<div class="scan"></div>
<div class="frame"></div>
<div class="text">
  <p class="top">SELECT YOUR CAT</p>
  {{TIER}}
  <h1 class="name">{{NAME}}</h1>
  <p class="ko">{{KO}}</p>
</div>
{{QUOTE}}
<div class="brand"><img src="/assets/icon.png" alt=""><b>Woodada</b><span>CAT GACHA</span></div>
<script src="/cats/cats.js"></script>
<script>
  // 무대와 고양이를 그린 뒤 제목을 'ready'로 바꾼다(찍는 쪽은 시간 예산 안에서 기다린다)
  Promise.all([window.WoodadaCats.masks, document.fonts.ready]).then(([masks]) => {
    const ctx = document.getElementById('c').getContext('2d');
    const floorH = 170, base = 630 - floorH;
    window.WoodadaCats.drawArcade(ctx, 1200, 630, floorH, 1.3, 0.35);
    {{DRAW}}
    document.title = 'ready';
  });
</script>
</body>
</html>
"""


def page_for(key: str) -> str:
    """key: 고양이 id 또는 'all'(도감)."""
    c1, c2, c3, c4 = NAME_COLORS[key]
    if key == "all":
        name, ko, tier, quote, size = "All Cats", "고양이 도감", "", "", 150
        # 세 마리를 수평선 위에 나란히. 유령은 떠 있고, 플래시 꼬리는 왼쪽으로 뻗는다.
        draw = "\n    ".join([
            "window.WoodadaCats.drawPoster(ctx, 'ghost', masks, { x: 400, base, catH: 128 });",
            "window.WoodadaCats.drawPoster(ctx, 'flash', masks, { x: 690, base, catH: 128 });",
            "window.WoodadaCats.drawPoster(ctx, 'glitch', masks, { x: 925, base, catH: 128 });",
        ])
    else:
        c = next(c for c in CATS if c["id"] == key)
        name, ko = c["name"]
        en_tier, _ = TIERS[c["stars"]]
        tier = f'<span class="tier">{en_tier} {"★" * c["stars"]}</span>'
        quote = f'<p class="quote">{html.escape(c["quote"][0])}</p>'
        size = 150 if len(name) <= 9 else 138
        # 글리치는 노이즈 조각이 몸 오른쪽으로 튀어나가서 조금 왼쪽에 둔다
        x = 820 if key == "glitch" else 860
        draw = f"window.WoodadaCats.drawPoster(ctx, '{key}', masks, {{ x: {x}, base, catH: 196 }});"
    out = PAGE
    for k, v in {"{{NAME_SIZE}}": str(size), "{{C1}}": c1, "{{C2}}": c2, "{{C3}}": c3, "{{C4}}": c4,
                 "{{TIER}}": tier, "{{NAME}}": html.escape(name), "{{KO}}": html.escape(ko),
                 "{{QUOTE}}": quote, "{{DRAW}}": draw}.items():
        out = out.replace(k, v)
    return out


class Handler(SimpleHTTPRequestHandler):
    """docs/를 그대로 내주고, /__og/<key> 에서만 미리보기 그림 페이지를 만들어 준다."""

    def do_GET(self) -> None:  # noqa: N802 (http.server 이름 규칙)
        if self.path.startswith("/__og/"):
            body = page_for(self.path[len("/__og/"):]).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        super().do_GET()

    def log_message(self, *args) -> None:
        pass


def find_browser() -> str:
    for b in BROWSERS:
        if b and Path(b).exists():
            return b
    sys.exit("Chrome이나 Edge를 찾지 못했어요. WOODADA_BROWSER에 실행 파일 경로를 넣어 주세요.")


def shoot(browser: str, url: str, dest: Path) -> None:
    # 사용자의 브라우저 프로필을 건드리지 않게 빈 임시 프로필로 띄운다
    with tempfile.TemporaryDirectory() as profile:
        shot = Path(profile) / "shot.png"
        subprocess.run([
            browser, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--mute-audio",
            "--no-first-run", "--no-default-browser-check", f"--user-data-dir={profile}",
            f"--window-size={W},{H}", "--force-device-scale-factor=1",
            "--virtual-time-budget=15000", f"--screenshot={shot}", url,
        ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120)
        im = Image.open(shot).convert("RGB")
        if im.size != (W, H):
            im = im.crop((0, 0, W, H))
        buf = io.BytesIO()
        im.save(buf, "PNG", optimize=True)
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(buf.getvalue())
    print("wrote", dest.relative_to(ROOT).as_posix(), f"({len(buf.getvalue()) // 1024} KB)")


def main() -> None:
    browser = find_browser()
    server = ThreadingHTTPServer(("127.0.0.1", 0), functools.partial(Handler, directory=str(DOCS)))
    threading.Thread(target=server.serve_forever, daemon=True).start()
    base = f"http://127.0.0.1:{server.server_address[1]}"
    try:
        shoot(browser, f"{base}/__og/all", OUT / "cats.png")
        for c in CATS:
            shoot(browser, f"{base}/__og/{c['id']}", OUT / f"{c['id']}.png")
    finally:
        server.shutdown()


if __name__ == "__main__":
    main()
