// AlliedID Desktop Phone — Raspberry Pi 5 금속 케이스 + 윗면 4" 터치스크린 (OpenSCAD, 파라메트릭)
//
// 기준 디자인: reference/After.png — 낮고 둥근 은색 금속 몸체의 윗면 전체가 화면.
// 특정 제품을 복제하지 않은 자체 형상이며, 상표 각인은 넣지 않는다.
//
//   openscad -o body.stl   -D 'part="body"'   case.scad    # 몸체 (화면 턱 + 단자 구멍)
//   openscad -o bottom.stl -D 'part="bottom"' case.scad    # 바닥판 (Pi 5 고정, 통풍)
//   openscad -o preview.png --imgsize=1200,900 case.scad   # 조립 미리보기
//
// ⚠ "확인 필요" 치수는 데이터시트/어림값이다. 3D 프린트 시제품으로 맞춰 본 뒤 금속 가공으로 넘어갈 것.

part = "assembly"; // "body" | "bottom" | "assembly"

// ── 윗면 화면: Waveshare 4inch DSI LCD (480x800) ──────────────────
panel_l = 104.37;   // 외곽 (제조사 도면)
panel_w = 65.44;    // 외곽 (제조사 도면)
panel_t = 6.0;      // 유리+패널+터치 두께 — 도면에 없음, 실측 필요
view_l  = 87.0;     // 표시 영역 (제조사 도면)
view_w  = 52.44;

// ── Raspberry Pi 5 ────────────────────────────────────────────────
pi_l = 85; pi_w = 56; pi_t = 1.6;
pi_hole = [[3.5, 3.5], [61.5, 3.5], [3.5, 52.5], [61.5, 52.5]]; // 장착 구멍 (기판 원점 기준)
pi_hole_d = 2.7;
port_stack_h = 16.0;  // USB/이더넷 높이 (기판 윗면 기준) — 확인 필요
standoff_h = 3.0;

// ── 몸체 ───────────────────────────────────────────────────────────
wall   = 2.6;
floor_t = 2.4;
gap    = 0.3;            // 화면 공차
ledge  = 1.6;            // 화면을 받치는 안쪽 턱 폭
corner = 9;              // 모서리 반경 (After 의 둥근 몸체)
edge_r = 2.0;            // 윗/아랫 모서리 라운드
inner_l = panel_l + 2 * gap;
inner_w = panel_w + 2 * gap;
outer_l = inner_l + 2 * wall;
outer_w = inner_w + 2 * wall;
board_z = floor_t + standoff_h;                    // 기판 아랫면 높이
panel_z = board_z + pi_t + port_stack_h + 1.0;     // 화면 아랫면 높이 (포트 위 1mm 여유)
height  = panel_z + panel_t;                       // 화면이 몸체 윗면과 같은 높이

// 기판 위치: 단자 블록(USB/이더넷)이 +x 짧은 면에 붙도록 오른쪽 정렬
pi_x = outer_l / 2 - wall - pi_l - 0.5;
pi_y = -pi_w / 2;

// ── 단자 구멍 (After 배치) ────────────────────────────────────────
// 긴 앞면(-y): 작은 전원(패널형 USB-C), 풀사이즈 HDMI(내부 micro-HDMI 연장), 3.5mm 잭(내장 USB 오디오)
// 짧은 옆면(+x): 랜 포트 + USB 4개(Pi 5 실제 위치 — 기판 도면으로 확인 필요)
front_ports = [  // [x 중심, 폭, 높이, 모서리 r] — 몸체 중심 기준
  [-36, 9.5, 3.8, 1.6],   // USB-C 전원
  [-18, 15.5, 6.0, 1.0],  // HDMI (패널 고정형)
  [ -2,  6.2, 6.2, 3.1],  // 3.5mm 잭
];
side_ports = [   // [y 중심(기판 기준), 폭, 높이] — Pi 5 도면 확인 필요
  [10.2, 16.5, 13.8],     // 이더넷
  [29.1, 13.6, 16.0],     // USB 2단
  [47.0, 13.6, 16.0],     // USB 2단
];
port_z = board_z + pi_t;   // 단자 바닥 높이

$fn = 48;

module rounded_rect(l, w, r) { offset(r) offset(-r) square([l, w], center = true); }

