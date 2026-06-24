# Gera INSTALAR.exe — execute no Windows (PowerShell):
#   powershell -ExecutionPolicy Bypass -File scripts\build-windows-installer.ps1
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

Write-Host "==> Instalando PyInstaller (se necessario)..."
python -m pip install --upgrade pip pyinstaller | Out-Host

Write-Host "==> Gerando INSTALAR.exe..."
Set-Location installer
python -m PyInstaller --clean --noconfirm INSTALAR-windows.spec

Write-Host "==> Gerando uninstall-language-pack-ptbr.exe..."
python -m PyInstaller --clean --noconfirm DESINSTALAR-windows.spec
Set-Location $Root

$Built = Join-Path $Root "installer\dist\INSTALAR.exe"
$BuiltUn = Join-Path $Root "installer\dist\uninstall-language-pack-ptbr.exe"
$Out = Join-Path $Root "dist\INSTALAR.exe"
$OutUn = Join-Path $Root "dist\uninstall-language-pack-ptbr.exe"
$GameUn = Join-Path $Root "installer\game-uninstall"
$GameUnExe = Join-Path $GameUn "uninstall-language-pack-ptbr.exe"

if (-not (Test-Path $Built)) {
    Write-Error "Falha: $Built nao foi criado"
}
if (-not (Test-Path $BuiltUn)) {
    Write-Error "Falha: $BuiltUn nao foi criado"
}

New-Item -ItemType Directory -Force -Path (Join-Path $Root "dist") | Out-Null
New-Item -ItemType Directory -Force -Path $GameUn | Out-Null
Copy-Item -Force $Built $Out
Copy-Item -Force $BuiltUn $OutUn
Copy-Item -Force $BuiltUn $GameUnExe

Write-Host ""
Write-Host "OK: $Out"
Write-Host "OK: $OutUn"
Write-Host "OK: $GameUnExe"
Write-Host "Proximo passo: make pack-windows && make release-pack-windows"
