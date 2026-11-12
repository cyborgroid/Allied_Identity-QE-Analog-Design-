#!/usr/bin/env bash
# 호스트에서 실행: AlliedID 구성요소를 두 아키텍처로 빌드 → out/artifacts/{amd64,arm64,blocks}
#   Rust  : 정적 musl 바이너리 (C 툴체인 불필요, rust-lld 링크)
#   C#    : .NET self-contained (OS 에 dotnet 설치 불필요)
#   Blocks: Blockly 오프라인 번들
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/out/artifacts"
log() { printf '\e[1;34m[artifacts]\e[0m %s\n' "$*"; }
mkdir -p "$OUT"/{amd64,arm64}

declare -A RUST=([amd64]=x86_64-unknown-linux-musl [arm64]=aarch64-unknown-linux-musl)
declare -A DOTNET=([amd64]=linux-x64 [arm64]=linux-arm64)

for arch in amd64 arm64; do
  t=${RUST[$arch]}
  rustup target list --installed 2>/dev/null | grep -qx "$t" || rustup target add "$t"
  log "Rust aid ($t)"
  ( cd "$ROOT/core" && CARGO_TARGET_X86_64_UNKNOWN_LINUX_MUSL_LINKER=rust-lld cargo build --release --target "$t" -q )
  install -m 0755 "$ROOT/core/target/$t/release/aid" "$OUT/$arch/aid"

  log "C# alliedid-shell (${DOTNET[$arch]})"
  rm -rf "$OUT/$arch/shell"
  dotnet publish "$ROOT/shell/src/AlliedID.Shell/AlliedID.Shell.csproj" -c Release -r "${DOTNET[$arch]}" \
    --self-contained true -p:PublishTrimmed=false -p:DebugType=none -nologo -v quiet -o "$OUT/$arch/shell"
done

log "Blockly 조립대"
( cd "$ROOT/blocks" && { [ -d node_modules ] || npm ci --no-audit --no-fund; } && npm run -s vendor && npm test --silent >/dev/null )
rm -rf "$OUT/blocks" && mkdir -p "$OUT/blocks"
cp -a "$ROOT/blocks/"{index.html,server.py,src,vendor} "$OUT/blocks/"
cp "$ROOT/blocks/test/out/default.aid" "$OUT/desk.aid"
rm -rf "$OUT/android" && cp -a "$ROOT/android" "$OUT/android"

log "완료"
( cd "$OUT" && du -sh amd64 arm64 blocks && file amd64/aid arm64/aid | sed 's/,.*static/… static/' )
