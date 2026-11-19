#!/usr/bin/env python3
"""홈페이지 고양이 도감·프로필 페이지(docs/cats/)를 만든다.

    python app/tools/cat_pages.py

- docs/cats/index.html          도감 첫 화면 (고양이 고르기)
- docs/cats/<id>/index.html     고양이마다 한 장 (아케이드 고르기 화면)

글은 아래 CATS에 영어·한국어로 함께 적는다. 머리말·꼬리말·GA 같은 공통 부분은 이 파일의 틀 하나에서 나온다.
그림·움직임·능력치 계산은 docs/cats/cats.js, 모양은 docs/cats/cats.css에 있다.
새 스킨을 공개하면 CATS에 한 칸 더하고(앱 RewardCatalog의 이름·등급과 맞춘다), cats.js에 그 스킨 그리는 코드를 넣고,
LOCKED를 하나 줄인 뒤 이 스크립트를 다시 돌린다. 공유 미리보기 이미지는 app/tools/cat_og.py로 다시 만든다.
공개 전 스킨은 여기에 넣지 않는다.
"""
from __future__ import annotations

import html
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "docs" / "cats"
SITE = "https://hyuntae-jeong.github.io/Woodada/"

# 목록 끝에 붙이는 "곧 만나요!" 칸 수
LOCKED = 1


def t(en: str, ko: str) -> str:
    """영어·한국어를 함께 넣는다. 화면에는 고른 언어 하나만 보인다."""
    return f'<span class="en">{en}</span><span class="ko">{ko}</span>'


TIERS = {
    2: ("RARE", "레어"),
    3: ("EPIC", "에픽"),
    4: ("LEGENDARY", "전설"),
}

