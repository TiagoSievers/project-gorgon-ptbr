@echo off
setlocal
cd /d "%~dp0"

if exist "INSTALAR.exe" (
    start "" "%~dp0INSTALAR.exe"
    exit /b 0
)

where pythonw >nul 2>&1
if %errorlevel%==0 (
    start "" pythonw "%~dp0installer\pg_ptbr_installer_windows.py"
    exit /b 0
)

where python >nul 2>&1
if %errorlevel%==0 (
    start "" python "%~dp0installer\pg_ptbr_installer_windows.py"
    exit /b 0
)

echo Erro: INSTALAR.exe nao encontrado e Python nao esta instalado.
echo Extraia o pacote completo ou use INSTALAR.exe
pause
exit /b 1
