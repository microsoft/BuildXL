@echo off
echo Preparing to build.

call %~dp0\Init.cmd %*
if %ERRORLEVEL% NEQ 0 (
	echo %~dp0\Init.cmd FAILED: %ERRORLEVEL%
	exit /b %ERRORLEVEL%
)

setlocal

set BxlAnalyzerPath=%BUILDXL_LKG%

if "%1" == "-usedev" (
	set BxlAnalyzerPath=%~dp0%..\..\out\selfhost\dev
	shift
)

echo.
REM Runing the XLG BuildXL analyzer: %BxlAnalyzerPath%\bxlanalyzer.exe
echo.
%BxlAnalyzerPath%\bxlanalyzer.exe %1 %2 %3 %4 %5 %6 %7 %8 %9
if %ERRORLEVEL% NEQ 0 (
    exit /b %ERRORLEVEL%
)

endlocal

exit /b 0