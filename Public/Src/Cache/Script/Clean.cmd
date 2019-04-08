@echo off
setlocal
pushd %~dp0..

for %%f in (ContentStore MemoizationStore) do (
    for %%p in (App Interfaces InterfacesTest Library Test) do (
        if exist %%f\%%p\bin rmdir /s /q %%f\%%p\bin
        if exist %%f\%%p\obj rmdir /s /q %%f\%%p\obj
    )
)

rd /s/q ..\bin
rd /s/q ..\obj

popd
