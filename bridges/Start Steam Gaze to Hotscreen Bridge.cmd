@echo off
cd /d "%~dp0"
py -3 -c "import sys" >nul 2>&1
if not errorlevel 1 (
  py -3 "%~dp0steamgaze_hotscreen_bridge.py" %*
  goto finished
)

python -c "import sys" >nul 2>&1
if not errorlevel 1 (
  python "%~dp0steamgaze_hotscreen_bridge.py" %*
  goto finished
)

for /d %%D in ("%USERPROFILE%\.pyenv\pyenv-win\versions\*") do if exist "%%~fD\python.exe" (
  "%%~fD\python.exe" "%~dp0steamgaze_hotscreen_bridge.py" %*
  goto finished
)

echo No working Python 3 installation was found.
pause
exit /b 1

:finished
if errorlevel 1 pause
