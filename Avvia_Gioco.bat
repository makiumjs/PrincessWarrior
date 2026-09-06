@echo off
cd /d "%~dp0"
start "" "%~dp0tools\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path "%~dp0game"
