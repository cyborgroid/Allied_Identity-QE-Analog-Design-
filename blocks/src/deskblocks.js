/*
 * AlliedID 조립대 — 블록 정의 + DeskScript 생성기.
 *
 * 브라우저(전역 Blockly)와 Node(테스트, require('blockly')) 양쪽에서 쓴다.
 *   DeskBlocks.install(Blockly)  → { generator, toolbox, defaultWorkspace }
 */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.DeskBlocks = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';

  const APPS = [
    ['⌨ 터미널', 'terminal'], ['🗂 파일', 'files'], ['📦 패키지', 'packages'], ['📊 작업', 'tasks'],
    ['📞 전화', 'dialer'], ['🧩 조립대', 'blocks'], ['📡 네트워크', 'network'], ['🤖 안드로이드', 'android'], ['⚙ 설정', 'settings'],
  ];
  const WIDGETS = [
    ['🕒 시계', 'clock'], ['⌨ 터미널', 'terminal'], ['🗂 열린 파일', 'files'], ['📦 패키지', 'packages'], ['📊 작업', 'tasks'],
    ['📞 전화', 'dialer'], ['📝 메모', 'note'], ['📡 네트워크', 'network'], ['🔌 회로', 'circuit'],
  ];
  const OUTPUT_PARTS = [['💡 LED', 'led'], ['🔔 부저', 'buzzer'], ['⚡ 릴레이', 'relay']];
  const VERBS = [['(없음)', 'none'], ['전화 걸기', 'call'], ['앱 열기', 'launch'], ['핀 전환', 'toggle'], ['명령 실행', 'run'], ['알림', 'notify']];

  const C_DESK = 230, C_LAYOUT = 160, C_APP = 200, C_CIRCUIT = 20, C_PROP = 60;

  const BLOCKS = [
    {
      type: 'desk_desktop',
      message0: '🖥 바탕화면 %1 모드 %2 배경 %3',
      args0: [
        { type: 'field_input', name: 'NAME', text: 'AlliedID' },
        { type: 'field_dropdown', name: 'MODE', options: [['자동', 'auto'], ['📱 폰', 'phone'], ['🖥 데스크톱', 'desktop']] },
        { type: 'field_input', name: 'WALLPAPER', text: '#14182b' },
      ],
      message1: '%1',
      args1: [{ type: 'input_statement', name: 'BODY', check: 'desk_stmt' }],
      colour: C_DESK,
      tooltip: '바탕화면 한 장. 이 블록 안에 사이드바, 위젯, 회로를 끼워 넣으세요.',
    },
    {
      type: 'desk_sidebar',
      message0: '사이드바 %1',
      args0: [{ type: 'field_dropdown', name: 'SIDE', options: [['왼쪽', 'left'], ['오른쪽', 'right'], ['아래', 'bottom']] }],
      message1: '%1',
      args1: [{ type: 'input_statement', name: 'APPS', check: 'desk_app' }],
      previousStatement: 'desk_stmt', nextStatement: 'desk_stmt',
      colour: C_LAYOUT,
    },
    {
      type: 'desk_app',
      message0: '앱 %1',
      args0: [{ type: 'field_dropdown', name: 'APP', options: APPS }],
      previousStatement: 'desk_app', nextStatement: 'desk_app',
      colour: C_APP,
    },
    {
      type: 'desk_widget',
      message0: '위젯 %1 제목 %2',
      args0: [
        { type: 'field_dropdown', name: 'KIND', options: WIDGETS },
        { type: 'field_input', name: 'TITLE', text: '' },
      ],
      message1: '위치 x %1 y %2 크기 %3 × %4',
      args1: [
        { type: 'field_number', name: 'X', value: 24, min: 0, precision: 8 },
        { type: 'field_number', name: 'Y', value: 24, min: 0, precision: 8 },
        { type: 'field_number', name: 'W', value: 320, min: 80, precision: 8 },
        { type: 'field_number', name: 'H', value: 200, min: 60, precision: 8 },
      ],
      message2: '%1',
      args2: [{ type: 'input_statement', name: 'PROPS', check: 'desk_prop' }],
      previousStatement: 'desk_stmt', nextStatement: 'desk_stmt',
      colour: C_LAYOUT,
      tooltip: '바탕화면에 붙일 위젯. 데스크톱에서는 창, 폰에서는 전체화면 앱이 됩니다.',
    },
    {
      type: 'desk_prop',
      message0: '설정 %1 = %2',
      args0: [
        { type: 'field_input', name: 'KEY', text: 'command' },
        { type: 'field_input', name: 'VALUE', text: 'uname -a' },
      ],
      previousStatement: 'desk_prop', nextStatement: 'desk_prop',
      colour: C_PROP,
    },
    {
      type: 'desk_circuit',
      message0: '🔌 회로도 (Raspberry Pi GPIO)',
      message1: '%1',
      args1: [{ type: 'input_statement', name: 'PARTS', check: 'desk_part' }],
      previousStatement: 'desk_stmt', nextStatement: 'desk_stmt',
      colour: C_CIRCUIT,
    },
    {
      type: 'desk_output',
      message0: '%1 %2 GPIO %3',
      args0: [
        { type: 'field_dropdown', name: 'KIND', options: OUTPUT_PARTS },
        { type: 'field_input', name: 'NAME', text: '상태' },
        { type: 'field_number', name: 'PIN', value: 17, min: 2, max: 27, precision: 1 },
      ],
      previousStatement: 'desk_part', nextStatement: 'desk_part',
      colour: C_CIRCUIT,
    },
    {
      type: 'desk_button',
      message0: '🔘 버튼 %1 GPIO %2',
      args0: [
        { type: 'field_input', name: 'NAME', text: '긴급전화' },
        { type: 'field_number', name: 'PIN', value: 27, min: 2, max: 27, precision: 1 },
      ],
      message1: '누르면 %1 %2',
      args1: [
        { type: 'field_dropdown', name: 'VERB', options: VERBS },
        { type: 'field_input', name: 'ARG', text: '' },
      ],
      previousStatement: 'desk_part', nextStatement: 'desk_part',
      colour: C_CIRCUIT,
      tooltip: '전화 걸기: AlliedID 번호 / 앱 열기: terminal 등 / 핀 전환: GPIO 번호',
    },
  ];

  const toolbox = {
    kind: 'categoryToolbox',
    contents: [
      { kind: 'category', name: '바탕화면', colour: String(C_DESK), contents: [{ kind: 'block', type: 'desk_desktop' }] },
      {
        kind: 'category', name: '배치', colour: String(C_LAYOUT), contents: [
          { kind: 'block', type: 'desk_sidebar' },
          { kind: 'block', type: 'desk_widget' },
          { kind: 'block', type: 'desk_prop' },
        ],
      },
      {
        kind: 'category', name: '앱', colour: String(C_APP),
        contents: APPS.map(([, v]) => ({ kind: 'block', type: 'desk_app', fields: { APP: v } })),
      },
      {
        kind: 'category', name: '회로', colour: String(C_CIRCUIT), contents: [
          { kind: 'block', type: 'desk_circuit' },
          { kind: 'block', type: 'desk_output' },
          { kind: 'block', type: 'desk_button' },
        ],
      },
    ],
  };

  // ── 기본 작업공간 (셸의 DeskLayout.DefaultScript 와 같은 배치) ─────────────
  function chain(blocks) {
    for (let i = blocks.length - 2; i >= 0; i--) blocks[i].next = { block: blocks[i + 1] };
    return blocks[0];
  }
  const app = (a) => ({ type: 'desk_app', fields: { APP: a } });
  const widget = (k, t, x, y, w, h, props) => ({
    type: 'desk_widget',
    fields: { KIND: k, TITLE: t, X: x, Y: y, W: w, H: h },
    inputs: props ? { PROPS: { block: chain(props.map(([key, value]) => ({ type: 'desk_prop', fields: { KEY: key, VALUE: value } }))) } } : {},
  });
  const defaultWorkspace = {
    blocks: {
      languageVersion: 0,
      blocks: [{
        type: 'desk_desktop', x: 20, y: 20,
        fields: { NAME: 'AlliedID', MODE: 'auto', WALLPAPER: '#14182b' },
        inputs: {
          BODY: {
            block: chain([
              {
                type: 'desk_sidebar', fields: { SIDE: 'left' },
                inputs: { APPS: { block: chain(['terminal', 'files', 'packages', 'tasks', 'dialer', 'blocks', 'network', 'android', 'settings'].map(app)) } },
              },
              widget('clock', '', 24, 24, 320, 152),
              widget('network', '네트워크', 24, 192, 320, 232),
              widget('terminal', '터미널', 360, 24, 640, 400, [['command', 'uname -a']]),
              widget('files', '열린 파일', 24, 440, 480, 264),
              widget('tasks', '작업', 520, 440, 480, 264),
            ]),
          },
        },
      }],
    },
  };

  // ── DeskScript 생성기 ────────────────────────────────────────────────
  const quote = (s) => '"' + String(s).replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"';
  const num = (n) => String(Math.round(Number(n) * 100) / 100);
  const isColor = (s) => /^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/.test(s);

  function install(Blockly) {
    Blockly.common.defineBlocksWithJsonArray(BLOCKS);

    const g = new Blockly.CodeGenerator('DeskScript');
    g.INDENT = '  ';
    // 다음 블록을 이어서 생성
    g.scrub_ = function (block, code, thisOnly) {
      const next = block.nextConnection && block.nextConnection.targetBlock();
      return code + (next && !thisOnly ? g.blockToCode(next) : '');
    };

    g.forBlock['desk_desktop'] = (b) => {
      const wp = b.getFieldValue('WALLPAPER');
      return `desktop ${quote(b.getFieldValue('NAME'))} {\n` +
        `  mode ${b.getFieldValue('MODE')};\n` +
        `  wallpaper ${quote(isColor(wp) ? wp : '#14182b')};\n` +
        g.statementToCode(b, 'BODY') +
        '}\n';
    };
    g.forBlock['desk_sidebar'] = (b) => {
      const apps = [];
      for (let a = b.getInputTargetBlock('APPS'); a; a = a.getNextBlock()) {
        if (!a.isEnabled()) continue;
        const v = a.getFieldValue('APP');
        if (!apps.includes(v)) apps.push(v);
      }
      return `sidebar ${b.getFieldValue('SIDE')} { ${apps.map((a) => a + ';').join(' ')} }\n`;
    };
    g.forBlock['desk_app'] = () => ''; // 사이드바가 직접 읽는다
    g.forBlock['desk_widget'] = (b) => {
      const title = b.getFieldValue('TITLE');
      const props = g.statementToCode(b, 'PROPS').trim();
      return `widget ${b.getFieldValue('KIND')}${title ? ' ' + quote(title) : ''}` +
        ` at (${num(b.getFieldValue('X'))}, ${num(b.getFieldValue('Y'))})` +
        ` size (${num(b.getFieldValue('W'))}, ${num(b.getFieldValue('H'))})` +
        (props ? ` { ${props.split('\n').map((l) => l.trim()).join(' ')} }` : ';') + '\n';
    };
    g.forBlock['desk_prop'] = (b) => `${b.getFieldValue('KEY').replace(/[^a-zA-Z0-9_.-]/g, '_') || 'key'} ${quote(b.getFieldValue('VALUE'))};\n`;
    g.forBlock['desk_circuit'] = (b) => `circuit {\n${g.statementToCode(b, 'PARTS')}}\n`;
    g.forBlock['desk_output'] = (b) => `${b.getFieldValue('KIND')} ${quote(b.getFieldValue('NAME'))} pin ${b.getFieldValue('PIN')};\n`;
    g.forBlock['desk_button'] = (b) => {
      const verb = b.getFieldValue('VERB');
      const arg = b.getFieldValue('ARG');
      const action = verb === 'none' ? '' : ` -> ${verb}${arg ? ' ' + quote(arg) : ''}`;
      return `button ${quote(b.getFieldValue('NAME'))} pin ${b.getFieldValue('PIN')}${action};\n`;
    };

    /** 작업공간 → DeskScript (첫 번째 바탕화면 블록). 없으면 null. */
    function generate(workspace) {
      g.init(workspace);
      const top = workspace.getTopBlocks(true).find((b) => b.type === 'desk_desktop' && b.isEnabled());
      if (!top) return null;
      return g.finish(g.blockToCode(top, true)).trimEnd() + '\n';
    }

    /** 미리보기용 요약 (위젯 사각형, 회로 부품). */
    function summarize(workspace) {
      const top = workspace.getTopBlocks(true).find((b) => b.type === 'desk_desktop');
      const out = { name: '', wallpaper: '#14182b', widgets: [], parts: [], warnings: [] };
      if (!top) return out;
      out.name = top.getFieldValue('NAME');
      out.wallpaper = isColor(top.getFieldValue('WALLPAPER')) ? top.getFieldValue('WALLPAPER') : '#14182b';
      if (!isColor(top.getFieldValue('WALLPAPER'))) out.warnings.push('배경색은 #RRGGBB 형식이어야 합니다');
      const pins = new Map();
      for (const b of top.getDescendants(true)) {
        if (b.type === 'desk_widget') {
          out.widgets.push({ kind: b.getFieldValue('KIND'), title: b.getFieldValue('TITLE'), x: +b.getFieldValue('X'), y: +b.getFieldValue('Y'), w: +b.getFieldValue('W'), h: +b.getFieldValue('H') });
        } else if (b.type === 'desk_output' || b.type === 'desk_button') {
          const pin = +b.getFieldValue('PIN');
          const kind = b.type === 'desk_button' ? 'button' : b.getFieldValue('KIND');
          if (pins.has(pin)) out.warnings.push(`GPIO ${pin} 을(를) ${pins.get(pin)} 와(과) 함께 쓰고 있습니다`);
          pins.set(pin, b.getFieldValue('NAME'));
          out.parts.push({ kind, name: b.getFieldValue('NAME'), pin, verb: b.type === 'desk_button' ? b.getFieldValue('VERB') : null });
        }
      }
      return out;
    }

    return { generator: g, generate, summarize, toolbox, defaultWorkspace };
  }

  return { install, BLOCKS, quote };
});
