@echo off

rem
rem Please see http://msdn.microsoft.com/en-us/library/windows/desktop/bb787181%28v=vs.85%29.aspx
rem for full documentation about what various registry keys and their values mean.
rem

rem Collect full BuildXL minidumps in case of crash. You need to be an Administrator
rem add these keys.
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\bxl.exe" /v DumpFolder /t REG_EXPAND_SZ /d "%LOCALAPPDATA%\CrashDumps"
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\bxl.exe" /v DumpCount /t REG_DWORD /d 10
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\bxl.exe" /v DumpType /t REG_DWORD /d 2
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\bxl.exe" /v CustomDumpFlags /t REG_DWORD /d 2
