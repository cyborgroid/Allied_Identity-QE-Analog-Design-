# AlliedID 26.11.1 LTS — Desktop Phone OS

> 기획: [Allied Identity [QE Analog Design].md](Allied%20Identity%20[QE%20Analog%20Design].md) (정구영, 2026-11-12)

Raspberry Pi 5 + 케이스 일체형 터치스크린에서 돌아가는 **"전화가 되는 데스크톱"** OS입니다.
Ubuntu 24.04 LTS를 베이스로 하며, 화면만 보이면 **Phone**, 모니터를 꽂으면 **Desktop**으로 바뀝니다.
비밀번호는 없습니다. **기기가 열쇠이고, 번호가 계정입니다.**

```
┌──────────────── AlliedID 26.11.1 LTS ─────────────────┐
│  alliedid-shell (C#/Avalonia)   조립대 (Google Blockly) │  ← 사용자 화면
│     Desktop 뷰 │ Phone 뷰           DeskScript (ANTLR)   │
├────────────────────────────────────────────────────────┤
│  aid (Rust)  신원·번호·권한 │ VPN·Wi-Fi·위치 │ 모드 감지 │  ← 시스템
├────────────────────────────────────────────────────────┤
│  Ubuntu 24.04 (noble): Xorg/Openbox/LightDM, NM, WireGuard, gpsd, baresip, libgpiod │
├──────────────────────────┬─────────────────────────────┤
│  amd64 ISO → VMware       │  arm64 IMG → Raspberry Pi 5 │
└──────────────────────────┴─────────────────────────────┘
```

## 기획 항목 → 구현

| 기획 | 구현 | 위치 |
|---|---|---|
| 전화번호 = 기기 고유 코드, 국가번호 없음 | 기기 Ed25519 공개키에서 파생한 12자리 번호 `AID 4368-4688-2187` (Luhn 체크) | [core/crates/aid-identity](core/crates/aid-identity) |
| 비밀번호 없음, 권한이 문을 연다 | 기기 키로 서명한 권한 토큰(grant). `aid check call` → 🔓/🔒. 개인키는 하드웨어에 봉인되며 root만 읽음 | 〃 |
| 국경 없는 VPN, 무료 데이터 | 번호 → 오버레이 주소(`fd26:a11e:d000::/48`)·WireGuard 키를 결정적으로 파생. 통화는 `sip:번호@[오버레이]` | [core/crates/aid-net](core/crates/aid-net) |
| Wi-Fi는 기기 탐색용, GPS 연동 | `nmcli` 스캔 + GPS 위치로 AP 위치를 학습하고, Wi-Fi·GPS를 정확도 가중 평균 | 〃 `wifi.rs`, `location.rs` |
| Phone ↔ Desktop | DRM 커넥터 감시: 내장 DSI 화면만 있으면 Phone, HDMI 모니터가 꽂히면 Desktop | [core/crates/aid-mode](core/crates/aid-mode) |
| 우분투 유지항목 (사이드바/스크립트/터미널/열린 파일·패키지/작업·데스크톱) | Desktop 뷰: 사이드바, 터미널, 열린 파일, 패키지, 작업 위젯 + 작업 표시줄 | [shell/](shell) |
| 블록코딩으로 바탕화면 조립, 회로도 | Blockly 조립대 → DeskScript 생성 → 셸이 실시간 재조립. 회로 블록은 Pi GPIO(버튼·LED)로 동작 | [blocks/](blocks), [grammar/](grammar) |
| C#·Rust로 C/C++·Python·JS·CSS·HTML·ANTLR 환경 | Rust(시스템) · C#(셸) · ANTLR(DeskScript) · JS/HTML/CSS(조립대) · Python(조립대 서버) · Bash(이미지) | 전체 |
| VMware용 ISO | debootstrap → casper 라이브 ISO (BIOS+UEFI) + `.vmx` 가상머신 패키지 + 디스크 설치기 | [os/](os) |
| Raspberry Pi 5 설치 | 같은 커스터마이즈 스크립트로 Ubuntu raspi 이미지 → `.img.xz` | [os/scripts/build-rpi5.sh](os/scripts/build-rpi5.sh) |
| 안드로이드 | Waydroid로 안드로이드 앱 실행. AVD에서 셸 실행은 로드맵 | [android/](android) |
| 마우스/키보드/모니터, 게임 조이스틱 | 일반 X11 입력 + `antimicrox`(게임패드 → 마우스/키보드), 폰 모드 화면 키보드 `onboard` | [os/config/packages.common](os/config/packages.common) |
| 섀시 | Pi 5 + 7" 터치스크린 케이스(OpenSCAD), 펌웨어 디스플레이 설정 | [chassis/](chassis) |

