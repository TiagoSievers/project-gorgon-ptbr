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
Set-Location $Root

$Built = Join-Path $Root "installer\dist\INSTALAR.exe"
$Out = Join-Path $Root "dist\INSTALAR.exe"

if (-not (Test-Path $Built)) {
    Write-Error "Falha: $Built nao foi criado"
}

New-Item -ItemType Directory -Force -Path (Join-Path $Root "dist") | Out-Null
Copy-Item -Force $Built $Out

Write-Host ""
Write-Host "OK: $Out"
Write-Host "Proximo passo: make pack-windows && make release-pack-windows"
