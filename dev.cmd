@echo off
rem Woodada 개발용 실행: 지금 소스를 Debug로 빌드해 설치된 Woodada와 따로 띄운다.
rem 개발용 앱은 설정·기록을 %APPDATA%\Woodada-dev에 따로 저장하고, 자동 업데이트를 하지 않는다.
chcp 65001 >nul
setlocal
cd /d "%~dp0"
set "DOTNET_NOLOGO=1"
set "LOCAL_SDK=%LOCALAPPDATA%\Woodada-dev\dotnet"

rem 1) PC에 설치된 .NET 9 SDK  2) 이 스크립트가 받아 둔 SDK
set "DOTNET="
dotnet --list-sdks 2>nul | findstr /b /c:"9." >nul && set "DOTNET=dotnet"
if not defined DOTNET if exist "%LOCAL_SDK%\dotnet.exe" set "DOTNET=%LOCAL_SDK%\dotnet.exe"
if not defined DOTNET call :install_sdk || exit /b 1

rem 빌드가 exe를 덮어쓸 수 있도록 떠 있는 개발용 앱만 닫는다. 설치된 Woodada는 건드리지 않는다.
powershell -NoProfile -Command "Get-Process Woodada -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\src\Woodada\bin\Debug\*' } | Stop-Process -Force"

echo Woodada 개발용 빌드 중...
rem 빌드 서버를 남기지 않는다. 남으면 이 창의 출력을 물고 있어서, 출력을 받는 쪽(터미널 Run 버튼 등)이 끝나지 않는다.
"%DOTNET%" build src\Woodada -c Debug -v q -nodeReuse:false -p:UseSharedCompilation=false
if errorlevel 1 (
    echo.
    echo 빌드에 실패했습니다. 위 오류를 확인하세요.
    pause
    exit /b 1
)

rem start로 띄우면 앱이 이 창의 출력을 물려받아 같은 문제가 생기므로 셸 실행(Start-Process)으로 띄운다.
powershell -NoProfile -Command "Start-Process -FilePath 'src\Woodada\bin\Debug\net9.0\Woodada.exe' -ArgumentList '--settings'"
echo 개발용 Woodada를 띄웠습니다. 트레이 툴팁과 창 제목에 [DEV] / (개발용)이 붙어 있습니다.
exit /b 0

:install_sdk
echo .NET 9 SDK가 없습니다.
echo %LOCAL_SDK% 에 받아 두면 이 스크립트만 쓰고, PC에는 따로 설치하지 않습니다. (약 300MB)
echo.
rem choice는 UTF-8 한글 안내문을 글자마다 두 번씩 찍으므로, 안내는 set /p로 띄우고 Enter로 받는다.
set "ANSWER="
set /p "ANSWER=지금 받을까요? Y를 누르고 Enter: "
if /i not "%ANSWER%"=="Y" goto :install_declined

echo.
echo 좋아요, SDK 다운로드를 시작합니다...
echo [1/3] 설치 스크립트를 받는 중...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile (Join-Path $env:TEMP 'dotnet-install.ps1')"
if errorlevel 1 goto :install_failed

echo [2/3] .NET 9 SDK를 받아 압축을 푸는 중... 인터넷 속도에 따라 몇 분 걸릴 수 있어요. 창을 닫지 마세요.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'Stop'; $s = Join-Path $env:TEMP 'dotnet-install.ps1'; & $s -Channel 9.0 -InstallDir $env:LOCAL_SDK -NoPath; Remove-Item $s"
if errorlevel 1 goto :install_failed

echo [3/3] 준비 완료: %LOCAL_SDK%
echo.
set "DOTNET=%LOCAL_SDK%\dotnet.exe"
exit /b 0

:install_declined
echo SDK를 받지 않고 끝냅니다.
pause
exit /b 1

:install_failed
echo.
echo SDK를 받지 못했습니다. 인터넷 연결을 확인하고 다시 실행하세요.
pause
exit /b 1
