@echo off
set __NAME=%0
if "%1"=="/?" goto :help

:processArgs
set "__ARG=%1"
if "%__ARG%"=="/v" (
    set __VERBOSE=1
    shift
    goto :processArgs
)
if "%__ARG%"=="/c" (
    set __SKIP_CONFIRMATION=1
    shift
    goto :processArgs
)
if "%__ARG:~0,1%"=="/" (
    echo Unknown flag: %1
    echo Display the help with "%__NAME% /?"
    goto :end
)
set "__ARG="


if [%1]==[] GOTO :usage
if [%2] NEQ [] GOTO :usage

setlocal EnableDelayedExpansion
rem We will copy everything to the cmaketools folder
set "OUT=%~df1\Out\Bin\debug\net472\tools\CMakeNinjaPipEnvironment"

IF DEFINED __SKIP_CONFIRMATION GOTO :confirmed


:confirm
echo.
echo We will copy everything to the following directory
echo.
echo           %OUT%
echo.
echo You can skip this confirmation with the ^/c flag.
set /p CONFIRMED="Continue? [Y/N] "
if /I "%CONFIRMED%"=="Y" goto :confirmed
if /I "%CONFIRMED%"=="N" goto :end
goto :confirm

:confirmed
echo Copying the files to %OUT%
echo.
echo.
call :Verbose "[INFO] Copying everything under the environment variable LIB"
call :CopyContentsOfEnvironmentVariable LIB "%OUT%\lib\"

call :Verbose "[INFO] Copying everything under the environment variable INCLUDE"
call :CopyContentsOfEnvironmentVariable INCLUDE "%OUT%\include\"

call :Verbose "[INFO] Copying the Python related contents"
set PYTHON_DESTINATION="%OUT%\python\"
call :CopyFromEnvironmentVariableOrElseExecutable PYTHON_LOCATION python.exe %PYTHON_DESTINATION% "Python" 

call :Verbose "[INFO] Copying the CMake installation folder"
set CMAKE_DESTINATION="%OUT%\cmake\"
if defined CMAKE_LOCATION (
    call :Verbose "Will copy the CMake installation folder from the user-set environment variable CMAKE_LOCATION"
    set SOURCE="%CMAKE_LOCATION%"
) else (
    call where /q cmake.exe
    if !ERRORLEVEL! NEQ 0 (
        call :ReportNotFound cmake.exe CMAKE_DESTINATION "CMake Installation Root"
        exit /b 1
    )
    for /f "usebackq delims=" %%i in (`where /F "cmake.exe"`) do set SOURCE="%%~i\..\.."
)
XCOPY /E /D /y /q %SOURCE% %CMAKE_DESTINATION% > NUL 
IF !ERRORLEVEL! NEQ 0 (
    echo      [ERROR] XCOPY found an error copying %EXECUTABLE% to %DESTINATION%
            exit /b 1
)

call :Verbose "[INFO] Copying the Ninja executable to ninja\"
set NINJA_DESTINATION="%OUT%\ninja\"
call :CopyFileFromEnvironmentVariableOrFromWhere NINJA_EXE_LOCATION ninja.exe %NINJA_DESTINATION% "Ninja" 


call :Verbose "[INFO] Copying the Windows SDK"
set SDK_DESTINATION="%OUT%\sdk\"
call :CopyFromEnvironmentVariableOrElseExecutable SDK_LOCATION mt.exe %SDK_DESTINATION% "Windows SDK" 

call :Verbose "[INFO] Copying the MSVC C++ tools: compiler, linker, etc"
set CPPTOOLS_DESTINATION="%OUT%\cpptools\"
call :CopyFromEnvironmentVariableOrElseExecutable CPPTOOLS_LOCATION cl.exe %CPPTOOLS_DESTINATION% "MSVC C++ Tools" 

echo Finished. Any errors should have been reported. If not, the process was a success. The new files are in %OUT%
endlocal
goto :end


::=======================================================================================================
::====================================== SUBROUTINES ====================================================
::=======================================================================================================

:CopyFromEnvironmentVariableOrElseExecutable
::=======================================================================================================
:: Subroutine: CopyFromEnvironmentVariableOrElseExecutable
::     If the environment variable %1 is set, we try to copy its contents
::     to %3. If not, we try to copy the folder containing the executable %2
::     (calling CopyParentOfExecutable).
::     
:: Arguments:   %1 should hold the name of the environment variable (not the value but the name)   
::              %2 should hold the executable, this will be and argument to where
::              %3 is the destination directory for the files
::              %4 is a display name for the directory (in case of error) 
::=======================================================================================================
setlocal EnableDelayedExpansion
set LOCATION_VARIABLE=%1
set EXECUTABLE="%~2"
set DESTINATION="%~3"
set DESCRIPTION=%4

