taskkill -f -im bxl.exe

rmdir /s /q .buildxl
rmdir /s /q out
rmdir /s /q output
rmdir /s /q src\TestResults

REM Set the caches so that the packages will be redownloaded after clean.
SET _BUILDXL_INIT_DONE=
SET _BUILDXL_INIT_HASH=