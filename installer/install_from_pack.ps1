# Instalador Project Gorgon PT-BR (Windows) - BepInEx + PgTranslateLive + Translator + packs
param(
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Log([string]$Msg) { Write-Host $Msg }

function Test-PackFile([string]$Rel) {
    $p = Join-Path $Root $Rel
    if (-not (Test-Path -LiteralPath $p)) { throw "Pacote incompleto - falta: $Rel" }
}

function Copy-Tree([string]$Src, [string]$Dest) {
    if (Test-Path -LiteralPath $Dest) { Remove-Item -LiteralPath $Dest -Recurse -Force }
    Copy-Item -LiteralPath $Src -Destination $Dest -Recurse -Force
}

function Find-GameDir {
    $paths = @(
        "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam",
        "HKLM:\SOFTWARE\Valve\Steam",
        "HKCU:\Software\Valve\Steam"
    )
    foreach ($key in $paths) {
        try {
            $steam = (Get-ItemProperty -Path $key -Name InstallPath -ErrorAction Stop).InstallPath
            $game = Join-Path $steam "steamapps\common\Project Gorgon"
            if (Test-Path -LiteralPath $game) { return $game }
        } catch {}
    }
    foreach ($base in @("${env:ProgramFiles(x86)}\Steam", "${env:ProgramFiles}\Steam")) {
        $game = Join-Path $base "steamapps\common\Project Gorgon"
        if (Test-Path -LiteralPath $game) { return $game }
    }
    return $null
}

Test-PackFile "dist\PgTranslateLive.dll"
Test-PackFile "dist\Translator.dll"
Test-PackFile "output\Translation\version.json"
Test-PackFile "output\pt-BR\ui.yaml"
Test-PackFile "vendor\BepInExPack_IL2CPP.zip"

if (-not $GameDir) {
    $GameDir = Find-GameDir
}
if (-not $GameDir -or -not (Test-Path -LiteralPath $GameDir)) {
    Add-Type -AssemblyName System.Windows.Forms
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Selecione a pasta Project Gorgon (Steam)"
    if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 1 }
    $GameDir = $dlg.SelectedPath
}

Write-Log "Jogo: $GameDir"

$winhttp = Join-Path $GameDir "winhttp.dll"
$core = Join-Path $GameDir "BepInEx\core\BepInEx.Unity.IL2CPP.dll"
if (-not (Test-Path $winhttp) -or -not (Test-Path $core)) {
    Write-Log "Instalando BepInEx..."
    $staging = Join-Path $env:TEMP "pg-ptbr-bepinex-$(Get-Random)"
    Expand-Archive -LiteralPath (Join-Path $Root "vendor\BepInExPack_IL2CPP.zip") -DestinationPath $staging -Force
    $pack = Join-Path $staging "BepInExPack"
    if (-not (Test-Path $pack)) { $pack = $staging }
    Copy-Item -Path (Join-Path $pack "*") -Destination $GameDir -Recurse -Force
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    if (-not (Test-Path (Join-Path $GameDir "dotnet\coreclr.dll"))) {
        throw "BepInEx incompleto - falta dotnet\coreclr.dll no pacote"
    }
} else {
    Write-Log "BepInEx ja instalado"
}

Write-Log "Instalando PgTranslateLive..."
$pgDir = Join-Path $GameDir "BepInEx\plugins\PgTranslateLive"
New-Item -ItemType Directory -Force -Path $pgDir | Out-Null
Copy-Item -Force (Join-Path $Root "dist\PgTranslateLive.dll") (Join-Path $pgDir "PgTranslateLive.dll")
$cfgDir = Join-Path $GameDir "BepInEx\config"
New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
if (Test-Path (Join-Path $Root "dist\com.pg.translatelive.cfg")) {
    Copy-Item -Force (Join-Path $Root "dist\com.pg.translatelive.cfg") (Join-Path $cfgDir "com.pg.translatelive.cfg")
}

Write-Log "Instalando Translator + output/pt-BR..."
$trDir = Join-Path $GameDir "BepInEx\plugins\Translator"
New-Item -ItemType Directory -Force -Path $trDir | Out-Null
Copy-Item -Force (Join-Path $Root "dist\Translator.dll") (Join-Path $trDir "Translator.dll")
Copy-Tree (Join-Path $Root "output\pt-BR") (Join-Path $trDir "translations\pt-BR")
if (Test-Path (Join-Path $Root "dist\com.pickteam.translator.cfg")) {
    Copy-Item -Force (Join-Path $Root "dist\com.pickteam.translator.cfg") (Join-Path $cfgDir "com.pickteam.translator.cfg")
}

Write-Log "Copiando language pack..."
$trans = Join-Path $env:USERPROFILE "AppData\LocalLow\Elder Game\Project Gorgon\Translation"
Copy-Tree (Join-Path $Root "output\Translation") $trans

$checks = @(
    (Join-Path $GameDir "winhttp.dll"),
    (Join-Path $GameDir "dotnet\coreclr.dll"),
    (Join-Path $GameDir "BepInEx\plugins\PgTranslateLive\PgTranslateLive.dll"),
    (Join-Path $GameDir "BepInEx\plugins\Translator\Translator.dll"),
    (Join-Path $GameDir "BepInEx\plugins\Translator\translations\pt-BR\ui.yaml"),
    (Join-Path $trans "version.json")
)
foreach ($c in $checks) {
    if (-not (Test-Path -LiteralPath $c)) { throw "Verificacao falhou: $c" }
}

Write-Log ""
Write-Log "Instalacao concluida!"
Write-Log "Plugin: $(Join-Path $GameDir 'BepInEx')"
Write-Log "Language pack: $trans"
Write-Log "Abra o jogo na Steam (Launch Options vazias para usar BepInEx)."
