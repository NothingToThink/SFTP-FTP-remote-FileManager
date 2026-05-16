@echo off
setlocal
cd /d "%~dp0"

py -m pip install -r requirements.txt
py -m PyInstaller --noconfirm --clean --onefile --windowed --name FileManager GUI.py

echo.
echo Build complete:
echo %~dp0dist\FileManager.exe
endlocal
