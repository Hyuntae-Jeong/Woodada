#!/usr/bin/env bash
# Woodada 개발용 실행(macOS): 지금 소스를 Debug로 빌드해 설치된 Woodada와 따로 띄운다.
# 개발용 앱은 설정·기록을 ~/Library/Application Support/Woodada-dev에 따로 저장하고, 업데이트 확인을 하지 않는다.
# Windows에서는 dev.cmd를 쓴다.
set -euo pipefail
cd "$(dirname "$0")"
export DOTNET_NOLOGO=1
LOCAL_SDK="$HOME/Library/Application Support/Woodada-dev/dotnet"

# 1) 이 맥에 설치된 .NET 9 SDK  2) 이 스크립트가 받아 둔 SDK
if command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^9\.'; then
  DOTNET=dotnet
elif [ -x "$LOCAL_SDK/dotnet" ]; then
  DOTNET="$LOCAL_SDK/dotnet"
else
  echo ".NET 9 SDK가 없습니다."
  echo "$LOCAL_SDK 에 받아 두면 이 스크립트만 쓰고, 맥에는 따로 설치하지 않습니다. (약 300MB)"
  read -r -p "지금 받을까요? [y/N] " answer
  if [[ ! "$answer" =~ ^[Yy]$ ]]; then
    echo "SDK를 받지 않고 끝냅니다."
    exit 1
  fi
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 9.0 --install-dir "$LOCAL_SDK" --no-path
  rm -f /tmp/dotnet-install.sh
  DOTNET="$LOCAL_SDK/dotnet"
fi

# 빌드가 실행 파일을 덮어쓸 수 있도록 떠 있는 개발용 앱만 닫는다. 설치된 Woodada.app은 건드리지 않는다.
pkill -f 'src/Woodada/bin/Debug/net9.0/Woodada' 2>/dev/null || true

echo "Woodada 개발용 빌드 중..."
"$DOTNET" build src/Woodada -c Debug -v q -nodeReuse:false -p:UseSharedCompilation=false

nohup src/Woodada/bin/Debug/net9.0/Woodada --settings >/dev/null 2>&1 &
echo "개발용 Woodada를 띄웠습니다. 메뉴 막대 아이콘 툴팁과 창 제목에 [DEV] / (개발용)이 붙어 있습니다."
