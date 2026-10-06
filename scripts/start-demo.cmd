@echo off
rem Double-click launcher: bypasses the PowerShell execution policy for this one script. Extra arguments are passed on
rem (e.g. start-demo.cmd -Mode Hotspot -SkipTests).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0start-demo.ps1" %*
