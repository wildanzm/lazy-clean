@echo off
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Windows built-in C# compiler not found.
  exit /b 1
)

call build.bat
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:exe /platform:anycpu ^
  /out:bin\LazyClean.Tests.exe ^
  /r:bin\LazyClean.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  tests\LazyClean.Tests\SelfTest.cs
if errorlevel 1 (
  echo BUILD OF TESTS FAILED
  exit /b 1
)

bin\LazyClean.Tests.exe --selftest
if errorlevel 1 (
  echo.
  echo SCAN TESTS FAILED
  exit /b 1
)

bin\LazyClean.Tests.exe --uismoke
if errorlevel 1 (
  echo.
  echo UI TESTS FAILED
  exit /b 1
)

del /q bin\LazyClean.Tests.exe 2>nul
echo.
echo ALL TESTS PASSED
