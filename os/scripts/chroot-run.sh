# source 용: chroot 마운트 도우미 (빌더 컨테이너 안, root)
chroot_mount() {
  local r="$1"
  mount --bind /dev "$r/dev"; mount --bind /dev/pts "$r/dev/pts"
  mount -t proc proc "$r/proc"; mount -t sysfs sysfs "$r/sys"
  mount -t tmpfs tmpfs "$r/run"
  if [[ -L "$r/etc/resolv.conf" || -f "$r/etc/resolv.conf" ]]; then mv "$r/etc/resolv.conf" "$r/etc/resolv.conf.aid-bak"; fi
  cp /etc/resolv.conf "$r/etc/resolv.conf"
}
chroot_umount() {
  local r="$1"
  rm -f "$r/etc/resolv.conf"
  if [[ -e "$r/etc/resolv.conf.aid-bak" || -L "$r/etc/resolv.conf.aid-bak" ]]; then mv "$r/etc/resolv.conf.aid-bak" "$r/etc/resolv.conf"; fi
  for m in run sys proc dev/pts dev; do umount -l "$r/$m" 2>/dev/null || true; done
}
stage_sources() {
  # 저장소의 os/ 와 빌드 산출물을 chroot 의 /tmp/alliedid 로
  local r="$1" src="$2"
  rm -rf "$r/tmp/alliedid"; mkdir -p "$r/tmp/alliedid"
  cp -a "$src/os/config" "$src/os/overlay" "$src/os/scripts" "$r/tmp/alliedid/"
  cp -a "$src/out/artifacts" "$r/tmp/alliedid/artifacts"
}
