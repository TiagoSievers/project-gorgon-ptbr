@echo off
chcp 65001 >nul
title Remover pasta BepInEx vazia

set "BEP=C:\Program Files (x86)\Steam\steamapps\common\Project Gorgon\BepInEx"

echo.
echo Esta pasta BepInEx esta VAZIA (mod ja foi removido).
echo O Windows so nao apaga enquanto o Explorador esta aberto nela.
echo.
echo Feche a janela do Explorador em "Project Gorgon" e pressione uma tecla...
pause >nul

if not exist "%BEP%" (
    echo Pasta ja nao existe. Pronto.
    pause
    exit /b 0
)

rd /s /q "%BEP%" 2>nul
if exist "%BEP%" (
    echo.
    echo Ainda nao foi possivel apagar.
    echo Feche TODAS as janelas do Explorador nessa pasta e execute de novo.
    echo.
    pause
    exit /b 1
)

echo.
echo Pasta BepInEx removida com sucesso.
echo Pode testar o instalador do zero.
echo.
pause
