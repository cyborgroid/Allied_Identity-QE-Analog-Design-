// AlliedID Desktop Phone — Raspberry Pi 5 + 7" 터치스크린 일체형 케이스 (OpenSCAD, 파라메트릭)
//
//   openscad -o case-front.stl -D 'part="front"' case.scad
//   openscad -o case-back.stl  -D 'part="back"'  case.scad
//
// 치수(mm)는 출력 전에 실제 부품/데이터시트로 확인할 것. 특히 디스플레이 외곽 치수.
// 앞판: 화면 베젤. 뒷판: Pi 5 장착 + 포트 구멍(전원, micro HDMI ×2 → Desktop 모드,
// USB ×4 → 키보드/마우스/조이스틱, 이더넷) + GPIO 버튼 구멍(조립대 회로도의 "긴급전화" 등).

part = "assembly"; // "front" | "back" | "assembly"

// ── 디스플레이 (Raspberry Pi Touch Display 2, 7") ─────────────
disp_w = 189.3;   // 외곽 가로  (확인 필요)
disp_h = 120.3;   // 외곽 세로  (확인 필요)
disp_t = 7.5;     // 유리+패널 두께 (확인 필요)
view_w = 155.0;   // 보이는 영역 가로 (확인 필요)
view_h = 88.0;    // 보이는 영역 세로 (확인 필요)

// ── Raspberry Pi 5 ────────────────────────────────────────────
pi_w = 85; pi_h = 56;
pi_hole_dx = 58; pi_hole_dy = 49; pi_hole_off = 3.5; pi_hole_d = 2.7;
standoff_h = 6;

// ── 케이스 ─────────────────────────────────────────────────────
wall = 2.4;
gap = 0.4;          // 공차
depth = 34;         // 뒷판 내부 깊이 (Pi + 쿨러 + 케이블)
corner = 8;
btn_d = 12;         // GPIO 버튼 구멍
btn_count = 2;

outer_w = disp_w + 2 * (wall + gap);
outer_h = disp_h + 2 * (wall + gap);

module rounded_box(w, h, t, r) {
  linear_extrude(t) offset(r) offset(-r) square([w, h], center = true);
}

module front() {
  difference() {
    rounded_box(outer_w, outer_h, disp_t + wall, corner);
    // 디스플레이 자리
    translate([0, 0, wall]) linear_extrude(disp_t + 1) square([disp_w + 2 * gap, disp_h + 2 * gap], center = true);
    // 보이는 영역
    translate([0, 0, -1]) linear_extrude(wall + 2) offset(2) offset(-2) square([view_w, view_h], center = true);
  }
}

module back() {
  difference() {
    union() {
      difference() {
        rounded_box(outer_w, outer_h, depth + wall, corner);
        translate([0, 0, wall]) rounded_box(outer_w - 2 * wall, outer_h - 2 * wall, depth + 1, corner - wall);
      }
      // Pi 5 스탠드오프 (뒷판 중앙에서 오른쪽으로 치우침: 포트가 측면으로 나오게)
      translate([outer_w / 2 - wall - pi_w / 2 - 2, 0, wall])
        for (x = [-1, 1], y = [-1, 1])
          translate([x * pi_hole_dx / 2 + (pi_w / 2 - pi_hole_off - pi_hole_dx / 2) * 0, y * pi_hole_dy / 2, 0])
            difference() { cylinder(h = standoff_h, d = 6, $fn = 32); cylinder(h = standoff_h + 1, d = pi_hole_d, $fn = 24); }
    }
    // 측면 포트 (오른쪽 벽): USB/이더넷 블록
    translate([outer_w / 2 - wall - 1, 0, wall + standoff_h + 1.6]) cube([wall + 2, 50, 17], center = false);
    // 아래쪽 벽: USB-C 전원 + micro HDMI ×2 (모니터 → Desktop 모드)
    translate([outer_w / 2 - wall - pi_w - 2, -outer_h / 2 - 1, wall + standoff_h + 1.6]) cube([46, wall + 2, 8]);
    // 윗면 GPIO 버튼 구멍 (긴급전화 등 — 조립대 회로도에서 핀 지정)
    for (i = [0 : btn_count - 1])
      translate([-outer_w / 4 + i * (btn_d + 10), outer_h / 2 + 1, wall + depth / 2]) rotate([90, 0, 0]) cylinder(h = wall + 2, d = btn_d, $fn = 48);
    // 통풍 슬롯 (Active Cooler 위)
    for (i = [-4 : 4]) translate([outer_w / 2 - wall - pi_w / 2 - 2 + i * 6, 0, -1]) linear_extrude(wall + 2) square([2.5, 36], center = true);
  }
}

if (part == "front") front();
else if (part == "back") back();
else {
  color("#2b3050") back();
  translate([0, 0, depth + wall + 0.5]) color("#14182b") front();
}