if defined %LOCATION_VARIABLE% (
    call :Verbose "     Will copy the directory for %DESCRIPTION% from the user-set environment variable %LOCATION_VARIABLE%"
    call :CopyAllContents %DESTINATION% "%%%LOCATION_VARIABLE%%%"
) else (
    call :CopyParentOfExecutable  %EXECUTABLE% %DESTINATION% %DESCRIPTION%
    if !ERRORLEVEL! NEQ 0 (
        call :ReportNotFound %EXECUTABLE% %LOCATION_VARIABLE% %DESCRIPTION%
    )
)
goto :eof
endlocal


::=======================================================================================================
::====================================== SUBROUTINES ====================================================
::=======================================================================================================

:CopyFileFromEnvironmentVariableOrFromWhere
::=======================================================================================================
:: Subroutine: CopyFileFromEnvironmentVariableOrFromWhere
::     If the environment variable %1 is set, we try to copy its contents (a file)
::     to %3. If not, we try to copy the the executable specified in %2
::     
:: Arguments:   %1 should hold the name of the environment variable (not the value but the name)   
::              %2 should hold the executable
::              %3 is the destination directory for the files
::              %4 is a display name for the directory (in case of error) 
::=======================================================================================================
setlocal EnableDelayedExpansion
set LOCATION_VARIABLE=%1
set EXECUTABLE="%~2"
set DESTINATION="%~3"
set DESCRIPTION=%4

if defined %LOCATION_VARIABLE% (
    call :Verbose "Will copy the directory for %DESCRIPTION% from the user-set environment variable %LOCATION_VARIABLE%"
    set SOURCE="%%%LOCATION_VARIABLE%%%"
) else (
    call where /Q %EXECUTABLE%
    if !ERRORLEVEL! NEQ 0 (
        call :ReportNotFoundExecutable %EXECUTABLE% %LOCATION_VARIABLE% %DESCRIPTION%
        exit /b 1
    )
    for /f "usebackq delims=" %%i in (`where /F "%EXECUTABLE%"`) do set SOURCE="%%~i"
)
XCOPY /E /D /y /q %SOURCE% %DESTINATION% > NUL 
IF !ERRORLEVEL! NEQ 0 (
    echo      [ERROR] XCOPY found an error copying %EXECUTABLE% to %DESTINATION%
            exit /b 1
)

goto :eof
endlocal



:CopyContentsOfEnvironmentVariable
::=======================================================================================================
:: Subroutine: CopyContentsOfEnvironmentVariable
::     If the environment variable %1 is set, we try to copy its contents
::     to the directory %2. If not, we fail.
::     
:: Arguments:   %1 should hold the name of the environment variable (not the value but the name)   
::              %2 is the destination directory for the files
::=======================================================================================================
setlocal EnableDelayedExpansion
IF NOT DEFINED %1% (
    echo     [ERROR] The %1 environment variable should be defined
    exit /b 1
) ELSE (
    rem %%%%1%%%% will get the value of the environment variable named %1%
    call :CopyAllContents "%~2" "%%%1%%%"
)
exit /b 0
endlocal


:CopyParentOfExecutable
::=======================================================================================================
:: Subroutine: CopyParentOfExecutable
::     Copies the folder containing the executable %1
::     Exits with error if the 'where' program can't find the executable
::     
:: Arguments:   %1 should hold the argument to where (an executable)
::              %2 is the destination directory for the files
::              %3 is a display name for the directory (in case of error) 
::=======================================================================================================
setlocal EnableDelayedExpansion
call where /Q %~1
if !ERRORLEVEL! NEQ 0 (
    exit /b 1
)
for /f "usebackq delims=" %%i in (`where /F "%~1"`) do set DIRECTORY=%%~dpi.
call :CopyAllContents %~2 "%DIRECTORY%"
exit /b 0
endlocal


:CopyAllContents
::=======================================================================================================
:: Subroutine: CopyAllContents
::     Copies the contents of all the folders in the semicolon-separated 
::     list passed as first argument
::
:: Arguments:   %1 is the destination directory
::              %2 should hold the semicolon-separated list of directories
::=======================================================================================================
setlocal EnableDelayedExpansion
SET "iter=%~2"
:NextItem
if "%iter%" == "" exit /b 0