# 능력치 줄: (키, 영어 이름표, 한국어 이름표, 칸 수, 빨간 칸인지). 값은 cats.js가 사용률로 계산한다.
# duo: 두 마리가 서로 주고받는 스킨(털실냥의 패스처럼). 이 고양이만 1P·2P 단추가 생기고 처음엔 두 마리가 달린다.
CATS = [
    {
        "id": "ghost",
        "name": ("Ghost Cat", "유령냥"),
        "stars": 2,
        "quote": ("Boo… did I scare you?", "부우… 놀랐지?"),
        "intro": (
            "Floats along your taskbar with a see-through body. Wears ghostly white instead of your color.",
            "반투명한 몸으로 작업표시줄 위를 둥실둥실 떠다녀요. 고른 색 대신 유령색을 입어요.",
        ),
        "short": ("Floats along with a see-through, softly glowing body.", "반투명하게 빛나는 몸으로 둥실둥실 떠다녀요."),
        "hint": (
            "It always floats the same way. Only its speed follows your CPU.",
            "사용률과 상관없이 늘 둥실 떠 있어요. 빨라지기만 해요.",
        ),
        "ticks": [],
        "stats": [
            ("spd", "SPD", "속도", 10, False),
            ("float", "FLOAT", "둥실", 10, False),
            ("sheer", "SHEER", "투명", 10, False),
            ("glow", "GLOW", "빛", 10, False),
        ],
        "talent": ("Floating a little above the taskbar", "작업표시줄 위로 살짝 떠 있기"),
        "habit": ("Bobs up and down every 1.7 seconds", "1.7초마다 위아래로 흔들려요"),
        "likes": ("Late-night coding, dark mode", "새벽 코딩, 다크 모드"),
        "dislikes": ("Light mode, bright lights", "라이트 모드, 형광등"),
    },
    {
        "id": "flash",
        "name": ("Flash Cat", "플래시냥"),
        "stars": 3,
        "quote": ("Catch me if you can!", "따라올 수 있으면 따라와 봐!"),
        "intro": (
            "Leaves afterimages and sparks behind. The faster it runs, the longer the trail.",
            "지나온 길에 잔상과 불꽃을 남겨요. 빠를수록 꼬리가 길어져요.",
        ),
        "short": ("Leaves a fiery trail. The faster it runs, the longer it gets.", "불꽃 꼬리를 남겨요. 빠를수록 길어져요."),
        "hint": (
            "Sparks peak at 40%. Above 50% it starts to glow red, and at 60% it's bright red.",
            "40%에서 불꽃이 가장 세지고, 50%를 넘으면 달아오르기 시작해 60%면 새빨개져요.",
        ),
        "ticks": [40, 50, 60],
        "stats": [
            ("spd", "SPD", "속도", 10, False),
            ("tail", "TAIL", "꼬리", 10, False),
            ("fire", "FIRE", "불꽃", 10, False),
            ("heat", "HEAT", "열기", 5, True),
        ],
        "talent": ("Leaving eight afterimages", "잔상 여덟 겹 남기기"),
        "habit": ("Turns red above 50% CPU", "50%를 넘으면 달아올라요"),
        "likes": ("Loading screens, 100% CPU", "게임 로딩, CPU 100%"),
        "dislikes": ("Sleep mode", "절전 모드"),
    },
    {
        "id": "yarn",
        "name": ("Yarn Cat", "털실냥"),
        "stars": 3,
        "duo": True,
        "quote": ("Pass it here! …wait, that one's mine.", "패스! …아니, 그건 내 털실이야."),
        "intro": (
            "Kicks a ball of yarn along as it runs. The faster it goes, the harder the kick and the farther the yarn bounces. "
            "Two cats wearing it pass one ball between them.",
            "털실 뭉치를 툭툭 차면서 달려요. 빨리 달릴수록 세게 차서 털실이 통통 튀며 멀리 굴러가요. "
            "두 마리가 함께 입으면 털실 하나로 패스해요.",
        ),
        "short": ("Kicks a ball of yarn as it runs. Two of them pass it back and forth.", "털실을 툭툭 차며 달려요. 두 마리면 서로 패스해요."),
        "hint": (
            "The busier your CPU, the harder it kicks: a gentle nudge when idle, a bouncing ball about 3 bodies ahead when busy. "
            "The gray cat (2P) is the memory cat wearing it too. Press 1P to see just one.",
            "CPU가 바쁠수록 세게 차요. 한가하면 코앞에 살포시, 바쁘면 털실이 통통 튀며 몸 3개쯤 앞까지 굴러가요. "
            "회색 고양이(2P)는 같이 입은 메모리 고양이예요. 1P를 누르면 한 마리만 달려요.",
        ),
        "ticks": [],
        "stats": [
            ("spd", "SPD", "속도", 10, False),
            ("kick", "KICK", "차기", 10, False),
            ("roll", "ROLL", "굴리기", 10, False),
            ("hop", "HOP", "튐", 10, True),
        ],
        "talent": ("Dribbling yarn along the taskbar", "작업표시줄 위 털실 드리블"),
        "habit": ("Flips the yarn over its head when it turns around", "돌아설 때 뒷발로 털실을 머리 위로 넘겨요"),
        "likes": ("A fresh ball of yarn, a teammate to pass to", "새 털실 뭉치, 패스 받아 줄 친구"),
        "dislikes": ("Tangled thread, the vacuum cleaner", "엉킨 실, 청소기"),
    },
    {
        "id": "daku",
        "name": ("Sticker Cat", "다꾸냥"),
        "stars": 3,
        "quote": ("Empty spot? Let me fix that.", "여기 비었네? 하나 붙여 줄게!"),
        "intro": (
            "Runs as a sticker with a white outline and decorates its trail with stickers. The busier your computer, the more it sticks.",
            "흰 테두리를 두른 스티커 모습으로 달리며 지나간 자리에 스티커를 붙여요. 사용률이 높을수록 자주 붙여요.",
        ),
        "short": ("Decorates its trail with stickers.", "지나간 자리에 스티커를 붙여요."),
        "hint": (
            "The busier your CPU, the more often it sticks: one every 12 seconds when idle, every 3 seconds when busy. "
            "After about a minute, each sticker peels off from a corner.",
            "CPU가 바쁠수록 자주 붙여요. 한가하면 12초에 한 장, 바쁘면 3초에 한 장. "
            "붙인 스티커는 1분쯤 지나면 귀퉁이부터 들려 떨어져요.",
        ),
        "ticks": [],
        "stats": [
            ("spd", "SPD", "속도", 10, False),
            ("stick", "STICK", "붙이기", 10, False),
            ("deco", "DECO", "꾸밈", 10, False),
            ("size", "SIZE", "크기", 10, True),
        ],
        "talent": ("Turning your taskbar into a diary", "작업표시줄을 다이어리처럼 꾸미기"),
        "habit": ("Keeps its stickers three bodies apart", "스티커끼리 몸 3개만큼 띄워 붙여요"),
        "likes": ("Glitter stickers, a blank diary page", "반짝이 스티커, 빈 다이어리"),
        "dislikes": ("Stickers that won't stick, crumpled paper", "안 붙는 스티커, 구겨진 종이"),
    },
    {
        "id": "glitch",
        "name": ("Glitch Cat", "글리치냥"),
        "stars": 4,
        "quote": ("Bzzt… it's a feature, not a bug.", "지지직… 버그 아니고 기능이야."),
        "intro": (
            "Now and then it flickers like a broken screen. More often when your CPU is busy.",
            "가끔 화면이 고장 난 것처럼 지지직거려요. CPU가 바쁠수록 자주요.",
        ),
        "short": ("Every now and then it flickers like a broken screen.", "가끔 화면이 고장 난 것처럼 지지직거려요."),
        "hint": (
            "The busier your CPU, the more often and harder it glitches: from 0.4 to 3 times a second.",
            "CPU가 바쁠수록 더 자주, 더 세게 지지직거려요. 1초에 0.4번에서 3번까지요.",
        ),
        "ticks": [],
        "stats": [
            ("spd", "SPD", "속도", 10, False),
            ("rate", "RATE", "빈도", 10, False),
            ("power", "POWER", "세기", 10, True),
            ("noise", "NOISE", "조각", 4, True),
        ],
        "talent": ("Pretending to be a broken screen", "고장 난 화면인 척하기"),
        "habit": ("Glitches up to 3 times a second when busy", "바쁘면 1초에 3번까지 지지직"),
        "likes": ("Old CRT screens, bug reports", "옛날 브라운관, 버그 제보"),
        "dislikes": ("Turning it off and on again", "껐다 켜기"),
    },
]

