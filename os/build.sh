#!/usr/bin/env bash
# AlliedID 26.11.1 LTS 이미지 빌드
#
#   os/build.sh artifacts   Rust/C#/Blockly 구성요소 빌드 (amd64 + arm64)
#   os/build.sh iso         VMware Workstation 용 ISO        → out/AlliedID-26.11.1-desktop-amd64.iso
#   os/build.sh vmware      ISO 를 붙인 VMware 가상머신(.vmx) → out/vmware/
#   os/build.sh rpi5        Raspberry Pi 5 이미지              → out/AlliedID-26.11.1-rpi5-arm64.img.xz
#   os/build.sh qemu        ISO 를 QEMU 로 부팅해 확인
#   os/build.sh all         artifacts + iso + vmware + rpi5
#
# iso/rpi5 는 Docker(--privileged) 빌더 컨테이너에서 root 로 실행된다 (호스트 sudo 불필요).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
IMAGE=alliedid-builder:26.11.1

builder() {
  docker build -q -t "$IMAGE" "$ROOT/os/docker" >/dev/null
  docker volume create alliedid-build >/dev/null
  docker run --rm --privileged -v /dev:/dev \
    -v "$ROOT:/src" -v alliedid-build:/build \
    -e HOST_UID="$(id -u)" -e HOST_GID="$(id -g)" \
    "$IMAGE" "$@"
}

case "${1:-help}" in
  artifacts) "$ROOT/os/scripts/build-artifacts.sh" ;;
  iso)       builder /src/os/scripts/build-iso.sh ;;
  rpi5)      builder /src/os/scripts/build-rpi5.sh ;;
  vmware)    "$ROOT/os/scripts/make-vmware.sh" ;;
  qemu)      "$ROOT/os/scripts/test-iso-qemu.sh" "${@:2}" ;;
  all)       "$0" artifacts && "$0" iso && "$0" vmware && "$0" rpi5 ;;
  clean)     docker volume rm alliedid-build 2>/dev/null || true ;;
  *)         sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//' ;;
esac
