#!/usr/bin/env python3
"""
AlliedID 조립대 서버 — 127.0.0.1:8626

  GET  /            블록 편집기 (정적 파일)
  GET  /api/desk    현재 desk.aid 와 블록 작업공간(JSON)
  POST /api/desk    {"script": "...", "blocks": {...}} 저장 → 셸이 즉시 재조립

저장은 `aid check desktop.edit` 권한이 있을 때만 허용된다 ("권한이 없으면 문이 돌아가지 않는다").
다른 웹사이트가 localhost 로 요청을 보내는 것을 막기 위해 Host/Origin 을 검사한다.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

MAX_BODY = 512 * 1024


def desk_path() -> Path:
    if os.environ.get("AID_DESK_FILE"):
        return Path(os.environ["AID_DESK_FILE"])
    cfg = Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config")
    return cfg / "alliedid" / "desk.aid"


def can_edit() -> tuple[bool, str]:
    aid = os.environ.get("AID_BIN") or shutil.which("aid")
    if not aid:
        return True, "aid 없음 (개발 모드: 허용)"
    r = subprocess.run([aid, "check", "desktop.edit"], capture_output=True, text=True)
    return r.returncode == 0, (r.stdout or r.stderr).strip()


def atomic_write(path: Path, data: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(data, encoding="utf-8")
    os.replace(tmp, path)


class Handler(SimpleHTTPRequestHandler):
    server_version = "AlliedID-Blocks/26.11.1"

    def _same_origin(self) -> bool:
        port = self.server.server_address[1]
        allowed = {f"127.0.0.1:{port}", f"localhost:{port}"}
        if self.headers.get("Host") not in allowed:
            return False
        origin = self.headers.get("Origin")
        return origin is None or origin in {f"http://{h}" for h in allowed}

    def _json(self, status: int, obj) -> None:
        body = json.dumps(obj, ensure_ascii=False).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if not self._same_origin():
            return self._json(HTTPStatus.FORBIDDEN, {"error": "bad host"})
        if self.path.split("?")[0] == "/api/desk":
            p = desk_path()
            blocks_file = p.with_name("desk.blocks.json")
            script = p.read_text(encoding="utf-8") if p.exists() else None
            try:
                blocks = json.loads(blocks_file.read_text(encoding="utf-8")) if blocks_file.exists() else None
            except json.JSONDecodeError:
                blocks = None
            ok, why = can_edit()
            return self._json(HTTPStatus.OK, {"path": str(p), "script": script, "blocks": blocks, "canEdit": ok, "reason": why})
        return super().do_GET()

    def do_POST(self):
        if not self._same_origin():
            return self._json(HTTPStatus.FORBIDDEN, {"error": "bad origin"})
        if self.path != "/api/desk":
            return self._json(HTTPStatus.NOT_FOUND, {"error": "not found"})
        n = int(self.headers.get("Content-Length") or 0)
        if n <= 0 or n > MAX_BODY:
            return self._json(HTTPStatus.REQUEST_ENTITY_TOO_LARGE, {"error": "body size"})
        try:
            body = json.loads(self.rfile.read(n))
            script = body["script"]
        except (json.JSONDecodeError, KeyError, TypeError):
            return self._json(HTTPStatus.BAD_REQUEST, {"error": "expected {script, blocks}"})
        if not isinstance(script, str) or not script.lstrip().startswith("desktop"):
            return self._json(HTTPStatus.BAD_REQUEST, {"error": "DeskScript 는 'desktop' 으로 시작해야 합니다"})
        ok, why = can_edit()
        if not ok:
            return self._json(HTTPStatus.FORBIDDEN, {"error": why or "desktop.edit 권한이 없습니다"})
        p = desk_path()
        atomic_write(p.with_name("desk.blocks.json"), json.dumps(body.get("blocks"), ensure_ascii=False))
        atomic_write(p, script)  # 셸은 desk.aid 를 감시한다 → 마지막에 쓴다
        return self._json(HTTPStatus.OK, {"saved": str(p)})

    def log_message(self, fmt, *args):
        sys.stderr.write("blocks: " + (fmt % args) + "\n")


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--port", type=int, default=int(os.environ.get("AID_BLOCKS_PORT", 8626)))
    ap.add_argument("--root", default=str(Path(__file__).resolve().parent), help="정적 파일 위치")
    args = ap.parse_args()
    if not (Path(args.root) / "vendor" / "blockly" / "blockly_compressed.js").exists():
        sys.exit("vendor/blockly 가 없습니다: `npm install && npm run vendor` 를 먼저 실행하세요")
    os.chdir(args.root)
    httpd = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    print(f"AlliedID 조립대: http://127.0.0.1:{args.port}/  →  {desk_path()}", flush=True)
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