GA = """<!-- Google Analytics. 홈페이지(docs/index.html)와 같다. 유럽 방문자는 두 겹으로 뺀다.
     1) 브라우저 시간대가 유럽이면 GA를 아예 불러오지 않는다.
     2) 그래도 들어온 유럽(EEA·영국·스위스) 방문자는 GA 지역 설정으로 쿠키를 남기지 않는다.
     로컬 미리보기에서는 실제 주소가 아니라서 켜지지 않는다. -->
<script>
  (function () {
    if (location.hostname !== 'hyuntae-jeong.github.io') return;
    var tz = '';
    try { tz = Intl.DateTimeFormat().resolvedOptions().timeZone || ''; } catch (e) {}
    if (/^(Europe\\/|Atlantic\\/(Reykjavik|Canary|Madeira|Azores|Faroe)$|Asia\\/(Nicosia|Famagusta)$|Arctic\\/)/.test(tz)) return;

    var id = 'G-K1RDPDRTLX';
    window.dataLayer = window.dataLayer || [];
    window.gtag = function () { window.dataLayer.push(arguments); };
    gtag('consent', 'default', { analytics_storage: 'granted', ad_storage: 'denied', ad_user_data: 'denied', ad_personalization: 'denied' });
    gtag('consent', 'default', {
      analytics_storage: 'denied',
      region: ['AT','BE','BG','HR','CY','CZ','DK','EE','FI','FR','DE','GR','HU','IE','IT','LV','LT','LU','MT','NL','PL','PT','RO','SK','SI','ES','SE','IS','LI','NO','GB','CH']
    });
    gtag('js', new Date());
    gtag('config', id);

    var s = document.createElement('script');
    s.async = true;
    s.src = 'https://www.googletagmanager.com/gtag/js?id=' + id;
    document.head.appendChild(s);
  })();
</script>"""


