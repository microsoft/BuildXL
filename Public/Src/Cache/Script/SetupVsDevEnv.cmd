@echo off
if not defined VisualStudioVersion (
    if defined VS140COMNTOOLS (
        if EXIST "%VS140COMNTOOLS%\VsDevCmd.bat" (
            echo Running "%VS140COMNTOOLS%\VsDevCmd.bat" to set up VS2015 command prompt.
            echo You can save time from this step by opening and using 'Developer Command Prompt for VS2015' window.
            call "%VS140COMNTOOLS%\VsDevCmd.bat"
        )
    )
)
if not defined VisualStudioVersion exit /b 1
exit /b 0
