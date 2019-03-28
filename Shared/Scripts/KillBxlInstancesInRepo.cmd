taskkill -f -im bxl.exe 2> NUL
taskkill -f -im bxp.exe 2> NUL
taskkill -f -im bxp-server.exe 2> NUL
powershell -ExecutionPolicy RemoteSigned %~dp0KillBxlInstancesInRepo.ps1 %*