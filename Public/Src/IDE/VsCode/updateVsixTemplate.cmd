@echo off
SET myPath=%~dp0
set templatePath=%myPath%\pluginTemplate
set clientPath=%myPath%\client

echo Recompiling the plugin
cmd /c %clientPath%\node_modules\.bin\tsc -p %clientPath%

if exist %templatePath%\extension (
   echo Cleaning template directory
   rd /s /q  %templatePath%\extension
)

echo Updating the template
xcopy /q /y /s /e %clientPath%\out\src\*.* %templatePath%\extension\out\src\ > NUL
xcopy /q /y /s /e %clientPath%\node_modules\vscode\*.* %templatePath%\extension\node_modules\vscode\ > NUL
xcopy /q /y /s /e %clientPath%\node_modules\vscode-debugadapter\*.* %templatePath%\extension\node_modules\vscode-debugadapter\ > NUL
xcopy /q /y /s /e %clientPath%\node_modules\vscode-debugprotocol\*.* %templatePath%\extension\node_modules\vscode-debugprotocol\ > NUL
xcopy /q /y /s /e %clientPath%\node_modules\vscode-jsonrpc\*.* %templatePath%\extension\node_modules\vscode-jsonrpc\ > NUL
xcopy /q /y /s /e %clientPath%\node_modules\vscode-languageclient\*.* %templatePath%\extension\node_modules\vscode-languageclient\ > NUL
xcopy /q /y /s /e %clientPath%\node_modules\vscode-languageserver-protocol\*.* %templatePath%\extension\node_modules\vscode-languageserver-protocol\ > NUL
xcopy /q /y /s /e %clientPath%\node_modules\vscode-languageserver-types\*.* %templatePath%\extension\node_modules\vscode-languageserver-types\ > NUL
xcopy /q /y /s /e %clientPath%\node_modules\vscode-nls\*.* %templatePath%\extension\node_modules\vscode-nls\ > NUL
