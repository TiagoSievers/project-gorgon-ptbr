# -*- mode: python ; coding: utf-8 -*-
# Gera INSTALAR.exe — execute no Windows: scripts/build-windows-installer.ps1

block_cipher = None

a = Analysis(
    ["pg_ptbr_installer_windows.py"],
    pathex=["."],
    binaries=[],
    datas=[],
    hiddenimports=["windows_core", "tkinter", "tkinter.ttk", "tkinter.filedialog", "tkinter.messagebox", "tkinter.scrolledtext"],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=[],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.zipfiles,
    a.datas,
    [],
    name="INSTALAR",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=False,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon=None,
)
