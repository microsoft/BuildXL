@echo off
setlocal

:: Resharper needs to see a true condition for bond-generated source compilation.
:: These conditions are false by default when Bond targets inject the sources.
set BondGeneratedCompileCondition=true

:: Full path to latest Resharper command line tools.
for /f %%i in ('dir /s /b InspectCode.exe') DO (
    set EXE_PATH=%%i
)

:: Path to VS solution file.
set SLN_PATH=%~dp0..\CloudStore.sln

:: Path to random temporary file containing Resharper command line tool XML output.
set TEMP_DIR_PATH=%TEMP%\CloudStore
if not exist %TEMP_DIR_PATH% mkdir %TEMP_DIR_PATH%
set XML_PATH=%TEMP_DIR_PATH%\%~n0-%RANDOM%.xml

:: Run the Resharper command tool having it put XML result in the temp file.
%EXE_PATH% %SLN_PATH% /output=%XML_PATH%

:: Analyze the XML result.
powershell %~dp0ResharperXmlAnalyze.ps1 %XML_PATH%

endlocal
