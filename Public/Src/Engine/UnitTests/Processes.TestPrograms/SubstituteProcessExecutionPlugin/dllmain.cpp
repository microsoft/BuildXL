// dllmain.cpp : Defines the entry point for the DLL application.
// Disable warnings about unreferenced inline functions. We'd do this below but this disable has to be in effect
// at optimization time.
#pragma warning( disable : 4514 4710 )

#pragma warning( push )
#pragma warning( disable : 4350 4668 )
#include <windows.h>
#include <string>
#pragma warning( pop )

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    UNREFERENCED_PARAMETER(hModule);
    UNREFERENCED_PARAMETER(lpReserved);
    
    switch (ul_reason_for_call)
    {
        case DLL_PROCESS_ATTACH:
        case DLL_THREAD_ATTACH:
        case DLL_THREAD_DETACH:
        case DLL_PROCESS_DETACH:
            break;
    }

    return TRUE;
}


/// The substitute process filter function configured in BuildXL sandboxing.
/// One 32-bit and one 64-bit DLL must be provided to match the DetoursServices.dll
/// flavor used for wrapping a process.
///
/// Returns TRUE or nonzero if the prospective process should have the shim process injected. Returns FALSE or zero otherwise.
///
/// Note for implementors: Process creation is halted for this process until this callback returns.
/// WINAPI (__stdcall) is used for register call efficiency.
///
/// command: The executable command. Can be a fully qualified path, relative path, or unqualified path
/// that needs a PATH search.
///
/// arguments: The arguments to the command. May be an empty string.
///
/// environmentBlock: The environment block for the process. The format is a sequence of "var=value"
/// null-terminated strings, with an empty string (i.e. double null character) terminator. Note that
/// values can have equals signs in them; only the first equals sign is the variable name separator.
/// See more formatting info in the lpEnvironment parameter description at
/// https://docs.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-createprocessa
///
/// workingDirectory: The working directory for the command.
// See SubstituteProcessExecutionFilterFunc definition in BuildXL codebase.
extern "C" __declspec(dllexport) BOOL WINAPI CommandMatches(const wchar_t* command, const wchar_t* arguments, LPVOID environmentBlock, const wchar_t* workingDirectory)
{
    UNREFERENCED_PARAMETER(command);
    UNREFERENCED_PARAMETER(environmentBlock);
    UNREFERENCED_PARAMETER(workingDirectory);

    std::wstring marker(L"DoNotShimMe");

    if (command != nullptr) 
    {
        std::wstring command_str(command);
        if (command_str.find(marker) != -1)
        {
            return FALSE;
        }
    }

    if (arguments != nullptr) 
    {
        std::wstring arguments_str(arguments);
        if (arguments_str.find(marker) != -1)
        {
            return FALSE;
        }
    }
    
    return TRUE;
}