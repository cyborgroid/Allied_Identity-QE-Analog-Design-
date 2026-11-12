#!/usr/bin/env bash
# 빌더 컨테이너 안(root)에서 실행: Ubuntu 24.04 amd64 → AlliedID 라이브/설치 ISO (VMware Workstation 용)
set -euo pipefail
SRC=/src
. "$SRC/os/config/os.env"
. "$SRC/os/scripts/chroot-run.sh"
WORK=/build/amd64
CH=$WORK/chroot
ISO=$WORK/iso
OUT=$SRC/out
NAME="AlliedID-$AID_VERSION-desktop-amd64.iso"
log() { printf '\e[1;35m[iso]\e[0m %s\n' "$*"; }
trap 'chroot_umount "$CH"' EXIT

[[ -x "$SRC/out/artifacts/amd64/aid" ]] || { echo "먼저 os/build.sh artifacts" >&2; exit 1; }

if [[ ! -f "$CH/.debootstrap-done" ]]; then
  log "debootstrap $UBUNTU_SUITE (amd64)"
  rm -rf "$CH"; mkdir -p "$CH"
  debootstrap --arch=amd64 --components=main,restricted,universe,multiverse \
    --include=ca-certificates,gnupg,systemd-sysv,locales,initramfs-tools \
    "$UBUNTU_SUITE" "$CH" "$UBUNTU_MIRROR_AMD64"
  touch "$CH/.debootstrap-done"
fi

log "AlliedID 커스터마이즈"
stage_sources "$CH" "$SRC"
chroot_mount "$CH"
chroot "$CH" /tmp/alliedid/scripts/customize-rootfs.sh amd64
chroot "$CH" dpkg-query -W --showformat='${Package} ${Version}\n' > "$WORK/filesystem.manifest"
chroot_umount "$CH"
rm -rf "$CH/tmp/alliedid"

log "ISO 트리"
rm -rf "$ISO"; mkdir -p "$ISO"/{casper,boot/grub,.disk}
cp "$(ls -1 "$CH"/boot/vmlinuz-* | sort -V | tail -1)" "$ISO/casper/vmlinuz"
cp "$(ls -1 "$CH"/boot/initrd.img-* | sort -V | tail -1)" "$ISO/casper/initrd"
cp "$WORK/filesystem.manifest" "$ISO/casper/filesystem.manifest"
echo "$AID_PRETTY \"$AID_CODENAME\" - Release amd64 ($(date -u +%Y%m%d))" > "$ISO/.disk/info"
touch "$ISO/.disk/base_installable"

log "squashfs (xz)"
rm -f "$ISO/casper/filesystem.squashfs"
mksquashfs "$CH" "$ISO/casper/filesystem.squashfs" -comp xz -b 1M -noappend -wildcards -e '.debootstrap-done' 'proc/*' 'sys/*' 'dev/*' 'run/*'
du -sx --block-size=1 "$CH" | cut -f1 > "$ISO/casper/filesystem.size"

cat > "$ISO/boot/grub/grub.cfg" <<GRUB
set default=0
set timeout=5
insmod all_video
insmod gfxterm
terminal_output gfxterm
set menu_color_normal=white/black
set menu_color_highlight=black/light-green

menuentry "$AID_PRETTY — 라이브 (Desktop Phone)" {
  linux /casper/vmlinuz boot=casper quiet splash ---
  initrd /casper/initrd
}
menuentry "$AID_PRETTY — 라이브 (Phone 모드 강제)" {
  linux /casper/vmlinuz boot=casper quiet splash alliedid.mode=phone ---
  initrd /casper/initrd
}
menuentry "$AID_PRETTY — 안전 그래픽 (nomodeset)" {
  linux /casper/vmlinuz boot=casper nomodeset ---
  initrd /casper/initrd
}
menuentry "디스크에 설치 안내 (라이브에서 sudo alliedid-install /dev/sda)" {
  linux /casper/vmlinuz boot=casper systemd.unit=multi-user.target ---
  initrd /casper/initrd
}
GRUB

log "GRUB 하이브리드 ISO (BIOS + UEFI)"
mkdir -p "$OUT"
grub-mkrescue -o "$OUT/$NAME" "$ISO" -- -volid "ALLIEDID_${AID_VERSION//./}" 2>&1 | grep -v '^xorriso : UPDATE' || true
[[ -s "$OUT/$NAME" ]] || { echo "ISO 생성 실패" >&2; exit 1; }
( cd "$OUT" && sha256sum "$NAME" > "$NAME.sha256" )
chown -R "${HOST_UID:-0}:${HOST_GID:-0}" "$OUT" 2>/dev/null || true
log "완료 → out/$NAME ($(du -h "$OUT/$NAME" | cut -f1))"
