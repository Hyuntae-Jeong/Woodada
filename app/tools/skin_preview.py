"""스페셜 스킨 미리보기를 브라우저로 연다. 슬라이더로 값을 바꿔 보고, 마음에 들면 앱 상수에 옮겨 적는다.

    python tools/skin_preview.py ghost

tools/skin_preview/<스킨>.html 템플릿에 고양이 프레임 PNG를 data URI로 넣어 임시 폴더에 쓰고 연다.
PNG를 파일 경로로 불러오면 브라우저가 file:// 그림의 픽셀 읽기를 막아서, 눈 구멍을 찾을 수 없다.
"""
import base64, json, os, sys, tempfile, webbrowser

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FRAMES = os.path.join(ROOT, "src", "Woodada", "Assets", "RunCat")
TEMPLATES = os.path.join(ROOT, "tools", "skin_preview")


def main():
    skins = sorted(f[:-5] for f in os.listdir(TEMPLATES) if f.endswith(".html"))
    skin = sys.argv[1] if len(sys.argv) > 1 else None
    if skin not in skins:
        print(f"사용법: python tools/skin_preview.py <{'|'.join(skins)}>")
        return 1

    frames = []
    for i in range(5):
        with open(os.path.join(FRAMES, f"cat_running_{i}.png"), "rb") as f:
            frames.append("data:image/png;base64," + base64.b64encode(f.read()).decode())

    with open(os.path.join(TEMPLATES, f"{skin}.html"), encoding="utf-8") as f:
        html = f.read().replace("__FRAMES__", json.dumps(frames))
    out = os.path.join(tempfile.gettempdir(), f"woodada_{skin}_preview.html")
    with open(out, "w", encoding="utf-8") as f:
        f.write(html)
    print(out)
    webbrowser.open("file:///" + out.replace("\\", "/"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
