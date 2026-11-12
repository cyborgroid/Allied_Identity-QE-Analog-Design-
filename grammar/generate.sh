#!/usr/bin/env bash
# DeskScript.g4 → C# 파서 생성 (셸 Core 프로젝트로)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
jar="$here/../tools/antlr-4.13.2-complete.jar"
out="$here/../shell/src/AlliedID.Shell.Core/DeskScript/Generated"
[[ -f "$jar" ]] || curl -fL -o "$jar" https://repo1.maven.org/maven2/org/antlr/antlr4/4.13.2/antlr4-4.13.2-complete.jar
rm -rf "$out" && mkdir -p "$out"
cd "$here"
java -jar "$jar" -Dlanguage=CSharp -visitor -no-listener -package AlliedID.Shell.Core.DeskScript.Generated -o "$out" DeskScript.g4
echo "generated → $out"
ls "$out"
