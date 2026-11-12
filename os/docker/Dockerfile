# AlliedID 이미지 빌더 (debootstrap / squashfs / GRUB ISO / Pi 이미지 + qemu-user)
FROM ubuntu:24.04
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
      debootstrap ubuntu-keyring squashfs-tools xorriso mtools dosfstools \
      grub-pc-bin grub-efi-amd64-bin grub-common \
      parted e2fsprogs fdisk util-linux udev kpartx \
      qemu-user-static binfmt-support \
      xz-utils curl ca-certificates rsync file \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
