@if not defined _ECHO @echo off
setlocal enabledelayedexpansion

set ENLISTMENTROOT=%~dp0
set SCRIPTROOT=%~dp0Shared\Scripts\

if /I "%1" == "-help" (
	goto :Usage
)

if /I "%1" == "/help" (
	goto :Usage
)

if /I "%1" == "/?" (
	goto :Usage
)

if /I "%1" == "-list" (
	goto :ListPrs
)

if /I "%1" == "/list" (
	goto :ListPrs
)

if /I "%1" == "-remove" (
	goto :RemovePr
)

if /I "%1" == "/remove" (
	goto :RemovePr
)

if /I "%1" == "-removeall" (
	goto :RemoveAll
)

if /I "%1" == "/removeall" (
	goto :RemoveAll
)

goto :CreatePr



REM ************************************************************
REM 
REM   CreatePr
REM
REM ************************************************************

:CreatePr
	set PrName=%1

	:CheckArguments
		if "%PrName%"=="" (
			goto :Usage
		)

	:EnsureWorktreeClean
		for /f %%F in ('call git status --porcelain') do (
			echo.
			echo Your working directory does not seem to be clean:
			call git status
			echo.
			echo Commit or revert changes and try again.
			goto :Error
		)

	:CheckForNoChanges
		call git diff --no-ext-diff --quiet 
		if %ERRORLEVEL% NEQ 0 (
			echo >&2 You have uncommited and unstaged changes.  Refusing to push for gated checkin.
			goto :Error
		)
		call git diff --no-ext-diff --cached --quiet
		if %ERRORLEVEL% NEQ 0 (
			echo >&2 You have uncommited staged changes.  Refusing to push for gated checkin.
			goto :Error
		)

	:PinCurrentCommitWithABranch
		call git branch --force RebaseAndPushWorkflowBranch
		if %ERRORLEVEL% NEQ 0 (
			echo.
			echo Could not checkpoint current work.
			goto :Error
		)

	:CreateBranchName
		set PR_BRANCH_NAME=personal/%USERNAME%/pr/%PrName%

	:CheckIfExists
		for /f %%R in ('git branch -r --list */%PR_BRANCH_NAME%') do (
			set PR_BRANCH_EXISTS=1
		)

	:AllowUserToBailOnUpdate
		if "%PR_BRANCH_EXISTS%"=="1" (
			echo.
			choice /c YN /M "Branch already exists, do you want to update the pull request?"
			set ANSWER=!ERRORLEVEL!
		)
		if "%ANSWER%" == "1" (
			echo.
			echo Updating the existing pull request with new bits
		)
		if "%ANSWER%" == "2" (
			echo.
			echo Cancelling updating the pull request. If you intended to create a new pull request, please call this script with a new name: %~nx0 ^<NewName^>
			echo.
			echo ---------------------------------------------------------------
			echo - Cancelled
			echo ---------------------------------------------------------------
			exit /b 1
			goto :Error
		)

	:ForcePushToServer
		call git push --force origin HEAD:%PR_BRANCH_NAME%
		if %ERRORLEVEL% NEQ 0 (
			echo >&2 Failed to force-push to branch %PR_BRANCH_NAME%
			goto :Error
		)
		start https://dev.azure.com/mseng/_git/Domino/pullrequestcreate?targetRef=master^&sourceRef=%PR_BRANCH_NAME%


	goto :Success


REM ************************************************************
REM 
REM   ListPrs
REM
REM ************************************************************

:ListPrs
	echo ---------------------------------------------------------------
	echo Listing all existing PR's
	echo ---------------------------------------------------------------

	REM Remove all local branches that have been removed on the server
	git remote prune origin > NUL

	for /f "tokens=1-6 delims=/" %%a in ('git branch --all ^| findstr /ic:"%username%/pr/"') do (
		if "%%f" NEQ "" (
			echo %%f
		)
	)
	exit /b 0
	goto :Done

REM ************************************************************
REM 
REM   RemovePr
REM
REM ************************************************************

