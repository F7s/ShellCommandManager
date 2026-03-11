@echo off
setlocal
for /f "tokens=2,*" %%A in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul ^| find /I "Path"') do set "MACHINE_PATH=%%B"
for /f "tokens=2,*" %%A in ('reg query "HKCU\Environment" /v Path 2^>nul ^| find /I "Path"') do set "USER_PATH=%%B"
if defined MACHINE_PATH (
  if defined USER_PATH (
    set "PATH=%MACHINE_PATH%;%USER_PATH%;%PATH%"
  ) else (
    set "PATH=%MACHINE_PATH%;%PATH%"
  )
)
set "PATH=%PATH%;%LOCALAPPDATA%\Microsoft\WindowsApps"
start "" "%~dp0ShellCommandManager.exe"
endlocal