// 위아래 모서리를 살짝 굴린 둥근 몸체
module soft_block(l, w, h, r, er) {
  hull() {
    translate([0, 0, er]) linear_extrude(h - 2 * er) rounded_rect(l, w, r);
    linear_extrude(h) rounded_rect(l - 2 * er, w - 2 * er, r - er);
  }
}

module port_hole(wid, hgt, r) {
  rotate([90, 0, 0]) linear_extrude(wall * 3, center = true) rounded_rect(wid, hgt, min(r, hgt / 2 - 0.01));
}

module body() {
  difference() {
    soft_block(outer_l, outer_w, height, corner, edge_r);
    // 화면 자리 (윗면에서 panel_t 만큼)
    translate([0, 0, panel_z]) linear_extrude(panel_t + 1) rounded_rect(inner_l, inner_w, corner - wall);
    // 내부 공간 (턱을 남김)
    translate([0, 0, floor_t]) linear_extrude(panel_z - floor_t + 0.01) rounded_rect(inner_l - 2 * ledge, inner_w - 2 * ledge, corner - wall - ledge);
    // 바닥은 열어 두고 바닥판으로 막는다
    translate([0, 0, -1]) linear_extrude(floor_t + 1.01) rounded_rect(inner_l - 2 * ledge, inner_w - 2 * ledge, corner - wall - ledge);
    // 앞면 단자
    for (p = front_ports) translate([p[0], -outer_w / 2 + wall / 2, port_z + 3 + p[2] / 2]) port_hole(p[1], p[2], p[3]);
    // 옆면 단자 (Pi 5 USB/이더넷)
    for (p = side_ports) translate([outer_l / 2 - wall / 2, pi_y + p[0], port_z + p[2] / 2]) rotate([0, 0, 90]) port_hole(p[1] + 0.8, p[2] + 0.8, 1);
    // 반대쪽 옆면 통풍 슬릿
    for (i = [-3 : 3]) translate([-outer_l / 2 + wall / 2, i * 6, board_z + 8]) rotate([0, 0, 90]) port_hole(3, 8, 1.4);
    // 화면 FPC 가 기판 DSI 커넥터로 내려갈 자리는 내부 공간에 포함됨
  }
}

module bottom() {
  difference() {
    linear_extrude(floor_t) rounded_rect(inner_l - 2 * ledge - 0.4, inner_w - 2 * ledge - 0.4, corner - wall - ledge);
    // 통풍 (SoC 아래)
    for (i = [-4 : 4]) translate([pi_x + 38 + i * 5, 0, -1]) linear_extrude(floor_t + 2) square([2.4, 34], center = true);
    // 나사 구멍 (기판 장착 구멍과 동축, 아래에서 M2.5)
    for (h = pi_hole) translate([pi_x + h[0], pi_y + h[1], -1]) cylinder(h = floor_t + 2, d = 2.9);
  }
  // 스탠드오프
  for (h = pi_hole) translate([pi_x + h[0], pi_y + h[1], floor_t])
    difference() { cylinder(h = standoff_h, d = 5.5); cylinder(h = standoff_h + 1, d = pi_hole_d); }
  // 고무발 자리
  for (sx = [-1, 1], sy = [-1, 1]) translate([sx * (inner_l / 2 - 10), sy * (inner_w / 2 - 9), -0.6]) cylinder(h = 0.6, d = 8);
}

// 조립 미리보기용 기판·화면 덩어리 (출력 대상 아님)
module ghosts() {
  color("#0e5c3f") translate([pi_x, pi_y, board_z]) cube([pi_l, pi_w, pi_t]);
  color("#9aa0a6") for (p = side_ports) translate([pi_x + pi_l - 17, pi_y + p[0] - p[1] / 2, board_z + pi_t]) cube([18, p[1], p[2]]);
  color("#111318") translate([0, 0, panel_z]) linear_extrude(panel_t) rounded_rect(panel_l, panel_w, corner - wall);
  color("#2a5bd7") translate([0, 0, height - 0.05]) linear_extrude(0.1) square([view_l, view_w], center = true);
}

if (part == "body") body();
else if (part == "bottom") bottom();
else {
  color("#c9ccd1") body();
  color("#a9adb3") bottom();
  %ghosts();
  echo(str("외형 ", outer_l, " x ", outer_w, " x ", height, " mm (화면 윗면 = 몸체 윗면)"));
}
