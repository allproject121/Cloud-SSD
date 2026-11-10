@echo off
title Cloud-SSD (5TB) Manager
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CloudSSD-Manager.ps1"
