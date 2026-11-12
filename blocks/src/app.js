/* AlliedID 조립대 — 편집기 화면 */
(function () {
  'use strict';
  const kit = DeskBlocks.install(Blockly);
  const $ = (id) => document.getElementById(id);
  const SVG = 'http://www.w3.org/2000/svg';
  const ICONS = { clock: '🕒', terminal: '⌨', files: '🗂', packages: '📦', tasks: '📊', dialer: '📞', note: '📝', network: '📡', circuit: '🔌',
    blocks: '🧩', android: '🤖', settings: '⚙' };

  const theme = Blockly.Theme.defineTheme('alliedid-dark', {
    base: Blockly.Themes.Classic,
    componentStyles: {
      workspaceBackgroundColour: '#10132a', toolboxBackgroundColour: '#0e1122', toolboxForegroundColour: '#e8eaf6',
      flyoutBackgroundColour: '#1c2140', flyoutForegroundColour: '#c9cce6', flyoutOpacity: 1,
      scrollbarColour: '#3a4170', insertionMarkerColour: '#ffffff', insertionMarkerOpacity: 0.3, cursorColour: '#d0d0d0',
    },
  });

  const ws = Blockly.inject($('blockly'), {
    toolbox: kit.toolbox,
    media: 'vendor/blockly/media/',
    renderer: 'zelos',
    theme,
    grid: { spacing: 24, length: 3, colour: '#22284d', snap: true },
    zoom: { controls: true, wheel: true, startScale: 0.8 },
    trashcan: true,
  });

  let canEdit = true;
  let online = location.protocol.startsWith('http');

  function toast(msg) {
    const t = $('toast');
    t.textContent = msg;
    t.classList.add('show');
    clearTimeout(toast.timer);
    toast.timer = setTimeout(() => t.classList.remove('show'), 2600);
  }

  function svg(tag, attrs, parent, text) {
    const el = document.createElementNS(SVG, tag);
    for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
    if (text != null) el.textContent = text;
    parent.appendChild(el);
    return el;
  }

  function renderPreview(sum) {
    const d = $('preview-desktop');
    d.replaceChildren();
    d.style.background = sum.wallpaper;
    svg('rect', { x: 0, y: 0, width: 64, height: 800, fill: '#0e1122', opacity: 0.8 }, d);
    for (const w of sum.widgets) {
      const g = svg('g', { transform: `translate(${64 + w.x},${34 + w.y})` }, d);
      svg('rect', { width: w.w, height: w.h, rx: 12, fill: '#1c2140', stroke: '#2fbf8a55', 'stroke-width': 2 }, g);
      svg('rect', { width: w.w, height: 32, rx: 12, fill: '#262c52' }, g);
      svg('text', { x: 14, y: 22, fill: '#e8eaf6', 'font-size': 16 }, g, `${ICONS[w.kind] || '▫'} ${w.title || w.kind}`);
    }
    $('deskname').textContent = sum.name;

    const p = $('preview-phone');
    p.replaceChildren();
    p.style.background = sum.wallpaper;
    const apps = [...new Set(sum.widgets.map((w) => w.kind))];
    for (const a of apps.slice(0, 9)) {
      const s = document.createElement('span');
      s.textContent = ICONS[a] || '▫';
      s.title = a;
      p.appendChild(s);
    }

    const warn = $('warnings');
    warn.replaceChildren(...sum.warnings.map((m) => Object.assign(document.createElement('li'), { textContent: m })));
  }

  /** Raspberry Pi 40핀 헤더 일부 + 연결된 부품을 그린다. */
  function renderSchematic(parts) {
    const s = $('schematic');
    s.replaceChildren();
    const h = Math.max(200, 40 + parts.length * 34);
    s.setAttribute('viewBox', `0 0 360 ${h}`);
    svg('rect', { x: 16, y: 16, width: 92, height: h - 32, rx: 8, fill: '#0e5c3f' }, s);
    svg('text', { x: 62, y: 36, fill: '#d8ffe9', 'font-size': 12, 'text-anchor': 'middle' }, s, 'Pi 5 GPIO');
    if (parts.length === 0) {
      svg('text', { x: 130, y: 60, fill: '#8b90b8', 'font-size': 13 }, s, '회로 블록을 추가하세요');
      return;
    }
    parts.forEach((p, i) => {
      const y = 60 + i * 34;
      svg('circle', { cx: 100, cy: y, r: 5, fill: '#f5d36b' }, s);
      svg('text', { x: 92, y: y + 4, fill: '#d8ffe9', 'font-size': 11, 'text-anchor': 'end' }, s, `GPIO${p.pin}`);
      svg('line', { x1: 105, y1: y, x2: 170, y2: y, stroke: '#f5d36b', 'stroke-width': 2 }, s);
      const icon = { led: '💡', buzzer: '🔔', relay: '⚡', button: '🔘' }[p.kind] || '▫';
      svg('text', { x: 176, y: y + 5, fill: '#e8eaf6', 'font-size': 14 }, s, `${icon} ${p.name}`);
      if (p.verb && p.verb !== 'none') svg('text', { x: 176, y: y + 19, fill: '#8b90b8', 'font-size': 10 }, s, `→ ${p.verb}`);
      svg('line', { x1: 340, y1: y - 8, x2: 340, y2: y + 8, stroke: '#8b90b8' }, s); // GND
    });
  }

  function refresh() {
    const script = kit.generate(ws);
    $('code').textContent = script ?? '// "바탕화면" 블록이 필요합니다';
    const sum = kit.summarize(ws);
    renderPreview(sum);
    renderSchematic(sum.parts);
    $('save').disabled = !script || !canEdit;
    return script;
  }

  let timer;
  ws.addChangeListener((e) => {
    if (e.isUiEvent) return;
    clearTimeout(timer);
    timer = setTimeout(refresh, 120);
  });

  async function load() {
    let state = null;
    if (online) {
      try {
        const r = await fetch('api/desk');
        const j = await r.json();
        state = j.blocks;
        canEdit = j.canEdit;
        const perm = $('perm');
        perm.textContent = canEdit ? '🔓 desktop.edit' : '🔒 desktop.edit 권한 없음';
        perm.className = 'pill ' + (canEdit ? 'ok' : 'no');
        perm.title = j.path;
      } catch {
        online = false;
      }
    }
    if (!online) {
      $('perm').textContent = '오프라인 (내려받기만 가능)';
      canEdit = false;
    }
    Blockly.serialization.workspaces.load(state || kit.defaultWorkspace, ws);
    refresh();
  }

  $('save').addEventListener('click', async () => {
    const script = refresh();
    if (!script) return;
    try {
      const r = await fetch('api/desk', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ script, blocks: Blockly.serialization.workspaces.save(ws) }),
      });
      const j = await r.json();
      toast(r.ok ? '✓ 저장됨 — 바탕화면이 다시 조립됩니다' : `✗ ${j.error}`);
    } catch (e) {
      toast(`✗ 저장 실패: ${e.message}`);
    }
  });

  $('download').addEventListener('click', () => {
    const script = refresh();
    if (!script) return;
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([script], { type: 'text/plain' }));
    a.download = 'desk.aid';
    a.click();
    URL.revokeObjectURL(a.href);
  });

  $('reset').addEventListener('click', () => {
    ws.clear();
    Blockly.serialization.workspaces.load(kit.defaultWorkspace, ws);
    refresh();
    toast('기본 바탕화면 블록으로 되돌렸습니다 (저장 전까지 적용되지 않음)');
  });

  window.addEventListener('resize', () => Blockly.svgResize(ws));
  load();
})();
