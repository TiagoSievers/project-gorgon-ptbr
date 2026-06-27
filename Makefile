# Project Gorgon — PT-BR (plugin Talk + pipeline CDN)
# Uso: make help

.DEFAULT_GOAL := help

ROOT            := $(CURDIR)
HOME            := $(HOME)
PYTHON          ?= python3
STEAM_COMMON    ?= $(HOME)/.steam/debian-installation/steamapps/common
GAME_DIR        ?= $(STEAM_COMMON)/Project Gorgon
UNITY_CONFIG_DIR ?= $(HOME)/.config/unity3d/Elder Game/Project Gorgon
STEAM_APP_ID_FULL  ?= 342940
STEAM_APP_ID_DEMO  ?= 969170
STEAM_APP_ID       ?= $(STEAM_APP_ID_FULL)
PROTON_BASE        ?= $(HOME)/.steam/debian-installation/steamapps/compatdata
PROTON_SUFFIX      ?= pfx/drive_c/users/steamuser/AppData/LocalLow/Elder Game/Project Gorgon
PROTON_PREFIX      ?= $(PROTON_BASE)/$(STEAM_APP_ID)/$(PROTON_SUFFIX)
PROTON_PREFIX_FULL ?= $(PROTON_BASE)/$(STEAM_APP_ID_FULL)/$(PROTON_SUFFIX)
PROTON_PREFIX_DEMO ?= $(PROTON_BASE)/$(STEAM_APP_ID_DEMO)/$(PROTON_SUFFIX)
LIVE_PLUGIN_DIR ?= $(GAME_DIR)/BepInEx/plugins/PgTranslateLive
LIVE_PLUGIN_CFG ?= $(GAME_DIR)/BepInEx/config/com.pg.translatelive.cfg
TRANSLATOR_DIR  ?= $(GAME_DIR)/BepInEx/plugins/Translator/translations/pt-BR
OFFICIAL_DIR       ?= $(PROTON_PREFIX)/Translation
OFFICIAL_DIR_FULL  ?= $(PROTON_PREFIX_FULL)/Translation
OFFICIAL_DIR_DEMO  ?= $(PROTON_PREFIX_DEMO)/Translation
OFFICIAL_DIR_LINUX ?= $(UNITY_CONFIG_DIR)/Translation
PLUGIN_PROJECT  ?= $(ROOT)/bepinex-plugin/PgTranslateLive/PgTranslateLive.csproj
PLUGIN_DLL      ?= $(ROOT)/bepinex-plugin/PgTranslateLive/bin/Release/net6.0/PgTranslateLive.dll
DIST_DLL        ?= $(ROOT)/dist/PgTranslateLive.dll
DIST_CFG        ?= $(ROOT)/dist/com.pg.translatelive.cfg
RELEASES_DIR    ?= $(ROOT)/releases
DOTNET          ?= $(HOME)/.dotnet/dotnet
MOD_DL_DIR      ?= $(ROOT)/.cache/mod-downloads
TRANSLATE_WORKERS ?= 16
TRANSLATE_DELAY   ?= 0

BEPINEX_URL ?= https://thunderstore.io/package/download/BepInEx/BepInExPack_IL2CPP/6.0.755/

.PHONY: help paths build-plugin configure-live-plugin install-plugin
.PHONY: install sync-dist release-pack verify-install build-installer fetch-bepinex-vendor pack
.PHONY: pack-windows release-pack-windows build-windows-exe
.PHONY: install-bepinex-proton verify-bepinex-proton setup-mod-proton
.PHONY: clean-bepinex-linux steam-proton-hint
.PHONY: fetch extract translate translate-priority write pipeline serve merge-npcs
.PHONY: install-translation install-official

