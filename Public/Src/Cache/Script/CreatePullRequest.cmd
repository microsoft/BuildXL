:: Allows users to create a VSTS pull request or update it from the CloudStore command line.
:: The only requirement for this be run from corp so that \\codeflow\public is accessible.

@ECHO OFF
setlocal
:: parse the current head into a branch format (abbreviated reference) and store it in a local branch name variable.
for /F "usebackq" %%b in (`git rev-parse --abbrev-ref HEAD`) do SET localBranchName=%%b

if /I "%localBranchName%"=="master" (
   @ECHO Cannot push or create pull requests for master. Please create a feature branch for using this script.
   EXIT /B 1
)

if /I "%localBranchName%"=="head" (
   @ECHO Cannot push or create pull requests for detached heads. Please create a feature branch for using this script.
   EXIT /B 1
)

for /F "usebackq" %%b in (`git status -s`) do SET localGitStatus=%%b

IF /I NOT "%localGitStatus%"=="" (
   @ECHO Cannot push or create pull requests if you have uncommitted changes. Please commit all changes before running this script.
   EXIT /B 1
)

CALL git push --set-upstream origin %localBranchName%
start https://mseng.visualstudio.com/Domino/_git/Domino/pullrequestcreate?sourceRef=%localBranchName%^&targetRef=master
endlocal
EXIT /B 0


