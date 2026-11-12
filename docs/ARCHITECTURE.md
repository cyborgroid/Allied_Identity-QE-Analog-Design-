# AlliedID 아키텍처

## 1. 신원 = 계정 = 번호 = 열쇠 (`aid-identity`)

```
첫 부팅 ─▶ Ed25519 키쌍 생성 ─┬─▶ device.key  (0600 root)  seed + SHA-256(하드웨어 ID)
                              ├─▶ public.json (0644)       공개키, 번호, WireGuard 공개키
                              └─▶ grants/*.json (0644)     기본 권한: call, net.*, apps.install, desktop.edit
```

- **하드웨어 봉인**: 키 파일에는 하드웨어 지문(Pi SoC 시리얼, 없으면 `/etc/machine-id`)이 들어갑니다.
  다른 기기로 복사하면 `WrongDevice`가 나서 열리지 않습니다.
- **번호**: `SHA-256("alliedid/number/v1" ‖ pubkey)` 앞 8바이트 mod 10¹¹ + Luhn 체크 숫자 → 12자리.
  국가번호(`+`)는 문법적으로 거부합니다. Rust(`AidNumber`)와 C#(`AidNumber.IsValid`)이 같은 규칙을 씁니다.
- **권한(grant)**: `{capability, issuer, subject, issued_at, expires_at}`를 기기 키로 서명한 토큰입니다.
  `check`는 *이 기기가 발급하고 이 기기가 받은*, 서명이 유효하고 만료되지 않은 토큰만 인정합니다.
  `net.*` 같은 와일드카드를 지원합니다.
- **권한 경계**: 일반 사용자 프로세스는 `public.json`과 `grants/`를 **읽을 수만** 있습니다(확인 가능, 발급 불가).
  관리 작업은 sudoers에 등록된 두 관문 `aid-install`(apps.install), `aid-admin`(system.admin)을 거치며,
  관문이 다시 `aid check`로 권한을 확인합니다. `system.admin`은 기본으로 부여하지 않습니다.

## 2. 국경 없는 오버레이 (`aid-net`)

| 파생 | 규칙 |
|---|---|
| WireGuard 개인키 | `SHA-256("alliedid/derive/v1/wireguard" ‖ 0 ‖ seed)` → X25519 |
| 오버레이 IPv6 | `fd26:a11e:d000::/48` + `SHA-256(번호)` 80비트 |
| 오버레이 IPv4 | `100.64.0.0/10` + 22비트 (보조, 충돌 가능) |
| 통화 | `sip:<번호>@[<오버레이 IPv6>]` → baresip |

번호만 알면 상대의 오버레이 주소를 계산할 수 있습니다. 상대의 WireGuard 공개키와 접속 지점은
피어 목록(`/var/lib/alliedid/peers.json`, `aid vpn add-peer`)으로 받습니다. `alliedid-vpn.service`가
피어 목록이 있을 때 `aid vpn config`로 `wg-quick` 설정을 만들어 `aid0`을 올립니다.

**위치**: `nmcli` 스캔 결과의 신호 세기를 경로손실 모델로 거리로 바꿔 알려진 AP의 가중 중심을 구하고,
gpsd(TPV) 결과와 분산 역수로 융합합니다. AP DB(`ap-db.json`)는 GPS가 잡힐 때 학습합니다.

## 3. Phone ↔ Desktop (`aid-mode`)

`alliedid-moded.service` → `aid mode watch`가 1초마다 `/sys/class/drm/card*-*/status`를 읽고,
바뀔 때만 `/run/alliedid/mode.json`을 원자적으로 씁니다.

| 커넥터 | 분류 |
|---|---|
| DSI, DPI, eDP, LVDS, SPI | 내장 패널 → Phone |
| HDMI, DP, VGA, DVI, **Virtual**(VMware) | 외부 모니터 → Desktop |

강제 우선순위: 셸 `--mode` → desk.aid `mode` → 커널 인자 `alliedid.mode=` → `/etc/alliedid/mode` → 자동 감지 → (파일 없음) 창 비율.

## 4. 셸 (`shell/`, C# .NET 10 + Avalonia 11)

- `ShellContext`: `aid` 클라이언트, desk.aid 감시(`DeskStore`), GPIO(`CircuitRunner`), 1초 틱, 앱 열기 요청.
- `MainWindow`: 모드에 따라 `DesktopView`/`PhoneView`를 갈아 끼웁니다.
- `WidgetFactory`: 같은 위젯 컨트롤을 Desktop에서는 **드래그 가능한 창**(8px 격자), Phone에서는 **전체화면 앱**으로 씁니다.
  위젯: 시계, 터미널, 열린 파일, 패키지, 작업, 전화, 메모, 네트워크, 회로, 설정, 조립대, 안드로이드.
- 셸과 시스템의 경계는 `aid --json …` 하나입니다(`AidClient`).

## 5. 조립대와 DeskScript

```
Blockly 블록 ──(deskblocks.js 생성기)──▶ DeskScript 텍스트 ──POST /api/desk──▶ ~/.config/alliedid/desk.aid
                                                                                     │ FileSystemWatcher
셸 ◀──(ANTLR DeskScriptParser → DeskLayout)───────────────────────────────────────────┘
```

- 문법: [grammar/DeskScript.g4](../grammar/DeskScript.g4). 키워드도 이름 자리에 쓸 수 있도록 `ident` 규칙을 둡니다(`widget circuit`).
- 의미 검사: 알 수 없는 앱/위젯/부품/동작, `#RRGGBB` 색, 양수 크기, GPIO 2–27, 핀 중복.
- 깨진 desk.aid는 기본 바탕화면 + 오류 목록으로 대체합니다(설정 위젯에 표시).
- JS 생성기와 C# `Emit`은 **바이트 단위로 같은 형식**을 냅니다(교차 테스트로 보장).
- 회로: `button … -> call|launch|toggle|run|notify`. 실제 Pi에서는 `gpiomon`/`gpioset`(libgpiod v1·v2 자동 판별, Ubuntu 24.04 는 v1.6),
  GPIO가 없으면 시뮬레이션(회로 위젯의 "누르기"/"전환" 버튼).
- 저장 서버는 `127.0.0.1`에만 바인드하고, Host/Origin을 검사하고, `aid check desktop.edit`이 통과할 때만 씁니다.

## 6. 이미지

```
                 os/scripts/customize-rootfs.sh  (공통: 패키지, 바이너리, overlay, 사용자, 서비스, 브랜딩)
                      ▲                                   ▲
debootstrap noble amd64 (chroot)            Ubuntu 24.04 raspi 서버 이미지 (loop + qemu-aarch64 chroot)
                      │                                   │
squashfs + casper + grub-mkrescue             +6GB 확장, config.txt(섀시) 추가
                      ▼                                   ▼
AlliedID-26.11.1-desktop-amd64.iso            AlliedID-26.11.1-rpi5-arm64.img.xz
   └─ make-vmware.sh → .vmx + .vmdk
   └─ alliedid-install (라이브 → 디스크)
```

부팅 순서: `alliedid-firstboot`(신원) → `alliedid-moded`(모드) → LightDM 자동 로그인(`aid`, 비밀번호 잠금)
→ `alliedid-session`(Openbox + 조립대 서버 + antimicrox + `alliedid-shell --fullscreen`, 죽으면 재시작).
