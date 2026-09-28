@echo off
setlocal
cd /d "%~dp0"
set "PET_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%PET_CSC%" set "PET_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%PET_CSC%" (
  echo .NET Framework compiler was not found.
  exit /b 1
)
if not exist dist mkdir dist
"%PET_CSC%" /nologo /target:winexe /out:dist\StickerDuo.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Xml.dll src\Duo.cs
if errorlevel 1 exit /b 1
copy /y assets\duo.png dist\duo.png >nul
if /i "%~1"=="test" (
  start /wait "" dist\StickerDuo.exe --self-test
  if errorlevel 1 exit /b 1
  type dist\test-results.txt
)
echo Ready: dist\StickerDuo.exe
