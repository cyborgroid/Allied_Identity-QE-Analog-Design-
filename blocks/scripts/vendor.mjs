// node_modules/blockly → vendor/blockly (오프라인 OS 이미지에 그대로 들어간다)
import { cpSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const src = join(root, 'node_modules', 'blockly');
const dst = join(root, 'vendor', 'blockly');
rmSync(dst, { recursive: true, force: true });
mkdirSync(join(dst, 'msg'), { recursive: true });
for (const f of ['blockly_compressed.js', 'LICENSE']) cpSync(join(src, f), join(dst, f));
cpSync(join(src, 'msg', 'ko.js'), join(dst, 'msg', 'ko.js'));
cpSync(join(src, 'media'), join(dst, 'media'), { recursive: true });
console.log('vendored Blockly →', dst);
