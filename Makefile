# AlliedID 26.11.1 LTS
SHELL := /bin/bash
AID   := core/target/debug/aid
SHELL_PROJ := shell/src/AlliedID.Shell/AlliedID.Shell.csproj

.PHONY: help build test test-rust test-dotnet test-blocks grammar dev-identity run run-phone run-desktop blocks \
        artifacts iso vmware rpi5 qemu clean

help:
	@echo "개발:  make build | test | run | run-phone | blocks | dev-identity | grammar"
	@echo "이미지: make artifacts | iso | vmware | rpi5 | qemu"

build:
	cd core && cargo build
	dotnet build shell/AlliedID.Shell.slnx -nologo -v quiet
	cd blocks && { [ -d node_modules ] || npm ci --no-audit --no-fund; } && npm run -s vendor

test: test-rust test-blocks test-dotnet

test-rust:
	cd core && cargo test -q

# blocks 를 먼저: C# 교차 검증 테스트가 blocks/test/out/*.aid 를 읽는다
test-blocks:
	cd blocks && { [ -d node_modules ] || npm ci --no-audit --no-fund; } && npm test

test-dotnet: build
	AID_BIN=$(CURDIR)/$(AID) dotnet test shell/AlliedID.Shell.slnx -nologo

grammar:
	./grammar/generate.sh

# 개발 PC 용 기기 신원 (~/.local/share/alliedid). 실제 기기에서는 첫 부팅 서비스가 만든다.
dev-identity: build
	$(AID) identity init || true
	$(AID) status

run: build
	PATH=$(CURDIR)/core/target/debug:$$PATH dotnet run --project $(SHELL_PROJ) --no-build

run-phone: build
	PATH=$(CURDIR)/core/target/debug:$$PATH dotnet run --project $(SHELL_PROJ) --no-build -- --mode phone

run-desktop: build
	PATH=$(CURDIR)/core/target/debug:$$PATH dotnet run --project $(SHELL_PROJ) --no-build -- --mode desktop

blocks: build
	AID_BIN=$(CURDIR)/$(AID) python3 blocks/server.py

artifacts:
	./os/build.sh artifacts
iso:
	./os/build.sh iso
vmware:
	./os/build.sh vmware
rpi5:
	./os/build.sh rpi5
qemu:
	./os/build.sh qemu

clean:
	cd core && cargo clean
	dotnet clean shell/AlliedID.Shell.slnx -nologo -v quiet
	rm -rf out blocks/test/out