## 빠르게 실행해 보기 (개발 PC)

```bash
make dev-identity        # 개발용 기기 신원 생성 (~/.local/share/alliedid)
make run                 # 셸 실행 (창 비율로 Phone/Desktop 자동)
make run-phone           # Phone 모드 (420x860)
make blocks              # 조립대 → http://127.0.0.1:8626  (저장하면 실행 중인 셸이 즉시 재조립)
```

`aid` 명령 예시:

```bash
aid status                    # 번호, 모드, 권한
aid check call                # 🔓 call: 문이 돌아갑니다   (exit 0)
aid check system.admin        # 🔒 … 돌아가지 않습니다     (exit 1)
aid lookup 436846882187       # 번호 → 오버레이 주소, SIP URI
aid wifi scan                 # 주변 Wi-Fi
aid vpn info                  # WireGuard 공개키, 오버레이 IPv6/IPv4
aid mode                      # phone | desktop
```

## 테스트

```bash
make test
```

| 묶음 | 내용 | 개수 |
|---|---|---|
| Rust (`cargo test`) | 번호 파생/검증, 하드웨어 봉인, 권한 위조·만료·남의 발급 거부, 공개 신원, Wi-Fi 파싱, 위치 융합, gpsd, 오버레이 주소, 모드 판정 | 21 |
| C# Core (xUnit) | DeskScript 파싱/의미 오류/왕복, Rust와 같은 번호 규칙, 모드 파일, GPIO 시뮬레이션·libgpiod v1/v2 명령, **Blockly 출력 ↔ ANTLR 파서 교차 검증** | 30 |
| C# UI (Avalonia Headless) | Desktop/Phone 뷰 실제 렌더링, 회로 버튼 → 다이얼러 | 2 |
| Blocks (`node --test`) | 블록 → DeskScript 생성, 회로/핀 충돌 경고 | 3 |

UI 테스트에 `AID_SCREENSHOT_DIR=<폴더>`를 주면 화면 PNG를 저장합니다.

## OS 이미지 빌드

필요한 것: Docker(이미지 빌더는 `--privileged` 컨테이너 안에서 root로 돌아가므로 호스트 sudo는 필요 없음), Rust, .NET 10 SDK, Node 18+.

```bash
make artifacts   # aid(정적 musl) + alliedid-shell(self-contained) + 조립대 → out/artifacts/{amd64,arm64}
make iso         # out/AlliedID-26.11.1-desktop-amd64.iso      (VMware Workstation)
make vmware      # out/vmware/AlliedID-26.11.1/AlliedID.vmx    (ISO 연결된 VM, 32GB 디스크)
make rpi5        # out/AlliedID-26.11.1-rpi5-arm64.img.xz      (SD/NVMe에 굽기)
make qemu        # ISO를 QEMU로 바로 부팅
```

- **VMware**: `AlliedID.vmx` 열기 → 라이브 부팅 → 터미널에서 `sudo alliedid-install /dev/sda`로 가상 디스크에 설치.
- **Pi 5**: `rpi-imager` 또는 `xz -dc AlliedID-…img.xz | sudo dd of=/dev/sdX bs=4M status=progress`.
- 첫 부팅에서 `alliedid-firstboot`이 기기 신원을 만들고, 번호를 `/etc/issue`와 셸에 표시합니다.

