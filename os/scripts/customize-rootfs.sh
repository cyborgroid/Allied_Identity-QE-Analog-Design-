#!/bin/bash
# chroot 안에서 실행: Ubuntu 24.04 루트 파일시스템 → AlliedID 26.11.1 LTS
#   사용법 (빌드 스크립트가 호출): chroot <root> /tmp/alliedid/customize-rootfs.sh <amd64|arm64>
set -euo pipefail
ARCH="$1"
SRC=/tmp/alliedid
. "$SRC/config/os.env"
export DEBIAN_FRONTEND=noninteractive LC_ALL=C.UTF-8
log() { printf '\e[1;32m[customize]\e[0m %s\n' "$*"; }

log "apt 저장소 (main restricted universe multiverse, updates, security)"
if [[ "$ARCH" == amd64 ]]; then MIRROR="$UBUNTU_MIRROR_AMD64"; SEC="http://security.ubuntu.com/ubuntu"; else MIRROR="$UBUNTU_MIRROR_ARM64"; SEC="$UBUNTU_MIRROR_ARM64"; fi
rm -f /etc/apt/sources.list
cat > /etc/apt/sources.list.d/ubuntu.sources <<SRC
Types: deb
URIs: $MIRROR
Suites: $UBUNTU_SUITE $UBUNTU_SUITE-updates $UBUNTU_SUITE-backports
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg

Types: deb
URIs: $SEC
Suites: $UBUNTU_SUITE-security
Components: main restricted universe multiverse
Signed-By: /usr/share/keyrings/ubuntu-archive-keyring.gpg
SRC
# 빌드 중 서비스가 시작되지 않도록
printf '#!/bin/sh\nexit 101\n' > /usr/sbin/policy-rc.d && chmod +x /usr/sbin/policy-rc.d
apt-get update

log "패키지 설치"
mapfile -t WANT < <(cat "$SRC/config/packages.common" "$SRC/config/packages.$ARCH" | sed 's/#.*//' | awk 'NF')
AVAIL=()
for p in "${WANT[@]}"; do
  if apt-cache show "$p" >/dev/null 2>&1; then AVAIL+=("$p"); else echo "  ! 저장소에 없음, 건너뜀: $p"; fi
done
# Ubuntu raspi 서버 이미지의 cloud-init / snapd 는 쓰지 않는다
apt-get purge -y cloud-init snapd 2>/dev/null || true
apt-get install -y --no-install-recommends "${AVAIL[@]}"
apt-get -y full-upgrade

log "AlliedID 바이너리 설치"
install -m 0755 "$SRC/artifacts/$ARCH/aid" /usr/bin/aid
rm -rf /opt/alliedid/shell && mkdir -p /opt/alliedid
cp -a "$SRC/artifacts/$ARCH/shell" /opt/alliedid/shell
chmod +x /opt/alliedid/shell/alliedid-shell
ln -sf /opt/alliedid/shell/alliedid-shell /usr/bin/alliedid-shell
rm -rf /usr/share/alliedid/blocks && mkdir -p /usr/share/alliedid
cp -a "$SRC/artifacts/blocks" /usr/share/alliedid/blocks
install -m 0644 "$SRC/artifacts/desk.aid" /usr/share/alliedid/desk.aid
if [[ -d "$SRC/artifacts/android" ]]; then
  rm -rf /usr/share/alliedid/android && cp -a "$SRC/artifacts/android" /usr/share/alliedid/android
  install -m 0755 /usr/share/alliedid/android/alliedid-android /usr/bin/alliedid-android
fi

