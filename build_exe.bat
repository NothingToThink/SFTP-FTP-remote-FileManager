@echo off
setlocal
cd /d "%~dp0"

py -m pip install -r requirements.txt

py -m PyInstaller --noconfirm --clean --onefile --windowed --name FileManager GUI.py
py -m PyInstaller --noconfirm --clean --onefile --console --add-data "api.json;." --add-data "docs.html;." --name LocalServer local_server.py

echo.
echo Build complete:
echo GUI: %~dp0dist\FileManager.exe
echo LocalServer: %~dp0dist\LocalServer.exe
endlocal
