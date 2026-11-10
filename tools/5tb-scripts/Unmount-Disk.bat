@echo off
title Unmounting Cloud-SSD (Z:)
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Unmount-Disk.ps1"
