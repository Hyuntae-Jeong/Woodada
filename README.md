# Woodada (우다다)

작업표시줄 윗선 위를 고양이가 우다다 왕복으로 달립니다. CPU·메모리 사용률이 높을수록 빨리 달립니다.

> 예전 이름(TaskbarRunner, Udada)으로 쓰던 설정과 "Windows 시작 시 실행" 등록은 첫 실행 때 자동으로 옮겨집니다.

## 실행

```bash
dotnet run --project src/Woodada
```

- 트레이 아이콘 **왼쪽 클릭** → 설정 / **오른쪽 클릭** → 설정·고양이별 표시·일시정지·Windows 시작 시 실행·종료
- 설정 → **일반**: 표시할 고양이(CPU/메모리), 달릴 모니터(하나만 선택), 전체 화면 앱 중 숨기기
- 설정 → **고양이별 모양**: 색, 크기
- `Woodada.exe --settings` 로 실행하면 설정 창을 바로 띄웁니다.
- 설정 파일: `%APPDATA%\Woodada\settings.json`

## 단일 exe 배포

```bash
dotnet publish src/Woodada -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

## 구조

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

## 새 러너 추가

1. `Sprites/IRunnerSprite`를 구현 (`Measure`, `Draw`)
2. `Sprites/SpriteRegistry`에 등록
3. `Settings/AppSettings.CreateDefault`에 `RunnerConfig` 추가 (`SpriteId`로 연결)

## 제한

- 보조 모니터에 작업표시줄이 없으면("모든 디스플레이에 작업 표시줄 표시" 꺼짐) 그 모니터 화면 아래를 따라 달립니다.
- 선택한 모니터를 분리하면 주 모니터로 대체됩니다.

## 서드파티 에셋

고양이 이미지(`src/Woodada/Assets/RunCat/cat_running_*.png`)는
[RunCat365](https://github.com/runcat-dev/RunCat365) (runcat-dev, Apache License 2.0)에서 가져왔습니다.
라이선스 전문: `src/Woodada/Assets/RunCat/LICENSE` (빌드 시 `ThirdParty/RunCat365-LICENSE.txt`로 복사).
