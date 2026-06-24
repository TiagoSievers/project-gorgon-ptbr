"""Instalação Project Gorgon PT-BR no Windows (BepInEx + plugin + language pack)."""
from __future__ import annotations

import os
import shutil
import sys
import zipfile
from collections.abc import Callable
from dataclasses import dataclass, field
from pathlib import Path

GAME_FOLDER = "Project Gorgon"
STUDIO_FOLDER = "Elder Game"
UNINSTALLER_WIN_EXE = "uninstall-language-pack-ptbr.exe"
UNINSTALLER_LINUX = "uninstall-language-pack-ptbr"


@dataclass
class InstallResult:
    ok: bool
    log: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)

    def info(self, msg: str) -> None:
        self.log.append(msg)

    def fail(self, msg: str) -> None:
        self.errors.append(msg)
        self.log.append(f"ERRO: {msg}")


ProgressFn = Callable[[int, str], None]


def _noop_progress(_pct: int, _msg: str) -> None:
    pass


def find_steam_root() -> Path | None:
    if sys.platform != "win32":
        return None
    try:
        import winreg  # type: ignore[import-untyped]
    except ImportError:
        return None

    candidates: list[tuple[int, str]] = [
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam"),
        (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Valve\Steam"),
        (winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam"),
    ]
    for hive, subkey in candidates:
        try:
            with winreg.OpenKey(hive, subkey) as key:
                value, _ = winreg.QueryValueEx(key, "InstallPath")
                path = Path(str(value))
                if path.is_dir():
                    return path
        except OSError:
            continue
    return None


def detect_game_dir() -> Path | None:
    steam = find_steam_root()
    if steam:
        game = steam / "steamapps" / "common" / GAME_FOLDER
        if game.is_dir():
            return game

    for base in (
        Path(r"C:\Program Files (x86)\Steam"),
        Path(r"C:\Program Files\Steam"),
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Steam",
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Steam",
    ):
        game = base / "steamapps" / "common" / GAME_FOLDER
        if game.is_dir():
            return game
    return None


def uninstaller_path(game_dir: Path) -> Path:
    return game_dir / UNINSTALLER_WIN_EXE


def schedule_self_delete(path: Path) -> None:
    """Agenda exclusão do próprio .exe após o processo encerrar (Windows)."""
    if sys.platform != "win32" or not path.is_file():
        return
    import subprocess

    quoted = str(path)
    subprocess.Popen(
        ["cmd", "/c", f'ping 127.0.0.1 -n 3 >nul & del /f /q "{quoted}"'],
        creationflags=getattr(subprocess, "DETACHED_PROCESS", 0)
        | getattr(subprocess, "CREATE_NO_WINDOW", 0),
        close_fds=True,
    )


def _remove_legacy_uninstallers(game_dir: Path) -> None:
    legacy = [
        game_dir / "BepInEx" / "DESINSTALAR.exe",
        game_dir / "BepInEx" / "DESINSTALAR.bat",
        game_dir / "BepInEx" / "desinstalar_jogo.ps1",
        game_dir / "DESINSTALAR.exe",
    ]
    for path in legacy:
        if path.is_file():
            try:
                path.unlink()
            except OSError:
                pass


def translation_dir() -> Path:
    profile = os.environ.get("USERPROFILE", "")
    return (
        Path(profile)
        / "AppData"
        / "LocalLow"
        / STUDIO_FOLDER
        / GAME_FOLDER
        / "Translation"
    )


def plugin_dir(game_dir: Path) -> Path:
    return game_dir / "BepInEx" / "plugins" / "PgTranslateLive"


def translator_dir(game_dir: Path) -> Path:
    return game_dir / "BepInEx" / "plugins" / "Translator"


def validate_pack(root: Path) -> list[str]:
    missing: list[str] = []
    if not (root / "dist" / "PgTranslateLive.dll").is_file():
        missing.append("dist/PgTranslateLive.dll")
    if not (root / "dist" / "Translator.dll").is_file():
        missing.append("dist/Translator.dll")
    if not (root / "output" / "Translation" / "version.json").is_file():
        missing.append("output/Translation/")
    if not (root / "output" / "pt-BR" / "ui.yaml").is_file():
        missing.append("output/pt-BR/")
    if not (root / "vendor" / "BepInExPack_IL2CPP.zip").is_file():
        missing.append("vendor/BepInExPack_IL2CPP.zip")
    if not (root / "game-uninstall" / UNINSTALLER_WIN_EXE).is_file():
        alt = Path(__file__).resolve().parent / "game-uninstall" / UNINSTALLER_WIN_EXE
        if not alt.is_file():
            missing.append(f"game-uninstall/{UNINSTALLER_WIN_EXE}")
    return missing


def _copy_tree(src: Path, dest: Path) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    for item in src.iterdir():
        target = dest / item.name
        if item.is_dir():
            if target.exists():
                shutil.rmtree(target)
            shutil.copytree(item, target)
        else:
            shutil.copy2(item, target)


def install_bepinex(root: Path, game_dir: Path, result: InstallResult) -> bool:
    winhttp = game_dir / "winhttp.dll"
    core = game_dir / "BepInEx" / "core" / "BepInEx.Unity.IL2CPP.dll"
    if winhttp.is_file() and core.is_file():
        result.info("BepInEx já instalado")
        return True

    bundled = root / "vendor" / "BepInExPack_IL2CPP.zip"
    if not bundled.is_file():
        result.fail(f"BepInEx não encontrado: {bundled}")
        return False

    try:
        with zipfile.ZipFile(bundled) as zf:
            if zf.testzip() is not None:
                result.fail("Arquivo BepInEx inválido (zip corrompido)")
                return False
            staging = root / ".cache" / "bepinex-pack-win"
            if staging.exists():
                shutil.rmtree(staging)
            staging.mkdir(parents=True, exist_ok=True)
            zf.extractall(staging)
    except zipfile.BadZipFile:
        result.fail("Arquivo BepInEx inválido")
        return False

    pack = staging / "BepInExPack"
    if not pack.is_dir():
        pack = staging

    for item in pack.iterdir():
        dest = game_dir / item.name
        if item.is_dir():
            shutil.copytree(item, dest, dirs_exist_ok=True)
        else:
            shutil.copy2(item, dest)

    if not winhttp.is_file():
        result.fail("winhttp.dll não encontrado após instalar BepInEx")
        return False

    result.info("BepInEx instalado")
    return True


def install_plugin(root: Path, game_dir: Path, result: InstallResult) -> bool:
    dll_src = root / "dist" / "PgTranslateLive.dll"
    if not dll_src.is_file():
        result.fail("PgTranslateLive.dll não encontrado em dist/")
        return False

    dest_dir = plugin_dir(game_dir)
    cfg_dir = game_dir / "BepInEx" / "config"
    dest_dir.mkdir(parents=True, exist_ok=True)
    cfg_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(dll_src, dest_dir / "PgTranslateLive.dll")

    cfg_src = root / "dist" / "com.pg.translatelive.cfg"
    if cfg_src.is_file():
        shutil.copy2(cfg_src, cfg_dir / "com.pg.translatelive.cfg")

    result.info(f"Plugin: {dest_dir / 'PgTranslateLive.dll'}")
    return True


def install_translator(root: Path, game_dir: Path, result: InstallResult) -> bool:
    dll_src = root / "dist" / "Translator.dll"
    yaml_src = root / "output" / "pt-BR"
    if not dll_src.is_file():
        result.fail("Translator.dll não encontrado em dist/")
        return False
    if not yaml_src.is_dir():
        result.fail("output/pt-BR/ não encontrado")
        return False

    dest_plugin = translator_dir(game_dir)
    dest_yaml = dest_plugin / "translations" / "pt-BR"
    cfg_dir = game_dir / "BepInEx" / "config"
    dest_plugin.mkdir(parents=True, exist_ok=True)
    cfg_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(dll_src, dest_plugin / "Translator.dll")
    _copy_tree(yaml_src, dest_yaml)

    cfg_src = root / "dist" / "com.pickteam.translator.cfg"
    if cfg_src.is_file():
        shutil.copy2(cfg_src, cfg_dir / "com.pickteam.translator.cfg")

    result.info(f"Translator: {dest_plugin / 'Translator.dll'}")
    result.info(f"YAML pt-BR: {dest_yaml}")
    return True


def install_translation_pack(root: Path, result: InstallResult) -> bool:
    src = root / "output" / "Translation"
    if not src.is_dir():
        result.fail("output/Translation/ não encontrado")
        return False

    dest = translation_dir()
    _copy_tree(src, dest)
    result.info(f"Language pack: {dest}")
    return True


def verify_install(game_dir: Path, result: InstallResult) -> bool:
    ok = True
    checks = [
        (game_dir / "winhttp.dll", "BepInEx loader (winhttp.dll)"),
        (game_dir / "dotnet" / "coreclr.dll", "BepInEx CoreCLR (dotnet/coreclr.dll)"),
        (
            game_dir / "BepInEx" / "core" / "BepInEx.Unity.IL2CPP.dll",
            "BepInEx IL2CPP core",
        ),
        (
            plugin_dir(game_dir) / "PgTranslateLive.dll",
            "Plugin PgTranslateLive.dll",
        ),
        (
            translator_dir(game_dir) / "Translator.dll",
            "Mod Translator.dll",
        ),
        (
            translator_dir(game_dir) / "translations" / "pt-BR" / "ui.yaml",
            "Traduções YAML pt-BR (ui.yaml)",
        ),
        (uninstaller_path(game_dir), "Desinstalador na pasta do jogo"),
        (translation_dir() / "version.json", "Language pack PT-BR"),
    ]
    for path, label in checks:
        if path.is_file():
            result.info(f"OK   {label}")
        else:
            result.fail(f"Falha {label}")
            ok = False
    return ok


def _remove_if_exists(path: Path, result: InstallResult, label: str) -> None:
    if not path.exists():
        result.info(f"OK   já ausente: {label}")
        return
    try:
        if path.is_dir():
            shutil.rmtree(path)
        else:
            path.unlink()
        result.info(f"OK   removido: {label} ({path})")
    except OSError as ex:
        result.fail(f"Não foi possível remover {label}: {ex}")


def uninstall_all(
    game_dir: Path,
    progress: ProgressFn | None = None,
) -> InstallResult:
    prog = progress or _noop_progress
    result = InstallResult(ok=False)

    if not game_dir.is_dir():
        result.fail(f"Pasta do jogo não encontrada: {game_dir}")
        return result

    prog(5, "Iniciando desinstalação…")
    result.info(f"Jogo: {game_dir}")

    game_removals = [
        (game_dir / "BepInEx", "BepInEx (plugins e config)"),
        (game_dir / "dotnet", "dotnet (BepInEx CoreCLR)"),
        (game_dir / "winhttp.dll", "winhttp.dll (Doorstop)"),
        (game_dir / "doorstop_config.ini", "doorstop_config.ini"),
        (game_dir / ".doorstop_version", ".doorstop_version"),
    ]

    prog(25, "Removendo BepInEx e plugins…")
    for path, label in game_removals:
        _remove_if_exists(path, result, label)

    prog(60, "Removendo language pack PT-BR…")
    _remove_if_exists(translation_dir(), result, "Language pack PT-BR (AppData)")

    prog(90, "Verificando desinstalação…")
    checks = [
        (game_dir / "winhttp.dll", "winhttp.dll"),
        (plugin_dir(game_dir) / "PgTranslateLive.dll", "PgTranslateLive"),
        (translator_dir(game_dir) / "Translator.dll", "Translator"),
        (translation_dir() / "version.json", "Language pack"),
    ]
    leftover = False
    for path, label in checks:
        if path.exists():
            result.fail(f"Ainda presente: {label} ({path})")
            leftover = True

    if leftover:
        return result

    prog(100, "Desinstalação concluída!")
    result.ok = True
    result.info(">>> Mod PT-BR removido. O jogo volta ao inglês original.")
    return result


def install_game_uninstaller(game_dir: Path, root: Path, result: InstallResult) -> bool:
    """Copia o desinstalador para a pasta do jogo (fora de BepInEx/)."""
    src = root / "game-uninstall"
    if not src.is_dir():
        src = Path(__file__).resolve().parent / "game-uninstall"
    exe = src / UNINSTALLER_WIN_EXE
    if not exe.is_file():
        result.fail(f"game-uninstall/ ausente no pacote ({UNINSTALLER_WIN_EXE})")
        return False

    _remove_legacy_uninstallers(game_dir)
    dest_exe = uninstaller_path(game_dir)
    shutil.copy2(exe, dest_exe)
    result.info(f"Desinstalador: {dest_exe}")
    return True


def build_success_message(game_dir: Path) -> str:
    trans = translation_dir()
    plugin = game_dir / "BepInEx"
    return (
        "Instalação concluída!\n\n"
        f"Diretório do plugin:\n{plugin}\n\n"
        f"Diretório CDN Tradução:\n{trans}\n\n"
        "Abra o Project Gorgon pela Steam.\n"
        "Se perguntar, aceite o language pack PT-BR.\n\n"
        "UI (Talk, Buy, etc.): mod Translator + output/pt-BR.\n"
        "Diálogo Falar (NPC): precisa de internet (Google Translate).\n\n"
        f"Para desinstalar depois: {uninstaller_path(game_dir)}\n\n"
        "Clique OK para ver o relatório técnico completo."
    )


def build_uninstall_message(result: InstallResult) -> str:
    removed: list[str] = []
    absent: list[str] = []
    for line in result.log:
        if "OK   removido:" in line:
            removed.append(line.split("OK   removido:", 1)[1].strip())
        elif "OK   já ausente:" in line:
            absent.append(line.split("OK   já ausente:", 1)[1].strip())

    parts = ["Desinstalação concluída!\n"]
    if removed:
        parts.append("Removido:")
        parts.extend(f"• {item}" for item in removed)
        parts.append("")
    if absent:
        parts.append("Já estava ausente (nada a fazer):")
        parts.extend(f"• {item}" for item in absent)
        parts.append("")
    if not removed and not absent:
        parts.append("Nenhum componente do mod foi encontrado.\n")
    parts.append("Abra o jogo pela Steam — estará em inglês.\n")
    parts.append("Este desinstalador será removido automaticamente ao fechar.\n")
    parts.append("Clique OK para ver o relatório técnico completo.")
    return "\n".join(parts)


def install_all(
    root: Path,
    game_dir: Path,
    progress: ProgressFn | None = None,
) -> InstallResult:
    prog = progress or _noop_progress
    result = InstallResult(ok=False)

    missing = validate_pack(root)
    if missing:
        for item in missing:
            result.fail(f"Pacote incompleto — falta: {item}")
        return result

    if not game_dir.is_dir():
        result.fail(f"Pasta do jogo não encontrada: {game_dir}")
        return result

    prog(5, "Iniciando instalação…")
    result.info(f"Jogo: {game_dir}")

    prog(25, "Instalando BepInEx…")
    if not install_bepinex(root, game_dir, result):
        return result

    prog(45, "Instalando plugin PgTranslateLive…")
    if not install_plugin(root, game_dir, result):
        return result

    prog(60, "Instalando Translator + YAML pt-BR…")
    if not install_translator(root, game_dir, result):
        return result

    prog(75, "Copiando language pack PT-BR…")
    if not install_translation_pack(root, result):
        return result

    prog(85, "Instalando desinstalador na pasta do jogo…")
    if not install_game_uninstaller(game_dir, root, result):
        return result

    prog(92, "Verificando instalação…")
    if not verify_install(game_dir, result):
        return result

    prog(100, "Instalação concluída!")
    result.ok = True
    result.info(">>> Pronto! Abra o jogo na Steam.")
    return result