:RemovePr
	set PrName=%2

	:CheckRemovePrArguments
		if "%PrName%"=="" (
			echo ERROR: Missing ^<PrName^> argument
			goto :Usage
		)
		set PR_BRANCH_NAME=personal/%USERNAME%/pr/%PrName%

	:CheckIfExists
		for /f %%R in ('git branch -r --list */%PR_BRANCH_NAME%') do (
			set PR_BRANCH_EXISTS=1
		)
		if "%PR_BRANCH_EXISTS%" NEQ "1" (
			echo ERROR: Branch %PR_BRANCH_NAME% does not exist on server
			exit /b 1
		)
	
	:AllowUserToCancel
		echo ---------------------------------------------------------------
		echo Removing PR branch: %PR_BRANCH_NAME%
		echo ---------------------------------------------------------------
		choice /c YN /M "Are you sure you want to remove '%PR_BRANCH_NAME%'?"
		set ANSWER=!ERRORLEVEL!
		if "%ANSWER%" == "1" (
			echo.
			echo Removing...
		)
		if "%ANSWER%" == "2" (
			echo.
			echo ---------------------------------------------------------------
			echo - Cancelled
			echo ---------------------------------------------------------------
			exit /b 1
			goto :Error
		)

	:ForceRemoveOnServer
		call git push --force origin :%PR_BRANCH_NAME%
		if %ERRORLEVEL% NEQ 0 (
		   echo >&2 Failed to remove branch %PR_BRANCH_NAME%
		   goto :Error
		)

	goto :Success

	
REM ************************************************************
REM 
REM   RemovePr
REM
REM ************************************************************

:RemoveAll
	echo ---------------------------------------------------------------
	echo Are you sure you want to remove the following PR branches?
	echo ---------------------------------------------------------------
	REM Remove all local branches that have been removed on the server
	git remote prune origin > NUL

	for /f "tokens=1-6 delims=/" %%a in ('git branch --all ^| findstr /ic:"%username%/pr/"') do (
		if "%%f" NEQ "" (
			echo - %%f
		)
	)
	choice /c YN /M "Are you sure?"
	set ANSWER=!ERRORLEVEL!
	if "%ANSWER%" == "1" (
		echo.
		echo Removing all ...
	)
	if "%ANSWER%" == "2" (
		echo.
		echo ---------------------------------------------------------------
		echo - Cancelled
		echo ---------------------------------------------------------------
		exit /b 1
		goto :Error
	)

	for /f "tokens=1-6 delims=/" %%a in ('git branch --all ^| findstr /ic:"%username%/pr/"') do (
		if "%%f" NEQ "" (
			call git push --force origin :personal/%USERNAME%/pr/%%f
			if %ERRORLEVEL% NEQ 0 (
				echo >&2 Failed to remove branch 'personal/%USERNAME%/pr/%%f'
				set SomeError=1
			)
		)
	)
	
	if "%SomeError%" == "1" (
		goto :Error
		exit /b 1
	)
	
	goto :Success
	exit /b 0

:Usage
	echo.
	echo ---------------------------------------------------------------
	echo - Usage: %~nx0 ^<PrName^>
	echo -	 This will create serverside branch with name 'personal/pr/%username%/pr/^<PrName^>'
	echo -	 and open a browser to create a pull request for that branch
	echo -	 If you already have a pull request open with that name it will update it.
	echo -
	echo - Usage: %~nx0 /list
	echo -	 Lists all existing pr branches you have open.
	echo -
	echo - Usage: %~nx0 /remove ^<PrName^>
	echo -	 Removes the serverside branch with name 'personal/pr/%username%/pr/^<PrName^>'
	echo -
	echo - Usage: %~nx0 /removeAll
	echo -	 Removes all of the the serverside branches under 'personal/pr/%username%/pr'
	echo ---------------------------------------------------------------
	exit /b 1


:Success
	echo.
	echo ++++++++++++++++++++++++++++++++++++++++++++++++
	echo + SUCCESS  :-)  Pull request created, please publish on the web UI. +
	echo ++++++++++++++++++++++++++++++++++++++++++++++++
	echo.
	exit /b 0


:Error
	echo.
	echo ---------------------------------------------------------------
	echo - FAILURE  :-(  Fix the issues and "%~nx0 %PrName%" again.	-
	echo ---------------------------------------------------------------
	exit /b 1

:End