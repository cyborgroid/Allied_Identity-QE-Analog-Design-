// node --test test/  — 블록 → DeskScript 생성 검증 (헤드리스 Blockly)
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { writeFileSync, mkdirSync } from 'node:fs';

const require = createRequire(import.meta.url);
const Blockly = require('blockly');
const DeskBlocks = require('../src/deskblocks.js');
const kit = DeskBlocks.install(Blockly);

function workspaceFrom(json) {
  const ws = new Blockly.Workspace();
  Blockly.serialization.workspaces.load(json, ws);
  return ws;
}

test('default workspace generates the shell default desk', () => {
  const script = kit.generate(workspaceFrom(kit.defaultWorkspace));
  assert.match(script, /^desktop "AlliedID" \{/);
  assert.match(script, /  mode auto;/);
  assert.match(script, /sidebar left \{ terminal; files; packages; tasks; dialer; blocks; network; android; settings; \}/);
  assert.match(script, /widget terminal "터미널" at \(360, 24\) size \(640, 400\) \{ command "uname -a"; \}/);
  assert.match(script, /widget clock at \(24, 24\) size \(320, 152\);/);
  // C# 쪽 교차 검증용 산출물
  mkdirSync(new URL('./out/', import.meta.url), { recursive: true });
  writeFileSync(new URL('./out/default.aid', import.meta.url), script);
});

test('circuit blocks generate GPIO parts and actions', () => {
  const ws = workspaceFrom(kit.defaultWorkspace);
  const desk = ws.getTopBlocks()[0];
  const circuit = ws.newBlock('desk_circuit');
  const led = ws.newBlock('desk_output');
  const btn = ws.newBlock('desk_button');
  btn.setFieldValue('call', 'VERB');
  btn.setFieldValue('436846882187', 'ARG');
  btn.setFieldValue('Say "hi"', 'NAME');
  circuit.getInput('PARTS').connection.connect(led.previousConnection);
  led.nextConnection.connect(btn.previousConnection);
  // 바탕화면 BODY 의 마지막에 붙인다
  let last = desk.getInputTargetBlock('BODY');
  while (last.getNextBlock()) last = last.getNextBlock();
  last.nextConnection.connect(circuit.previousConnection);

  const script = kit.generate(ws);
  assert.match(script, /circuit \{\n    led "상태" pin 17;\n    button "Say \\"hi\\"" pin 27 -> call "436846882187";\n  \}/);
  writeFileSync(new URL('./out/circuit.aid', import.meta.url), script);

  const sum = kit.summarize(ws);
  assert.equal(sum.parts.length, 2);
  assert.equal(sum.widgets.length, 5);
  assert.deepEqual(sum.warnings, []);
  btn.setFieldValue(17, 'PIN');
  assert.match(kit.summarize(ws).warnings[0], /GPIO 17/);
});

test('no desktop block → null', () => {
  assert.equal(kit.generate(new Blockly.Workspace()), null);
});