FOR /F "tokens=1* delims=;" %%a in ("%iter%") do (
    IF EXIST %%a (
        call :VerboseCopying "%%a" "%~1"
        XCOPY /E /D /y /q "%%a" "%~1" > NUL
        IF !ERRORLEVEL! NEQ 0 (
            echo      [ERROR] XCOPY found an error copying "%%a" to "%~1"
            exit /b 1
        )
        call :Verbose "     Done."
        call :Verbose.

    ) ELSE (
       echo      [WARNING] "%%a" was given as a directory to copy files from, but it doesn't exist.
       echo.
    )
    SET "iter=%%b"
)
goto NextItem
endlocal


:ReportNotFound
setlocal EnableDelayedExpansion
set EXECUTABLE=%1
set LOCATION_VARIABLE=%2
set DESCRIPTION=%~3
echo     [ERROR] We couldn't find the directory for: %DESCRIPTION%.
echo     You should install %3 in your machine (calling 'where %1' from a command line should give a correct location)
echo     Alternatively, you can set the environment variable %LOCATION_VARIABLE% to the directory containing %DESCRIPTION% 
echo     (caution: setting this variable overrides other behavior and the contents of that directory will be used)
echo.
exit /b 0
endlocal

:ReportNotFoundExecutable
setlocal EnableDelayedExpansion
set EXECUTABLE=%1
set LOCATION_VARIABLE=%2
set DESCRIPTION=%~3
echo     [ERROR] We couldn't find the executable for: %DESCRIPTION%.
echo     You should install %3 in your machine (calling 'where %1' from a command line should give a correct location)
echo     Alternatively, you can set the environment variable %LOCATION_VARIABLE% to the directory containing %DESCRIPTION% 
echo     (caution: setting this variable overrides other behavior and the contents of that directory will be used)
echo.
exit /b 0
endlocal

:Verbose
IF DEFINED __VERBOSE (
    echo %~1%
)
exit /b 0


:VerboseCopying
IF DEFINED __VERBOSE (
    echo      Copying, if necessary, the contents of %1 to %2
)
exit /b 0


:Verbose.
IF DEFINED __VERBOSE (
    echo.
)
exit /b 0
endlocal

:usage
echo Usage: %__NAME% ^[flags^] PathToBxl
echo Display the help with %__NAME% /?
goto :end

:help
echo                                CMake Dependencies Copy
echo. 
echo    This script will copy the required dependencies to the required folder inside BXL.
echo    This directory is \Out\Bin\debug\net472\tools\cmakeninjapipenvironment, relative to
echo    the BuildXL root. 
echo.
echo    The script will copy everything under the LIB environment variable to 'lib',
echo    everything under the INCLUDE environment variable to 'include', the Windows SDK to 'sdk',
echo    the Python installation to 'python', and the C++ toolchain to 'cpptools' 
echo.  
echo.
echo    It is important to run this script from an environment that was able to do a correct local build, meaning that the dependencies are there.  
echo    The script will complain if the required environment variables are not set, but it can't know that their contents are complete. 
echo    When the script finishes the directory structure has to be something like this (some files and directories that should *definitely* be there are shown, 
echo    [...] indicates that more files and directories should be there as well):
echo.   	
echo    	CMakeNinjaPipEnvironment
echo    	!-----cmake
echo    	!   !`--- bin
echo    	!   !    `----- cmake.exe
echo    	!   !    `----- (...)
echo    	!   !`--- doc
echo    	!    `--- share
echo    	!-----cpptools
echo    	!   !`--- cl.exe
echo    	!   !`--- link.exe
echo    	!   `---- (...)
echo    	!-----include
echo    	!      `---- (...)
echo    	!-----lib
echo    	!     `---- (...)
echo    	!-----ninja
echo    	!       `---- ninja.exe 
echo    	!-----python
echo    	!       `---- python.exe
echo    	!       `---- (...)
echo    	!-----sdk
echo    	       `---- mt.exe
echo                `---- (...)
echo    FLAGS:
echo       /v - Verbose: print the operations
echo       /c - Automatic confirmation: skip the confirmation of the destination directory


:end
set "__VERBOSE="
set "__SKIP_CONFIRMATION="
set "__NAME="