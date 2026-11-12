#!/usr/bin/env bash
# ISO 를 QEMU 로 부팅 (KVM 가속). VMware 없이 빠르게 확인할 때.
#   os/build.sh qemu            데스크톱 (1280x800)
#   os/build.sh qemu phone      폰 모드 (커널 인자 alliedid.mode=phone 메뉴 선택)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
. "$ROOT/os/config/os.env"
ISO="$ROOT/out/AlliedID-$AID_VERSION-desktop-amd64.iso"
[[ -f "$ISO" ]] || { echo "먼저 os/build.sh iso" >&2; exit 1; }
ACCEL=(); [[ -w /dev/kvm ]] && ACCEL=(-enable-kvm -cpu host)
exec qemu-system-x86_64 "${ACCEL[@]}" -m 4096 -smp 2 \
  -cdrom "$ISO" -boot d \
  -device virtio-vga -display "${QEMU_DISPLAY:-gtk}" \
  -nic user,model=virtio-net-pci \
  -usb -device usb-tablet
