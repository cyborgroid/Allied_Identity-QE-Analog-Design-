# 안드로이드 연동

AlliedID 는 안드로이드를 두 방향으로 다룹니다.

## 1. AlliedID 안에서 안드로이드 앱 실행 — Waydroid

```bash
sudo aid-admin /usr/share/alliedid/android/waydroid-setup.sh   # system.admin 권한 필요
alliedid-android                                                # 또는 셸의 🤖 안드로이드
```

- Waydroid 는 LineageOS 를 컨테이너로 실행합니다(에뮬레이션이 아니라 같은 커널을 공유).
- **amd64 (VMware)**: `linux-modules-extra-$(uname -r)` 에 `binder_linux` 가 있습니다.
- **arm64 (Pi 5)**: Ubuntu `linux-raspi` 커널에 binder 가 빠져 있으면 binder 를 켠 커널이 필요합니다.
  이 부분은 하드웨어에서 확인이 필요한 항목입니다.
- Waydroid 는 Wayland 가 필요합니다. 셸 세션은 X11 이므로 `alliedid-android` 가 Weston 을 창으로 띄워 그 안에서 보여 줍니다.

## 2. 안드로이드(AVD)에서 AlliedID 셸 실행 — 로드맵

셸 UI 는 Avalonia 로 작성되어 있고, Avalonia 는 Android 대상도 지원합니다.
`AlliedID.Shell.Core`(DeskScript 파서, 번호 검증, 레이아웃 모델)는 플랫폼 독립이라 그대로 재사용됩니다.

```bash
dotnet workload install android            # Android SDK/JDK 필요 (Android Studio 설치 시 함께 설치됨)
dotnet new avalonia.app -o shell/src/AlliedID.Shell.Android   # 또는 Avalonia.Android 패키지로 직접 구성
# Core 와 Views/Widgets 를 참조 → AVD(Android Studio 의 Pixel 에뮬레이터)에서 실행
dotnet build -t:Run -f net10.0-android
```

플랫폼에 묶인 부분(`/proc`, `dpkg-query`, `nmcli`, GPIO, `aid` CLI 호출)은 Android 에서 쓸 수 없으므로
`AidClient`·`SystemInfo`·`CircuitRunner` 를 인터페이스 뒤로 빼고 Android 구현을 따로 두어야 합니다.
이 작업은 아직 하지 않았습니다.