def head(title_en: str, title_ko: str, desc: str, path: str, root: str, og: str, og_alt: str) -> str:
    """<head>. root는 docs/ 맨 위까지의 상대 경로(예: "../../").
    og는 공유 미리보기 이미지 이름(docs/assets/og/<og>.png, app/tools/cat_og.py가 만든다)."""
    url = SITE + path
    og_url = f"{SITE}assets/og/{og}.png"
    e = html.escape
    return f"""<!doctype html>
<html lang="en" data-lang="en" data-title-en="{e(title_en)}" data-title-ko="{e(title_ko)}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>{e(title_en)}</title>
<meta name="description" content="{e(desc)}">
<meta name="theme-color" content="#07060f">
<link rel="canonical" href="{url}">
<link rel="icon" href="{root}assets/icon.png">
<link rel="apple-touch-icon" href="{root}assets/icon.png">
<meta property="og:type" content="website">
<meta property="og:title" content="{e(title_en)}">
<meta property="og:description" content="{e(desc)}">
<meta property="og:url" content="{url}">
<meta property="og:image" content="{og_url}">
<meta property="og:image:width" content="1200">
<meta property="og:image:height" content="630">
<meta property="og:image:alt" content="{e(og_alt)}">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:image" content="{og_url}">
<!-- 첫 화면이 그려지기 전에 언어를 정한다. 주소의 ?lang=(앱 도감에서 열 때 앱 언어를 붙인다)을 먼저 따르고,
     없으면 홈페이지와 같은 저장 값, 그것도 없으면 브라우저 언어. -->
<script>
  (function () {{
    var m = /[?&]lang=(en|ko)(?:&|$)/.exec(location.search), l = m && m[1];
    if (!l) try {{ l = localStorage.getItem('woodada-lang'); }} catch (e) {{}}
    if (l !== 'en' && l !== 'ko') l = /^ko\\b/i.test(navigator.language || '') ? 'ko' : 'en';
    var r = document.documentElement;
    r.lang = l;
    r.setAttribute('data-lang', l);
    document.title = r.getAttribute('data-title-' + l);
  }})();
</script>
{GA}
<link rel="preconnect" href="https://cdn.jsdelivr.net" crossorigin>
<link rel="stylesheet" href="https://cdn.jsdelivr.net/gh/orioncactus/pretendard@v1.3.9/dist/web/variable/pretendardvariable-dynamic-subset.min.css">
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@fontsource/black-han-sans@5.2.8/index.css">
<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@fontsource/press-start-2p@5.2.7/index.css">
<link rel="stylesheet" href="{root}cats/cats.css">
</head>"""


def top(root: str, show_crumb: bool) -> str:
    crumb = f'<a class="crumb" href="{root}cats/">{t("← All cats", "← 고양이 도감")}</a>' if show_crumb else ""
    return f"""<header class="top gutter">
  <div class="top-in">
    <a class="brand" href="{root}"><img src="{root}assets/icon.png" alt="" width="28" height="28"><span>Woodada</span></a>
    {crumb}
    <div class="lang" role="group" aria-label="Language">
      <button type="button" data-set-lang="en">EN</button>
      <button type="button" data-set-lang="ko">한국어</button>
    </div>
  </div>
</header>"""


def foot(root: str) -> str:
    return f"""<footer class="foot gutter">
  <div class="wrap">
    <p>{t(f'<a href="{root}">← Back to Woodada</a>', f'<a href="{root}">← Woodada로 돌아가기</a>')}</p>
    <p>{t('Cat sprites from <a href="https://github.com/runcat-dev/RunCat365">RunCat365</a> (Apache License 2.0).',
          '고양이 그림은 <a href="https://github.com/runcat-dev/RunCat365">RunCat365</a>(Apache License 2.0)에서 가져왔어요.')}</p>
    <p>{t('Special skin designs © 2026 Hyuntae Jeong, not covered by the MIT License (<a href="https://github.com/Hyuntae-Jeong/Woodada/blob/main/app/src/Woodada/Sprites/Skins/LICENSE">terms</a>).',
          '스페셜 스킨 디자인은 © 2026 Hyuntae Jeong이며 MIT License 적용 대상이 아니에요(<a href="https://github.com/Hyuntae-Jeong/Woodada/blob/main/app/src/Woodada/Sprites/Skins/LICENSE">조건 보기</a>).')}</p>
    <p><a href="{root}privacy.html">{t("Privacy", "개인정보처리방침")}</a></p>
  </div>
</footer>"""