help: ## Mostra esta ajuda
	@echo "Project Gorgon — PT-BR"
	@echo ""
	@echo "Jogador / instalação:"
	@echo "  make pack               Monta pack/pg-ptbr/ (TUDO numa pasta — testar/copiar)"
	@echo "  cd pack/pg-ptbr && ./Instalar-PTBR   Instalar (dois cliques)"
	@echo "  ./install.sh            Terminal (na raiz do repo dev)"
	@echo "  make release-pack       → releases/Project-Gorgon-PT-BR-v*-Linux.zip"
	@echo "  make pack-windows       Monta pack/pg-ptbr-windows/"
	@echo "  make build-windows-exe  Gera INSTALAR.exe (rodar no Windows)"
	@echo "  make release-pack-windows  → releases/Project-Gorgon-PT-BR-v*-Windows.zip"
	@echo ""
	@echo "Plugin (dev):"
	@echo "  make sync-dist          Copia DLL compilada → dist/"
	@echo "  make setup-mod-proton   BepInEx + plugin (compila)"
	@echo "  make install-plugin     Compila e instala PgTranslateLive"
	@echo "  make build-plugin       Só compilar"
	@echo "  make merge-npcs         Mescla npcs.yaml do jogo → output/pt-BR/"
	@echo ""
	@echo "Pipeline CDN (mantenedor):"
	@echo "  make fetch              Baixa CDN + Translation.zip"
	@echo "  make extract            strings.json"
	@echo "  make translate          Google → translations.json"
	@echo "  make translate-priority Categorias principais"
	@echo "  make write              output/pt-BR + output/Translation"
	@echo "  make pipeline           fetch → extract → translate → write"
	@echo "  make install-translation  YAML → mod Translator"
	@echo "  make install-official     Language pack sideload"
	@echo "  make serve                Servidor HTTP local (dev/pipeline)"
	@echo ""
	@echo "Outros:"
	@echo "  make steam-proton-hint  Launch Options do Steam"
	@echo "  make paths              Caminhos detectados"
	@echo ""
	@echo "Variáveis:"
	@echo "  GAME_DIR=$(GAME_DIR)"
	@echo "  TRANSLATOR_DIR=$(TRANSLATOR_DIR)"

paths: ## Mostra caminhos usados pelo Makefile
	@echo "ROOT:            $(ROOT)"
	@echo "GAME_DIR:        $(GAME_DIR)"
	@echo "OFFICIAL_FULL:   $(OFFICIAL_DIR_FULL)"
	@echo "OFFICIAL_DEMO:   $(OFFICIAL_DIR_DEMO)"
	@echo "OFFICIAL_LINUX:  $(OFFICIAL_DIR_LINUX)"
	@echo "LIVE_PLUGIN_DIR: $(LIVE_PLUGIN_DIR)"
	@echo "LIVE_PLUGIN_CFG: $(LIVE_PLUGIN_CFG)"
	@echo "TRANSLATOR_DIR:  $(TRANSLATOR_DIR)"
	@echo "PLUGIN_DLL:      $(PLUGIN_DLL)"

fetch: ## Baixa JSON do CDN + Translation.zip
	$(PYTHON) -m src fetch

extract: ## Extrai strings EN → cache/strings.json
	$(PYTHON) -m src extract

translate: ## Traduz via Google (cache/translations.json)
	$(PYTHON) -m src translate --workers $(TRANSLATE_WORKERS) --delay $(TRANSLATE_DELAY)

translate-priority: ## Traduz categorias principais primeiro
	$(PYTHON) -m src translate --categories skills,abilities,ui,items,quests,npcs,effects --workers $(TRANSLATE_WORKERS) --delay $(TRANSLATE_DELAY)

write: ## Gera output/pt-BR/*.yaml e output/Translation/
	$(PYTHON) -m src write

pipeline: fetch extract translate write ## Pipeline completo CDN

serve: ## Servidor HTTP 127.0.0.1:8765 (dev: sync manual do pack)
	$(PYTHON) -m src serve --host 127.0.0.1 --port 8765

install-translation: ## Instala YAML no mod Translator (requer output/pt-BR)
	@test -d "$(ROOT)/output/pt-BR" || { echo "Erro: output/pt-BR não encontrado"; exit 1; }
	@test -d "$(TRANSLATOR_DIR)" || mkdir -p "$(TRANSLATOR_DIR)"
	cp -a "$(ROOT)/output/pt-BR/." "$(TRANSLATOR_DIR)/"
	@echo "Instalado: $(TRANSLATOR_DIR)"

install-official: ## Instala language pack sideload (Proton pago+demo + Linux)
	@test -d "$(ROOT)/output/Translation" || { echo "Erro: output/Translation não encontrado"; exit 1; }
	@mkdir -p "$(OFFICIAL_DIR_FULL)" "$(OFFICIAL_DIR_DEMO)" "$(OFFICIAL_DIR_LINUX)"
	cp -a "$(ROOT)/output/Translation/." "$(OFFICIAL_DIR_FULL)/"
	cp -a "$(ROOT)/output/Translation/." "$(OFFICIAL_DIR_DEMO)/"
	cp -a "$(ROOT)/output/Translation/." "$(OFFICIAL_DIR_LINUX)/"
	@echo "Instalado (Proton pago):  $(OFFICIAL_DIR_FULL)"
	@echo "Instalado (Proton demo): $(OFFICIAL_DIR_DEMO)"
	@echo "Instalado (Linux):       $(OFFICIAL_DIR_LINUX)"

