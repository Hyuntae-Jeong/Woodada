<div align="center">

<img src="docs/icon.png" alt="Woodada 아이콘" width="128" height="128">

<h1><b>Woodada</b></h1>

**작업표시줄 위를 우다다 달리는 CPU·메모리 고양이**

컴퓨터가 바빠질수록 고양이도 더 빨리 달립니다.

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)
![WinForms](https://img.shields.io/badge/UI-WinForms-68217A)

<img src="docs/demo.gif" alt="작업표시줄 위를 달리는 Woodada 고양이" width="100%">

</div>

<br>

## ✨ 특징

- 🐈 **CPU 고양이 · 메모리 고양이** — 사용률이 높을수록 빨리 달리고, 화면 끝에 닿으면 돌아서 왕복합니다.
- 🎨 **고양이 꾸미기** — 고양이마다 색(팔레트 10가지 + 직접 고르기)과 크기(16~72px)를 따로 정할 수 있고, 미리보기로 바로 확인합니다.
- 🖥️ **멀티 모니터** — 달릴 모니터를 고를 수 있고, 작업표시줄이 아래·위·왼쪽·오른쪽 어디에 있어도 그 가장자리를 따라 달립니다.
- 🎮 **전체 화면에서는 숨기** — 게임이나 발표 중에는 알아서 사라집니다.
- 🪶 **가볍게 상주** — 트레이 아이콘으로만 떠 있고, 클릭은 고양이를 통과해 아래 창으로 그대로 전달됩니다.

## 🚀 시작하기

### 필요한 것

- Windows 10 / 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (실행만 할 때는 .NET 9 Desktop Runtime)

### 바로 실행

```bash
dotnet run --project src/Woodada
```

설정 창을 바로 열고 싶으면 `--settings`를 붙입니다.

```bash
dotnet run --project src/Woodada -- --settings
```

### exe 하나로 만들기

```bash
dotnet publish src/Woodada -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

`src/Woodada/bin/Release/net9.0-windows/win-x64/publish/Woodada.exe`가 만들어집니다.

## 🕹️ 사용법

| 동작 | 결과 |
|---|---|
| 트레이 아이콘 **왼쪽 클릭** | 설정 창 열기 |
| 트레이 아이콘 **오른쪽 클릭** | 설정 · 고양이별 표시 · 일시정지 · Windows 시작 시 실행 · 종료 |

**설정 창**

- **일반** — 표시할 고양이(CPU / 메모리), 달릴 모니터, 전체 화면 앱 실행 중 숨기기
- **고양이별 모양** — 색, 크기, 실시간 미리보기

설정은 바꾸는 즉시 반영되고 `%APPDATA%\Woodada\settings.json`에 저장됩니다.

## 🧩 구조

| 위치 | 역할 |
|---|---|
| `Core/MetricSampler` | `GetSystemTimes`·`GlobalMemoryStatusEx`로 CPU/메모리 % 수집 (0.5초 주기) |
| `Core/RunnerState.Advance` | 사용률 → 속도 환산, 왕복(벽 반사), 이동 거리 기반 보행 위상 |
| `Core/Track` | 작업표시줄 위치(아래/위/좌/우)별 달리기 띠, 속도 프로파일 |
| `Interop/TaskbarGeometry` | 모니터별 작업표시줄(`Shell_TrayWnd` / `Shell_SecondaryTrayWnd`) 위치·가장자리 조회, `MonitorCatalog` |
| `Rendering/OverlayController` | 30fps 프레임 루프, 전체 화면 앱 감지 시 숨김 |
| `Rendering/OverlayWindow`, `LayeredSurface` | 클릭 통과 레이어드 창 + premultiplied DIB |
| `Sprites/RunCatSprite` | RunCat 고양이 5프레임을 알파 마스크로 써서 원하는 색으로 칠함, 눈 구멍은 흰색으로 채움 |
| `UI/SettingsForm` | 러너별 표시 여부·색·크기, 실시간 미리보기 |

### 새 러너 추가하기

1. `Sprites/IRunnerSprite`를 구현합니다 (`Measure`, `Draw`).
2. `Sprites/SpriteRegistry`에 등록합니다.
3. `Settings/AppSettings.CreateDefault`에 `RunnerConfig`를 추가합니다 (`SpriteId`로 연결).

## 📌 알아둘 점

- 보조 모니터에 작업표시줄이 없으면("모든 디스플레이에 작업 표시줄 표시" 꺼짐) 그 모니터 화면 아래를 따라 달립니다.
- 선택한 모니터를 분리하면 주 모니터로 대체됩니다.
- 두 번 실행해도 고양이가 겹치지 않도록 한 번에 하나만 실행됩니다.

## 🙏 크레딧

고양이 이미지(`src/Woodada/Assets/RunCat/cat_running_*.png`)는
[RunCat365](https://github.com/runcat-dev/RunCat365) (runcat-dev, Apache License 2.0)에서 가져왔습니다.
라이선스 전문: `src/Woodada/Assets/RunCat/LICENSE` (빌드 시 `ThirdParty/RunCat365-LICENSE.txt`로 복사)
