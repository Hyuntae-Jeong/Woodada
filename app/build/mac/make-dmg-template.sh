#!/usr/bin/env bash
# 설치용 DMG의 틀(build/mac/dmg/template.dmg)을 만든다. 배경 그림·창 크기·아이콘 자리가 들어 있고 앱은 없다.
# 릴리즈 빌드(build-app.sh)는 이 틀에 Woodada.app만 넣어 압축하므로 CI에서는 Finder가 필요 없다.
#
#   build/mac/make-dmg-template.sh
#
# 배경 그림(tools/draw_dmg_background.py)이나 창 배치를 바꿨을 때만 맥에서 다시 돌리고, 결과를 커밋한다.
#
# 왜 Finder로 만드나: DMG 창 배경은 공식 API가 없는 Finder 기능이다. macOS 26(Tahoe)의 Finder는
# .DS_Store에 새 형식의 그림 참조(pBB0, pBBk)를 쓰고, 예전 방식(dmgbuild 등이 쓰는 alias만 있는 .DS_Store)의
# 배경은 보여 주지 않는다. 그래서 Finder에게 직접 배경을 지정하게 하고, 그 볼륨을 그대로 틀로 쓴다.
# Finder가 적은 그림 참조는 볼륨 안의 파일을 가리키므로, 볼륨을 키우고 앱을 넣고 압축해도 유지된다.
#
# 처음 실행하면 macOS가 이 터미널이 Finder를 제어해도 되는지 묻는다. 허용해야 한다.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DIR="$ROOT/build/mac/dmg"
OUT="$DIR/template.dmg"
VOLNAME="Woodada"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# 창 내용 660×420pt + Finder 제목 막대 32pt. 아이콘 자리는 tools/draw_dmg_background.py와 같아야 한다.
WIN_LEFT=200 WIN_TOP=120 WIN_W=660 WIN_H=452
APP_POS="170, 190" APPS_POS="490, 190"

if [ -e "/Volumes/$VOLNAME" ]; then
  echo "/Volumes/$VOLNAME 이 이미 열려 있습니다. Finder가 헷갈리지 않도록 꺼내고 다시 실행하세요." >&2
  exit 1
fi

# 레티나 화면용 2배 그림을 함께 담은 TIFF. Finder가 화면에 맞는 쪽을 고른다.
tiffutil -cathidpicheck "$DIR/background.png" "$DIR/background@2x.png" -out "$WORK/background.tiff" >/dev/null 2>&1

hdiutil create -size 20m -fs HFS+ -volname "$VOLNAME" -type UDIF "$WORK/rw.dmg" -quiet
MP="$(hdiutil attach -readwrite -noautoopen "$WORK/rw.dmg" | awk -F'\t' '/\/Volumes\//{print $NF}' | tail -1)"
mkdir "$MP/.background"
cp "$WORK/background.tiff" "$MP/.background/background.tiff"
ln -s /Applications "$MP/Applications"
# 앱 자리를 잡아 두는 빈 폴더. build-app.sh가 진짜 앱으로 바꿔 넣는다(자리는 이름으로 기억된다).
mkdir "$MP/Woodada.app"

# 그림 파일을 볼륨에 넣은 뒤에 배경을 지정해야 Finder가 볼륨 안 파일을 가리킨다.
# 설정은 창을 닫았다 다시 열어야 .DS_Store에 기록된다.
osascript - "$MP" <<EOF
on run argv
	set mp to item 1 of argv
	tell application "Finder"
		set theDisk to item (POSIX file mp as alias)
		tell theDisk
			open
			set current view of container window to icon view
			set toolbar visible of container window to false
			set statusbar visible of container window to false
			set bounds of container window to {$WIN_LEFT, $WIN_TOP, $((WIN_LEFT + WIN_W)), $((WIN_TOP + WIN_H))}
			set opts to icon view options of container window
			set arrangement of opts to not arranged
			set icon size of opts to 112
			set text size of opts to 13
			set label position of opts to bottom
			set shows icon preview of opts to false
			set background picture of opts to file ".background:background.tiff"
			set position of item "Woodada.app" of container window to {$APP_POS}
			set position of item "Applications" of container window to {$APPS_POS}
			close
			open
			update without registering applications
			delay 2
			close
		end tell
	end tell
end run
EOF

# Finder가 .DS_Store를 다 쓸 때까지 기다린다. 새 형식 그림 참조(pBB0)가 있어야 Tahoe에서 배경이 보인다.
for _ in $(seq 1 20); do
  if [ -f "$MP/.DS_Store" ] && grep -aq pBB0 "$MP/.DS_Store"; then break; fi
  sleep 0.5
done
if ! grep -aq pBB0 "$MP/.DS_Store" 2>/dev/null; then
  hdiutil detach "$MP" -quiet || true
  echo "Finder가 배경 설정을 기록하지 않았습니다(.DS_Store에 pBB0 없음)." >&2
  exit 1
fi
rm -rf "$MP/.fseventsd"

hdiutil detach "$MP" -quiet
rm -f "$OUT"
hdiutil convert "$WORK/rw.dmg" -format UDZO -o "$OUT" -quiet
echo "만들었습니다: $OUT"