install: ## Instalação completa (BepInEx + plugin + pack PT)
	@chmod +x "$(ROOT)/install.sh" "$(ROOT)/scripts/install.sh" \
	  "$(ROOT)/scripts/verify-install.sh" "$(ROOT)/scripts/install-paths.sh" 2>/dev/null || true
	"$(ROOT)/scripts/install.sh"

verify-install: ## Verifica BepInEx + plugin + pack no jogo
	@chmod +x "$(ROOT)/scripts/verify-install.sh" "$(ROOT)/scripts/install-paths.sh" 2>/dev/null || true
	"$(ROOT)/scripts/verify-install.sh"

sync-dist: ## Copia PgTranslateLive.dll (bin/Release) → dist/
	@mkdir -p "$(ROOT)/dist"
	@test -f "$(PLUGIN_DLL)" || { echo "Erro: compile primeiro — make build-plugin"; exit 1; }
	cp "$(PLUGIN_DLL)" "$(DIST_DLL)"
	@echo "dist: $(DIST_DLL)"

release-pack: pack ## Compacta pack → releases/Project-Gorgon-PT-BR-v*-Linux.zip
	@chmod +x "$(ROOT)/scripts/release-pack.sh"
	"$(ROOT)/scripts/release-pack.sh"

pack: ## Monta pack/pg-ptbr/ com tudo (instalador + plugin + tradução + BepInEx)
	@chmod +x "$(ROOT)/scripts/assemble-pack.sh" "$(ROOT)/scripts/validate-bepinex-zip.sh" \
	  "$(ROOT)/scripts/fetch-bepinex-vendor.sh"
	"$(ROOT)/scripts/assemble-pack.sh"

pack-windows: pack ## Monta pack/pg-ptbr-windows/ (INSTALAR.exe ou .bat)
	@chmod +x "$(ROOT)/scripts/assemble-pack-windows.sh"
	"$(ROOT)/scripts/assemble-pack-windows.sh"

build-windows-exe: ## Gera dist/INSTALAR.exe (PowerShell + PyInstaller no Windows)
	@chmod +x "$(ROOT)/scripts/build-windows-installer.sh"
	"$(ROOT)/scripts/build-windows-installer.sh"

release-pack-windows: pack-windows ## Compacta pack → releases/Project-Gorgon-PT-BR-v*-Windows.zip
	@chmod +x "$(ROOT)/scripts/release-pack-windows.sh"
	"$(ROOT)/scripts/release-pack-windows.sh"

build-installer: ## Gera executável gráfico dist/PgPtBr-Installer
	@chmod +x "$(ROOT)/scripts/build-installer.sh"
	"$(ROOT)/scripts/build-installer.sh"

fetch-bepinex-vendor: ## Baixa BepInEx IL2CPP para vendor/ (tarball Release)
	@chmod +x "$(ROOT)/scripts/fetch-bepinex-vendor.sh"
	"$(ROOT)/scripts/fetch-bepinex-vendor.sh"

build-plugin: ## Compila plugin BepInEx PgTranslateLive
	@test -x "$(DOTNET)" || { echo "Erro: dotnet não encontrado em $(DOTNET)"; exit 1; }
	"$(DOTNET)" build "$(PLUGIN_PROJECT)" -c Release -p:GameDir="$(GAME_DIR)"
	@echo "Plugin: $(PLUGIN_DLL)"

merge-npcs: ## Mescla npcs.yaml capturado no jogo → output/pt-BR/npcs.yaml
	@chmod +x "$(ROOT)/scripts/merge-npcs-from-game.sh"
	GAME_DIR="$(GAME_DIR)" "$(ROOT)/scripts/merge-npcs-from-game.sh"

configure-live-plugin: ## Garante cfg (UseGoogle=true, TalkVerbose=true)
	@if [ -f "$(LIVE_PLUGIN_CFG)" ]; then \
		sed -i 's/^UseGoogleFallback = false/UseGoogle = true/' "$(LIVE_PLUGIN_CFG)"; \
		sed -i 's/^UseGoogle = false/UseGoogle = true/' "$(LIVE_PLUGIN_CFG)"; \
		if grep -q '^TimeoutMs =' "$(LIVE_PLUGIN_CFG)"; then \
			sed -i 's/^TimeoutMs = .*/TimeoutMs = 30000/' "$(LIVE_PLUGIN_CFG)"; \
		else \
			sed -i '/^\[General\]/a TimeoutMs = 30000' "$(LIVE_PLUGIN_CFG)"; \
		fi; \
		echo "Config: $(LIVE_PLUGIN_CFG)"; \
	else \
		echo "Cfg será criado na primeira execução do jogo."; \
	fi