def tier_badge(stars: int) -> str:
    en, ko = TIERS[stars]
    s = "★" * stars
    return f'<span class="tier">{t(f"{en} {s}", f"{ko} {s}")}</span>'


def roster(current: str | None, root: str) -> str:
    items = []
    for c in CATS:
        en, ko = c["name"]
        cur = ' aria-current="page"' if c["id"] == current else ""
        items.append(
            f'<li><a class="slot" href="{root}cats/{c["id"]}/"{cur}>'
            f'<canvas data-thumb="{c["id"]}" aria-hidden="true"></canvas><span>{t(en, ko)}</span></a></li>')
    for _ in range(LOCKED):
        items.append('<li><div class="slot locked"><canvas data-thumb="unknown" aria-hidden="true"></canvas><span>???</span></div></li>')
    return '<ul class="roster" aria-label="Cats">\n        ' + "\n        ".join(items) + "\n      </ul>"


def head_row(players: bool = False) -> str:
    """players: 두 마리가 주고받는 고양이(duo)의 프로필. 1P를 누르면 2P(메모리 고양이)가 나왔다 들어가고, 떠 있는 2P를 눌러도 들어간다. 처음엔 두 마리."""
    if players:
        badge = ('<div class="players">\n'
                 f'        <button type="button" class="p1" id="p1" aria-pressed="true">1P<span class="vh">{t(" — show 2P", " — 2P 같이 보기")}</span></button>\n'
                 f'        <button type="button" class="p2" id="p2" aria-pressed="true">2P<span class="vh">{t(" — hide 2P", " — 2P 빼기")}</span></button>\n'
                 '      </div>')
    else:
        badge = '<span class="p1">1P</span>'
    return f"""<div class="head">
      {badge}
      <p class="head-title">SELECT YOUR CAT</p>
      <span class="blink">{t("PRESS START", "고양이를 고르세요")}</span>
    </div>"""


def how_to_get(root: str) -> str:
    return f"""<div class="box">
        <h2 class="lbl">{t("HOW TO GET", "만나는 법")}</h2>
        <p>{t("Win it from the Cat Gacha with 500 fish. Cats you already have never come up again.",
              "냥뽑기에서 생선 500개로 뽑아요. 이미 만난 고양이는 다시 나오지 않아요.")}</p>
        <a class="coin" href="{root}#install">{t("INSERT FISH ▶", "Woodada 받기 ▶")}</a>
      </div>"""