log "시스템 파일 (overlay)"
cp -a "$SRC/overlay/." /
chmod 0440 /etc/sudoers.d/alliedid
chmod 0755 /usr/bin/alliedid-session /usr/libexec/alliedid/*

log "브랜딩: /etc/os-release (ID_LIKE=ubuntu 로 호환 유지)"
cat > /usr/lib/os-release <<OSR
PRETTY_NAME="$AID_PRETTY"
NAME="$AID_NAME"
VERSION_ID="$AID_VERSION"
VERSION="$AID_VERSION LTS ($AID_CODENAME)"
VERSION_CODENAME=$UBUNTU_SUITE
ID=alliedid
ID_LIKE="ubuntu debian"
HOME_URL="https://example.invalid/alliedid"
UBUNTU_CODENAME=$UBUNTU_SUITE
LOGO=alliedid
OSR
ln -sf ../usr/lib/os-release /etc/os-release
cat > /etc/lsb-release <<LSB
DISTRIB_ID=$AID_NAME
DISTRIB_RELEASE=$AID_VERSION
DISTRIB_CODENAME=$UBUNTU_SUITE
DISTRIB_DESCRIPTION="$AID_PRETTY"
LSB
printf '%s \\n \\l\nNumber: (first boot)\n\n' "$AID_PRETTY" > /etc/issue
echo "$AID_HOSTNAME" > /etc/hostname
printf '127.0.0.1 localhost\n127.0.1.1 %s\n::1 localhost ip6-localhost ip6-loopback\n' "$AID_HOSTNAME" > /etc/hosts

log "로케일/시간대 (ko_KR.UTF-8, Asia/Seoul — 설치 후 변경 가능)"
sed -i 's/^# *\(ko_KR.UTF-8\)/\1/; s/^# *\(en_US.UTF-8\)/\1/' /etc/locale.gen 2>/dev/null || true
locale-gen ko_KR.UTF-8 en_US.UTF-8 >/dev/null
update-locale LANG=ko_KR.UTF-8
ln -sf /usr/share/zoneinfo/Asia/Seoul /etc/localtime

log "사용자 '$AID_USER' — 비밀번호 없음(잠금). 로그인은 자동, 관리는 권한(grant)으로."
getent group alliedid >/dev/null || groupadd --system alliedid
if ! id "$AID_USER" >/dev/null 2>&1; then
  # raspi 서버 이미지의 기본 사용자(ubuntu, uid 1000)가 있으면 지운다
  if id ubuntu >/dev/null 2>&1; then userdel -r ubuntu 2>/dev/null || true; fi
  useradd -m -u 1000 -s /bin/bash -c "AlliedID" "$AID_USER"
fi
passwd -l "$AID_USER" >/dev/null
for g in alliedid video audio input plugdev netdev dialout render; do
  getent group "$g" >/dev/null && usermod -aG "$g" "$AID_USER"
done
install -d -o "$AID_USER" -g "$AID_USER" "/home/$AID_USER/.config/alliedid"
install -m 0644 -o "$AID_USER" -g "$AID_USER" /usr/share/alliedid/desk.aid "/home/$AID_USER/.config/alliedid/desk.aid"

log "서비스"
install -d -m 0755 /var/lib/alliedid
rm -f /var/lib/alliedid/device.key   # 신원은 이미지가 아니라 각 기기의 첫 부팅에서 생긴다
systemctl enable alliedid-firstboot.service alliedid-moded.service alliedid-vpn.service lightdm.service NetworkManager.service gpsd.service 2>/dev/null || true
systemctl --global enable alliedid-blocks.service 2>/dev/null || true
ln -sf /lib/systemd/system/lightdm.service /etc/systemd/system/display-manager.service
echo /usr/sbin/lightdm > /etc/X11/default-display-manager
systemctl set-default graphical.target
# netplan → NetworkManager 위임
mkdir -p /etc/netplan && printf 'network:\n  version: 2\n  renderer: NetworkManager\n' > /etc/netplan/01-alliedid.yaml && chmod 600 /etc/netplan/01-alliedid.yaml
rm -f /etc/netplan/50-cloud-init.yaml
[[ -d /etc/cloud ]] && touch /etc/cloud/cloud-init.disabled

if [[ "$ARCH" == amd64 ]]; then
  log "라이브 ISO (casper): 라이브 사용자 = $AID_USER"
  cat > /etc/casper.conf <<CAS
export USERNAME="$AID_USER"
export USERFULLNAME="AlliedID"
export HOST="$AID_HOSTNAME"
export BUILD_SYSTEM="Ubuntu"
export FLAVOUR="AlliedID"
CAS
  install -m 0755 "$SRC/scripts/alliedid-install" /usr/sbin/alliedid-install
  update-initramfs -u -k all
fi

log "정리"
rm -f /usr/sbin/policy-rc.d
apt-get clean
rm -rf /var/lib/apt/lists/* /var/tmp/* /root/.bash_history
find /tmp -mindepth 1 -maxdepth 1 ! -name alliedid -exec rm -rf {} +
truncate -s 0 /etc/machine-id   # 기기마다 첫 부팅에서 새로 생성 (기기 지문의 재료)
log "완료: $AID_PRETTY ($ARCH)"
