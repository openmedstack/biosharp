DOTNET ?= dotnet
SOLUTION ?= openmedstack-biosharp.sln
PREATOR_PROJECT ?= src/openmedstack.preator/openmedstack.preator.csproj
CONFIGURATION ?= Release
ARTIFACTS_DIR ?= artifacts
PREATOR_PUBLISH_DIR ?= $(ARTIFACTS_DIR)/preator
CONTAINER_IMAGE ?= preator:local
PYTHON ?= python3
DATA_DIR ?= data
GOOGLE_DRIVE_DATA_URL ?= https://drive.google.com/drive/folders/1XDGsz8ItO_7iKPlEfdUesyyfDLZOgNJF?usp=share_link

CONTAINER_ENGINE := $(shell if command -v docker >/dev/null 2>&1; then echo docker; elif command -v podman >/dev/null 2>&1; then echo podman; fi)

PUBLISH_FLAGS = \
	--self-contained true \
	-p:PublishSingleFile=true \
	-p:EnableCompressionInSingleFile=true \
	-p:PublishTrimmed=true \
	-p:SuppressTrimAnalysisWarnings=true \
	-p:EnableSourceControlManagerQueries=false \
	-p:TrimmerRemoveSymbols=true \
	-p:StripSymbols=true \
	-p:DebugType=None \
	-p:DebugSymbols=false

.DEFAULT_GOAL := build

.PHONY: all build test publish publish-macos-arm64 publish-linux-x64 publish-windows-x64 \
	container-image print-container-engine ensure-container-engine ensure-container-ready \
	ensure-download-python download-data clean

all: clean build test publish container-image

build:
	@$(DOTNET) sln $(SOLUTION) list | sed '1,2d' | while IFS= read -r project; do \
		[ -n "$$project" ] || continue; \
		echo "$(DOTNET) build $$project -c $(CONFIGURATION) --nologo -m:1"; \
		$(DOTNET) build "$$project" -c $(CONFIGURATION) --nologo -m:1 || exit $$?; \
	done

test: download-data
	@$(DOTNET) sln $(SOLUTION) list | sed '1,2d' | grep '^tests/.*\.csproj$$' | while IFS= read -r project; do \
		[ -n "$$project" ] || continue; \
		echo "$(DOTNET) test $$project -c $(CONFIGURATION) --nologo -m:1 -p:EnableSourceControlManagerQueries=false"; \
		$(DOTNET) test "$$project" -c $(CONFIGURATION) --nologo -m:1 -p:EnableSourceControlManagerQueries=false || exit $$?; \
	done

publish: publish-macos-arm64 publish-linux-x64 publish-windows-x64

publish-macos-arm64:
	$(DOTNET) publish $(PREATOR_PROJECT) -c $(CONFIGURATION) -r osx-arm64 -m:1 $(PUBLISH_FLAGS) -o $(PREATOR_PUBLISH_DIR)/osx-arm64

publish-linux-x64:
	$(DOTNET) publish $(PREATOR_PROJECT) -c $(CONFIGURATION) -r linux-x64 -m:1 $(PUBLISH_FLAGS) -o $(PREATOR_PUBLISH_DIR)/linux-x64

publish-windows-x64:
	$(DOTNET) publish $(PREATOR_PROJECT) -c $(CONFIGURATION) -r win-x64 -m:1 $(PUBLISH_FLAGS) -o $(PREATOR_PUBLISH_DIR)/win-x64

print-container-engine:
	@if [ -n "$(CONTAINER_ENGINE)" ]; then \
		echo "$(CONTAINER_ENGINE)"; \
	else \
		echo "No Docker or Podman installation found." >&2; \
		exit 1; \
	fi

ensure-container-engine:
	@if [ -z "$(CONTAINER_ENGINE)" ]; then \
		echo "No Docker or Podman installation found. Install one of them to build the container image." >&2; \
		exit 1; \
	fi

ensure-container-ready: ensure-container-engine
	@if [ "$(CONTAINER_ENGINE)" = "podman" ]; then \
		if ! podman info >/dev/null 2>&1; then \
			echo "Starting Podman machine..."; \
			podman machine start; \
		fi; \
	fi

container-image: ensure-container-ready
	$(CONTAINER_ENGINE) build -t $(CONTAINER_IMAGE) .

ensure-download-python:
	@if ! command -v $(PYTHON) >/dev/null 2>&1; then \
		echo "Python interpreter '$(PYTHON)' was not found. Set PYTHON=<interpreter> or install Python 3." >&2; \
		exit 1; \
	fi
	@if ! $(PYTHON) -m venv --help >/dev/null 2>&1; then \
		echo "Python interpreter '$(PYTHON)' does not support venv creation." >&2; \
		exit 1; \
	fi

download-data: ensure-download-python
	@set -e; \
	VENV_DIR="$(DATA_DIR)/.download-venv"; \
	STAGING_DIR="$(DATA_DIR)/.download-staging"; \
	VENV_PYTHON="$$VENV_DIR/bin/python"; \
	cleanup() { rm -rf "$$VENV_DIR" "$$STAGING_DIR"; }; \
	trap cleanup EXIT; \
	mkdir -p "$(DATA_DIR)"; \
	rm -rf "$$VENV_DIR" "$$STAGING_DIR"; \
	$(PYTHON) -m venv "$$VENV_DIR"; \
	"$$VENV_PYTHON" -m pip install --quiet gdown; \
	"$$VENV_PYTHON" -m gdown --folder --continue "$(GOOGLE_DRIVE_DATA_URL)" -O "$$STAGING_DIR"; \
	"$$VENV_PYTHON" -c "from pathlib import Path; import shutil, sys; src = Path(sys.argv[1]); dst = Path(sys.argv[2]); [((dst / path.relative_to(src)).parent.mkdir(parents=True, exist_ok=True), shutil.move(str(path), str(dst / path.relative_to(src)))) for path in sorted(src.rglob('*')) if path.is_file() and not (dst / path.relative_to(src)).exists()]" "$$STAGING_DIR" "$(DATA_DIR)"

clean:
	rm -rf $(ARTIFACTS_DIR)