def cat_page(c: dict) -> str:
    root = "../../"
    en, ko = c["name"]
    stats = []
    for key, lbl_en, lbl_ko, segs, hot in c["stats"]:
        cls = "seg hot" if hot else "seg"
        stats.append(
            f'<div class="stat" data-stat="{key}"><span class="lbl">{t(lbl_en, lbl_ko)}</span>'
            f'<div class="{cls}">{"<i></i>" * segs}</div><output></output></div>')
    ticks = "".join(f'<span style="left:{n}%">{n}</span>' for n in c["ticks"])
    duo = c.get("duo", False)
    players = ' data-players="2"' if duo else ""
    desc = f'{en} — {c["intro"][0]} A special skin you can win in Woodada, the cat that runs on your taskbar.'
    alt = f"{en} ({ko}) running on a neon arcade stage"
    return f"""{head(f"{en} — Woodada", f"{ko} — Woodada", desc, f"cats/{c['id']}/", root, c["id"], alt)}
<body data-cat="{c['id']}" data-root="{root}"{players}>

{top(root, True)}

<main class="gutter">
  <div class="wrap">
    {head_row(players=duo)}

    <div class="stagebox">
      <canvas class="js-stage" role="img" aria-label="{html.escape(en)} running"></canvas>
      <div class="plate">
        {tier_badge(c["stars"])}
        <h1 class="name">{t(en, ko)}</h1>
        <p class="sub">{t(ko, en.upper())}</p>
      </div>
      <p class="quote">{t(c["quote"][0], c["quote"][1])}</p>
    </div>
    <p class="intro">{t(*c["intro"])}</p>

    <div class="ctrl">
      <label for="cpu">CPU</label>
      <div class="range-box"><input type="range" id="cpu" min="0" max="100" value="70"><div class="ticks" aria-hidden="true">{ticks}</div></div>
      <output id="cpu-out" for="cpu">70%</output>
      <button type="button" class="auto" id="auto" aria-pressed="true">AUTO ON</button>
    </div>
    <p class="ctrl-hint">{t(*c["hint"])}</p>

    <div class="bottom">
      {roster(c["id"], root)}
      <div class="stats" aria-label="Stats">
        {chr(10).join("        " + s for s in stats).strip()}
      </div>
    </div>

    <div class="info">
      <div class="box">
        <h2 class="lbl">{t("SKILL", "재주")}</h2>
        <p><b>{t("Talent", "특기")}</b>{t(*c["talent"])}</p>
        <p><b>{t("Habit", "버릇")}</b>{t(*c["habit"])}</p>
      </div>
      <div class="box">
        <h2 class="lbl">{t("LIKE / DISLIKE", "좋아함 / 싫어함")}</h2>
        <p><b>{t("Likes", "좋아함")}</b>{t(*c["likes"])}</p>
        <p><b>{t("Dislikes", "싫어함")}</b>{t(*c["dislikes"])}</p>
      </div>
      {how_to_get(root)}
    </div>
  </div>
</main>

{foot(root)}

<script src="{root}cats/cats.js"></script>
</body>
</html>
"""


def index_page() -> str:
    root = "../"
    cards = []
    for c in CATS:
        en, ko = c["name"]
        cards.append(
            f'<li><a class="card" href="{c["id"]}/"><canvas data-thumb="{c["id"]}" aria-hidden="true"></canvas>'
            f'<div>{tier_badge(c["stars"])}<b>{t(en, ko)}</b><small>{t(*c["short"])}</small></div></a></li>')
    for _ in range(LOCKED):
        cards.append(
            f'<li><div class="card locked"><canvas data-thumb="unknown" aria-hidden="true"></canvas>'
            f'<div><b>???</b><small>{t("Coming soon!", "곧 만나요!")}</small></div></div></li>')
    desc = "Meet the special cats you can win in Woodada's Cat Gacha."
    alt = "Ghost Cat, Flash Cat, Yarn Cat, Sticker Cat and Glitch Cat running on a neon arcade stage"
    return f"""{head("Cats — Woodada", "고양이 도감 — Woodada", desc, "cats/", root, "cats", alt)}
<body data-root="{root}">

{top(root, False)}

<main class="gutter">
  <div class="wrap">
    {head_row()}
    <h1 class="name">{t("All cats", "고양이 도감")}</h1>
    <p class="lead">{t("Special cats you can win with fish in the Cat Gacha. Pick one to take a closer look.",
                       "생선을 모아 냥뽑기로 만날 수 있는 고양이들이에요. 골라서 자세히 보세요.")}</p>
    <ul class="deck">
      {(chr(10) + "      ").join(cards)}
    </ul>
  </div>
</main>

{foot(root)}

<script src="{root}cats/cats.js"></script>
</body>
</html>
"""


def write(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")
    print("wrote", path.relative_to(ROOT).as_posix())


def main() -> None:
    write(OUT / "index.html", index_page())
    for c in CATS:
        write(OUT / c["id"] / "index.html", cat_page(c))


if __name__ == "__main__":
    main()
