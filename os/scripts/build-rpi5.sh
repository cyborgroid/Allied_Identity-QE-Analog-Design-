#!/usr/bin/env bash
# 빌더 컨테이너 안(root, --privileged, -v /dev:/dev)에서 실행:
# Ubuntu 24.04 Raspberry Pi 서버 이미지 → AlliedID Pi 5 이미지 (.img.xz, SD/NVMe 에 굽는다)
set -euo pipefail
SRC=/src
. "$SRC/os/config/os.env"
. "$SRC/os/scripts/chroot-run.sh"
OUT=$SRC/out
CACHE=$OUT/cache
WORK=/build/arm64
MNT=$WORK/mnt
IMG=$WORK/alliedid-rpi5.img
NAME="AlliedID-$AID_VERSION-rpi5-arm64.img"
log() { printf '\e[1;33m[rpi5]\e[0m %s\n' "$*"; }
LOOP=""
cleanup() {
  chroot_umount "$MNT" 2>/dev/null || true
  umount "$MNT/boot/firmware" 2>/dev/null || true
  umount "$MNT" 2>/dev/null || true
  [[ -n "$LOOP" ]] && losetup -d "$LOOP" 2>/dev/null || true
  [[ -n "$BINFMT_REGISTERED" ]] && echo -1 > /proc/sys/fs/binfmt_misc/qemu-aarch64 2>/dev/null || true
}
BINFMT_REGISTERED=""
trap cleanup EXIT

[[ -x "$SRC/out/artifacts/arm64/aid" ]] || { echo "먼저 os/build.sh artifacts" >&2; exit 1; }
mkdir -p "$CACHE" "$MNT"

log "베이스 이미지 찾기: $RPI_RELEASE_URL"
BASE=$(curl -fsSL "$RPI_RELEASE_URL/" | grep -oE "$RPI_IMAGE_PATTERN" | sort -uV | tail -1)
[[ -n "$BASE" ]] || { echo "베이스 이미지를 찾지 못했습니다" >&2; exit 1; }
if [[ ! -f "$CACHE/$BASE" ]]; then
  log "다운로드 $BASE"
  curl -fL --retry 3 -o "$CACHE/$BASE.part" "$RPI_RELEASE_URL/$BASE" && mv "$CACHE/$BASE.part" "$CACHE/$BASE"
fi
log "SHA256 검증"
curl -fsSL "$RPI_RELEASE_URL/SHA256SUMS" | grep " \*\?$BASE\$" | sed 's/\*//' | (cd "$CACHE" && sha256sum -c -)

log "압축 해제 + ${RPI_EXTRA_GB}GB 확장"
xz -dc -T0 "$CACHE/$BASE" > "$IMG"
truncate -s +"${RPI_EXTRA_GB}"G "$IMG"
parted -s "$IMG" resizepart 2 100%
LOOP=$(losetup -fP --show "$IMG")
udevadm settle 2>/dev/null || sleep 2
e2fsck -fy "${LOOP}p2" || true
resize2fs "${LOOP}p2"
mount "${LOOP}p2" "$MNT"
mkdir -p "$MNT/boot/firmware"
mkdir -p "$MNT/boot/firmware"   # 베이스 이미지에 마운트 지점이 없을 수 있다
mount "${LOOP}p1" "$MNT/boot/firmware"

log "qemu-aarch64 (binfmt)"
mountpoint -q /proc/sys/fs/binfmt_misc || mount -t binfmt_misc binfmt_misc /proc/sys/fs/binfmt_misc
if [[ ! -e /proc/sys/fs/binfmt_misc/qemu-aarch64 ]]; then
  # binfmt 는 호스트 커널 전역이다. F 플래그로 지금 인터프리터를 열어 두므로 chroot 안에서도 동작하고,
  # 빌드가 끝나면 cleanup 에서 해제해 호스트를 원래대로 돌려놓는다.
  printf '%s\n' "$(sed 's|:[^:]*:[^:]*$|:/usr/bin/qemu-aarch64-static:F|' /usr/lib/binfmt.d/qemu-aarch64.conf)" \
    > /proc/sys/fs/binfmt_misc/register
  BINFMT_REGISTERED=1
fi
cp /usr/bin/qemu-aarch64-static "$MNT/usr/bin/"
chroot "$MNT" /bin/true || { echo "aarch64 에뮬레이션이 동작하지 않습니다" >&2; exit 1; }

log "AlliedID 커스터마이즈 (에뮬레이션이라 느립니다)"
stage_sources "$MNT" "$SRC"
chroot_mount "$MNT"
chroot "$MNT" /tmp/alliedid/scripts/customize-rootfs.sh arm64
chroot_umount "$MNT"
rm -rf "$MNT/tmp/alliedid" "$MNT/usr/bin/qemu-aarch64-static"

log "섀시(케이스 + 터치스크린) 펌웨어 설정"
if ! grep -q "AlliedID chassis" "$MNT/boot/firmware/config.txt"; then
  cat "$SRC/chassis/config.txt" >> "$MNT/boot/firmware/config.txt"
fi

sync
umount "$MNT/boot/firmware" "$MNT"
losetup -d "$LOOP"; LOOP=""
log "xz 압축"
mkdir -p "$OUT"
xz -T0 -6 -c "$IMG" > "$OUT/$NAME.xz"
( cd "$OUT" && sha256sum "$NAME.xz" > "$NAME.xz.sha256" )
chown -R "${HOST_UID:-0}:${HOST_GID:-0}" "$OUT" 2>/dev/null || true
log "완료 → out/$NAME.xz ($(du -h "$OUT/$NAME.xz" | cut -f1))"
