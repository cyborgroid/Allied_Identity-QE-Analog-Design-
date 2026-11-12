#!/usr/bin/env bash
# AlliedID 에서 안드로이드 앱 실행 (Waydroid: LineageOS 컨테이너)
#   sudo aid-admin /usr/share/alliedid/android/waydroid-setup.sh     # system.admin 권한 필요
# amd64(VMware) 와 arm64(Pi 5) 모두 지원. 이미지 다운로드에 수 GB 가 필요하다.
set -euo pipefail
[[ $EUID -eq 0 ]] || { echo "root 로 실행하세요 (sudo aid-admin …)" >&2; exit 1; }
. /etc/os-release
CODENAME="${UBUNTU_CODENAME:-noble}"

echo "▶ Waydroid 저장소"
install -d -m 0755 /etc/apt/keyrings
curl -fsSL https://repo.waydro.id/waydroid.gpg -o /etc/apt/keyrings/waydroid.gpg
echo "deb [signed-by=/etc/apt/keyrings/waydroid.gpg] https://repo.waydro.id/ $CODENAME main" > /etc/apt/sources.list.d/waydroid.list
apt-get update
DEBIAN_FRONTEND=noninteractive apt-get install -y waydroid weston

echo "▶ binder 커널 모듈"
if ! modprobe binder_linux devices=binder,hwbinder,vndbinder 2>/dev/null; then
  echo "  ! binder_linux 모듈이 없습니다. amd64: linux-modules-extra-\$(uname -r) 설치," >&2
  echo "    Pi 5: binder 가 켜진 커널이 필요합니다 (android/README.md 참고)." >&2
fi

echo "▶ LineageOS 이미지 초기화 (GAPPS 없음)"
waydroid init -s VANILLA
systemctl enable --now waydroid-container.service
install -m 0755 "$(dirname "$0")/alliedid-android" /usr/bin/alliedid-android
echo "✓ 완료. 셸의 🤖 안드로이드 앱 또는 'alliedid-android' 로 실행"