> ⚠ **ISO를 라즈베리 파이에 그대로 넣는 것이 아닙니다.** VMware(PC)는 x86-64, Pi 5는 ARM64라서 실행 파일이 서로 호환되지 않습니다.
> 대신 **같은 커스터마이즈 스크립트**([customize-rootfs.sh](os/scripts/customize-rootfs.sh))가 두 아키텍처 이미지를 똑같이 만듭니다.
> VMware에서 확인한 OS가 Pi에서도 그대로 돌아가는 구조입니다.

## 현실성 노트 (기획과 다른 점)

소프트웨어로 해결할 수 없는 부분은 이렇게 처리했습니다.

1. **"데이터 완전 무료"**: 이동통신 데이터 요금은 OS가 없앨 수 없습니다. AlliedID 기기끼리의 통화·통신은 이미 있는 인터넷 연결(Wi-Fi/이더넷/테더링) 위의 오버레이로 오가므로 **추가 요금이 없다**는 의미로 구현했습니다. Pi 5에는 셀룰러 모뎀이 없어서, 밖에서 쓰려면 USB LTE 모뎀과 요금제가 필요합니다.
2. **국가번호 없는 번호**: AlliedID 번호끼리만 통화됩니다. 일반 전화번호(PSTN)와 연결하려면 통신사/SIP 트렁크가 필요하고, 국가별 규제를 받습니다.
3. **국경 없는 VPN**: 기기 간 WireGuard 오버레이입니다. NAT 뒤 기기끼리 연결하려면 공인 IP가 있는 **릴레이 피어**가 최소 하나 필요합니다(`aid vpn add-peer … --relay`). 릴레이/디렉터리 서버는 아직 포함하지 않았습니다.
4. **번호 공간**: 11자리(+체크 1자리) 해시라서 기기가 약 30만 대를 넘으면 번호 충돌 확률이 커집니다. 대규모로 쓰려면 등록소(registry)나 더 긴 번호가 필요합니다.
5. **"Wi-Fi가 GPS가 된다"**: 전 세계 AP 위치 DB가 없으므로, GPS가 잡힐 때 주변 AP를 학습해(`aid locate --learn`) 이후 Wi-Fi만으로 위치를 추정합니다.
6. **비밀번호 없음의 대가**: 기기를 물리적으로 가진 사람이 곧 사용자입니다(설계 의도). 복구 모드/콘솔 root 접근도 물리 접근으로 가능하며, 분실 대비 원격 잠금은 아직 없습니다. 라이브 ISO 세션의 신원은 재부팅하면 사라집니다.
7. **안드로이드 AVD에서 셸 실행**과 **Pi에서 Waydroid**는 하드웨어/SDK 확인이 필요한 로드맵 항목입니다([android/README.md](android/README.md)).

## 구조

```
core/      Rust 워크스페이스 — aid-identity, aid-net, aid-mode, aid-cli(`aid`)
shell/     C# .NET 10 — AlliedID.Shell.Core(DeskScript/플랫폼), AlliedID.Shell(Avalonia UI), 테스트
grammar/   DeskScript.g4 (ANTLR 4.13) + C# 파서 생성 스크립트
blocks/    조립대: Blockly 블록/생성기, 편집기 화면, 저장 서버(Python)
os/        이미지 빌드: Docker 빌더, ISO/Pi 5/VMware 스크립트, 패키지 목록, rootfs overlay
android/   Waydroid 설치·실행, AVD 로드맵
chassis/   케이스(OpenSCAD), Pi 5 펌웨어 디스플레이 설정
docs/      아키텍처 문서
tools/     antlr-4.13.2-complete.jar
```

자세한 설계는 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)를 보세요.
