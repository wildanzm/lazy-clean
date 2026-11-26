@echo off
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Windows built-in C# compiler not found.
  exit /b 1
)

if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu ^
  /win32icon:assets\app.ico ^
  /win32manifest:src\LazyClean\Properties\app.manifest ^
  /out:bin\LazyClean.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  src\LazyClean\Program.cs ^
  src\LazyClean\Properties\AssemblyInfo.cs ^
  src\LazyClean\Core\Models.cs ^
  src\LazyClean\Core\Targets.cs ^
  src\LazyClean\Core\ViewLogic.cs ^
  src\LazyClean\Core\ScanEngine.cs ^
  src\LazyClean\UI\Theme.cs ^
  src\LazyClean\UI\MainForm.cs

if errorlevel 1 (
  echo.
  echo BUILD FAILED
  exit /b 1
)
echo BUILD OK: bin\LazyClean.exe
