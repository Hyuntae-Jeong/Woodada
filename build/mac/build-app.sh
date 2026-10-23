#!/usr/bin/env bash
# macOS용 Woodada.app과 설치용 .dmg를 만든다. codesign·hdiutil을 쓰므로 macOS에서 실행해야 한다.
#
#   build/mac/build-app.sh [osx-arm64|osx-x64] [버전]
#
# 결과: publish/mac-<rid>/Woodada.app, publish/mac-<rid>/Woodada-mac-<arm64|x64>.dmg
# 릴리즈 CI(.github/workflows/release.yml)도 이 스크립트를 그대로 쓴다.
set -euo pipefail

RID="${1:-osx-arm64}"
VERSION="${2:-0.0.0}"
case "$RID" in
  osx-arm64) ARCH=arm64 ;;
  osx-x64) ARCH=x64 ;;
  *) echo "RID는 osx-arm64 또는 osx-x64여야 합니다: $RID" >&2; exit 1 ;;
esac

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
OUT="$ROOT/publish/mac-$RID"
APP="$OUT/Woodada.app"
DMG="$OUT/Woodada-mac-$ARCH.dmg"

rm -rf "$OUT"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

# .NET 런타임까지 담아(self-contained) 맥에 아무것도 설치돼 있지 않아도 실행되게 한다.
# .app 안에서는 파일 하나로 묶을 필요가 없어 실행 파일과 라이브러리를 Contents/MacOS에 그대로 둔다.
"$DOTNET" publish "$ROOT/src/Woodada" -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" -p:DebugType=none -o "$APP/Contents/MacOS"

cp "$ROOT/src/Woodada/Assets/Icon/Woodada.icns" "$APP/Contents/Resources/Woodada.icns"

# LSUIElement: 독과 앱 전환기(⌘Tab)에 나오지 않고 메뉴 막대 아이콘으로만 있는다.
# 번들 ID는 src/Woodada/AppInfo.cs의 BundleId(Release)와 같아야 한다.
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>Woodada</string>
    <key>CFBundleDisplayName</key>
    <string>Woodada</string>
    <key>CFBundleIdentifier</key>
    <string>io.github.hyuntae-jeong.woodada</string>
    <key>CFBundleExecutable</key>
    <string>Woodada</string>
    <key>CFBundleIconFile</key>
    <string>Woodada</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>LSUIElement</key>
    <true/>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSPrincipalClass</key>
    <string>NSApplication</string>
</dict>
</plist>
EOF

# Apple 개발자 서명이 없으므로 임시(ad-hoc) 서명만 한다. 애플 실리콘은 서명이 아예 없는 앱을 실행하지 않는다.
# 처음 열 때 "확인되지 않은 개발자" 경고가 뜨는 건 이 때문이다(README의 맥 설치 안내 참고).
codesign --force --deep --sign - "$APP"

# .dmg: 열면 Woodada.app과 응용 프로그램 폴더 바로가기가 보여서 끌어다 놓으면 설치된다.
STAGE="$OUT/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Woodada" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
rm -rf "$STAGE"

echo "만들었습니다: $APP"
echo "만들었습니다: $DMG"
