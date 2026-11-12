# 섀시 (Chassis) — "그냥 칩이 아니라 보관함"

AlliedID Desktop Phone 은 Raspberry Pi 5 와 터치스크린을 하나의 케이스에 넣은 기기입니다.

| 파일 | 내용 |
|---|---|
| [case.scad](case.scad) | 파라메트릭 케이스 (앞판 베젤 + 뒷판). 3D 프린트용 STL 을 OpenSCAD 로 출력 |
| [config.txt](config.txt) | Pi 5 펌웨어 설정 조각. `os/build.sh rpi5` 가 `/boot/firmware/config.txt` 에 덧붙임 |

## 호환성 체크리스트

- **화면**: 공식 Touch Display 2 (DSI) 기준. 다른 DSI/HDMI 패널이면 `config.txt` 의 `dtoverlay` 를 바꾼다.
  내장 패널이 DSI 로 잡히면 **Phone 모드**, micro HDMI 에 모니터를 꽂으면 **Desktop 모드** (`aid-moded`).
- **포트**: USB-C 전원(5V 5A 권장), micro HDMI ×2, USB-A ×4 (키보드/마우스/게임패드), 이더넷.
- **버튼/LED**: 케이스 윗면 구멍에 푸시 버튼을 달고 GPIO 에 연결 → 조립대 회로도에서 동작 지정
  (예: GPIO27 버튼 → 지정한 번호로 전화). 버튼은 GPIO 와 GND 사이에 연결 (내부 풀업 사용).
- **위치**: GPS 모듈은 USB(또는 UART) 로 연결하면 `gpsd` 가 잡고 `aid locate` 가 Wi-Fi 위치와 융합.
- **치수**: `case.scad` 의 디스플레이 치수는 **실물 확인 전 가정값**입니다. 출력 전에 반드시 측정하세요.