install-plugin: build-plugin configure-live-plugin ## Instala PgTranslateLive no jogo
	@test -d "$(GAME_DIR)/BepInEx" || { echo "Erro: BepInEx não encontrado. Rode: make setup-mod-proton"; exit 1; }
	@mkdir -p "$(LIVE_PLUGIN_DIR)"
	cp "$(PLUGIN_DLL)" "$(LIVE_PLUGIN_DIR)/"
	@echo "Instalado: $(LIVE_PLUGIN_DIR)/PgTranslateLive.dll"
	@echo "Launch: WINEDLLOVERRIDES=\"winhttp=n,b\" %command%"

define check_zip
	@file "$1" | grep -q 'Zip archive' || { \
		echo "Erro: download inválido ($1)"; exit 1; \
	}
endef

clean-bepinex-linux: ## Remove BepInEx Linux / híbrido da pasta do jogo
	@echo "Removendo BepInEx de $(GAME_DIR)..."
	@rm -rf "$(GAME_DIR)/BepInEx" "$(GAME_DIR)/dotnet" \
		"$(GAME_DIR)/libdoorstop.so" "$(GAME_DIR)/run_bepinex.sh" \
		"$(GAME_DIR)/winhttp.dll" "$(GAME_DIR)/doorstop_config.ini" \
		"$(GAME_DIR)/.doorstop_version"
	@echo "Limpo."

install-bepinex-proton: ## Instala BepInEx IL2CPP Windows (Proton)
	@test -d "$(GAME_DIR)" || { echo "Erro: jogo não encontrado: $(GAME_DIR)"; exit 1; }
	@mkdir -p "$(MOD_DL_DIR)"
	@echo "Baixando BepInExPack IL2CPP Windows..."
	curl -fsSL "$(BEPINEX_URL)" -o "$(MOD_DL_DIR)/BepInExPack_IL2CPP.zip"
	$(call check_zip,$(MOD_DL_DIR)/BepInExPack_IL2CPP.zip)
	@rm -rf "$(MOD_DL_DIR)/bepinex-pack"
	unzip -q -o "$(MOD_DL_DIR)/BepInExPack_IL2CPP.zip" -d "$(MOD_DL_DIR)/bepinex-pack"
	@if [ -d "$(MOD_DL_DIR)/bepinex-pack/BepInExPack" ]; then \
		cp -a "$(MOD_DL_DIR)/bepinex-pack/BepInExPack/." "$(GAME_DIR)/"; \
	else \
		cp -a "$(MOD_DL_DIR)/bepinex-pack/." "$(GAME_DIR)/"; \
	fi
	@$(MAKE) verify-bepinex-proton

verify-bepinex-proton: ## Confere BepInEx Windows (winhttp.dll)
	@test -f "$(GAME_DIR)/winhttp.dll" || { \
		echo "Erro: winhttp.dll não encontrado em $(GAME_DIR)"; exit 1; \
	}
	@test -f "$(GAME_DIR)/BepInEx/core/BepInEx.Unity.IL2CPP.dll" || { \
		echo "Erro: BepInEx.Unity.IL2CPP.dll não encontrado."; exit 1; \
	}
	@echo "OK — BepInEx Windows + winhttp.dll"

STEAM_LAUNCH_PROTON ?= WINEDLLOVERRIDES="winhttp=n,b" %command%

steam-proton-hint: ## Instruções Steam Proton + Launch Options
	@echo ""
	@echo "=== Steam (Proton + BepInEx) ==="
	@echo "1. Propriedades → Compatibilidade → Forçar Steam Play"
	@echo "2. Launch Options:"
	@echo "   $(STEAM_LAUNCH_PROTON)"
	@echo "3. Abra o jogo e confira BepInEx/LogOutput.log"
	@echo "   Pg Translate Live v0.7.0 - plugin + Google"
	@echo ""

setup-mod-proton: clean-bepinex-linux install-bepinex-proton install-plugin steam-proton-hint ## Setup completo (Proton)
