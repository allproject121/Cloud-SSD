@echo off
title Mounting Cloud-SSD (Z:)
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Mount-Disk.ps1"
