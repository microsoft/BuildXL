// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// RemoteApi.exe
//
// In the course of testing file system detours, we need to be able to exercise particular APIs in isolation.
// This program effectively exposes some needed APIs over RPC.
//
// Command format (sent over stdin):
//   commandName,parameter[,parameter]
// Response format (sent over stdout):
//   commandName,result (0 for success or 1 for failure).
//
// Supported commands:
//  EnumerateWithFindFirstFileEx: Takes a path parameter to be passed to FindFirstFileEx (e.g. C:\directory\*.h to find files ending in .h under C:\directory)
//                             Directory enumeration is exhausted via calls to FindNextFile.
//                             Returns 0 on success or 1 on failure (note that success is returned if enumeration proceeded, even if no matches were found or if
//                             the search path turned out to be a file rather than a directory).
//  DeleteViaNtCreateFile: Takes a path parameter and opens it (an existing file) for deletion. Returns 0 on success or 1 on failure.
//                         The path must be absolute and canonicalized (including a \??\ prefix) as required by NtCreateFile.
//  CreateHardLink: Creates a hardlink from the first parameter (existing file) to the second.
//  EnumerateFileOrDirectoryByHandle: Takes a path parameter to open (e.g. C:\directory\) and enumerates members via NtQueryDirectoryFile.
//                             Returns 0 on success or 1 on failure (note that success is returned if enumeration proceeded, even if no matches were found or if
//                             the search path turned out to be a file rather than a directory).
// TODO: These functions should be exposed as a Bond service. For now, we're starting with a trivial text format.

#include "stdafx.h"
#include "Command.h"
#pragma warning( disable : 4711) // ... selected for inline expansion

bool EnumerateWithFindFirstFileEx(std::wstring const& path) {
    WIN32_FIND_DATAW findData{};
    HANDLE findHandle = FindFirstFileExW(path.c_str(), FindExInfoBasic, &findData, FindExSearchNameMatch, NULL, 0);
    if (findHandle != INVALID_HANDLE_VALUE) {
        while (FindNextFileW(findHandle, &findData)) {}

        DWORD error = GetLastError();
        FindClose(findHandle);
        return error == ERROR_NO_MORE_FILES;
    }
    else {
        DWORD error = GetLastError();
        return error == ERROR_FILE_NOT_FOUND || error == ERROR_DIRECTORY;
    }
}

bool EnumerateFileOrDirectoryByHandle(std::wstring const& path) {
    HANDLE handle = CreateFileW(
        path.c_str(),
        FILE_LIST_DIRECTORY | SYNCHRONIZE,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr,
        OPEN_EXISTING,
        FILE_FLAG_BACKUP_SEMANTICS,
        NULL);
    if (handle == NULL || handle == INVALID_HANDLE_VALUE) {
        return false;
    }

    constexpr int BufferSize = 4096;
    char* buffer = (char*)_aligned_malloc(BufferSize, 8);

    for ( ; ; ) {
        IO_STATUS_BLOCK iosb{};
        NTSTATUS status = NtQueryDirectoryFile(
            handle,
            NULL, // event
            NULL, // apc
            NULL, // apc context,
            &iosb,
            buffer,
            BufferSize,
            FileDirectoryInformation,
            FALSE, // return single entry,
            NULL, // filename filter,
            FALSE // restart scan
            );

        if (!NT_SUCCESS(status)) {
            CloseHandle(handle);
            _aligned_free(buffer);
            return (status == STATUS_NO_MORE_FILES);
        }
    }
}


#undef CreateHardLink
bool CreateHardLink(std::wstring const& existingFile, std::wstring const& newLink) {
    BOOL success = CreateHardLinkW(newLink.c_str(), existingFile.c_str(), nullptr);
    return success == TRUE;
}

bool DeleteViaNtCreateFile(std::wstring const& path) {
    HANDLE handle{};

    UNICODE_STRING usPath;
    RtlInitUnicodeString(&usPath, path.c_str());

    OBJECT_ATTRIBUTES attrib;
    InitializeObjectAttributes(
        &attrib,
        &usPath,
        OBJ_CASE_INSENSITIVE,
        NULL,
        NULL
        );

    IO_STATUS_BLOCK iosb{};

    NTSTATUS status = NtCreateFile(
        &handle,
        DELETE | SYNCHRONIZE,
        &attrib,
        &iosb,
        (PLARGE_INTEGER)nullptr, // AllocationSize
        (ULONG)0, // Attributes,
        FILE_SHARE_DELETE,
        FILE_OPEN,
        FILE_NON_DIRECTORY_FILE | FILE_SYNCHRONOUS_IO_NONALERT | FILE_DELETE_ON_CLOSE,
        nullptr, // EaBuffer,
        0 // EaLength
        );

    if (NT_SUCCESS(status)) {
        status = NtClose(handle);
        if (!NT_SUCCESS(status)) {
            return false;
        }

        return true;
    }
    else {
        return false;
    }
}

static CommandBase const* Commands[] = {
    new Command<SingleParam>(L"EnumerateWithFindFirstFileEx", EnumerateWithFindFirstFileEx),
    new Command<SingleParam>(L"EnumerateFileOrDirectoryByHandle", EnumerateFileOrDirectoryByHandle),
    new Command<SingleParam>(L"DeleteViaNtCreateFile", DeleteViaNtCreateFile),
    new Command<DualParam>(L"CreateHardLink", CreateHardLink),
    nullptr
};

int main(int argc, char **argv)
{
    (void)argv; // Unused

    if (argc != 1) {
        std::wcerr << L"No arguments expected. API commands are expected over stdin." << std::endl;
        return 1;
    }

    std::wstring lineBuffer{};
    std::wcin.exceptions(std::ios_base::badbit);
    
    while (!std::wcin.eof()) {
        lineBuffer.clear();
        std::getline(std::wcin, lineBuffer);
        if (std::wcin.fail()) {
            // EOF immediately following a separator causes getline to fail (no string to read).
            if (std::wcin.eof()) {
                break;
            }
            else {
                std::wcerr << L"Stream failure while reading a command." << std::endl;
                return 5;
            }
        }

        std::vector<std::wstring> parameters;
        {
            wchar_t const* tokenStart = lineBuffer.c_str();
            wchar_t const* tokenEnd = tokenStart;

            for ( ; ; ) {
                wchar_t c = *tokenEnd;
                if (c == L',' || c == L'\0') {
                    parameters.push_back(
                        std::wstring(tokenStart, tokenEnd));

                    if (c == L'\0') { break; }

                    tokenEnd = tokenStart = tokenEnd + 1;
                }
                else {
                    tokenEnd++;
                }
            }
        }

        if (parameters.size() == 0 || parameters[0].length() == 0) {
            std::wcerr << L"Bad command format. Expected commandName,parameter,parameter ; zero or more parameters separated by commas. Actual: '" << lineBuffer << "'" << std::endl;
            return 2;
        }

        std::wstring const& commandName = parameters[0];
        for (CommandBase const** c = Commands; ; c++) {
            CommandBase const* cmd = *c;
            if (cmd == nullptr) {
                std::wcerr << L"Unknown command name. Supported: [EnumerateWithFindFirstFileEx, DeleteViaNtCreateFile, CreateHardLink]. Actual: '" << commandName << "'" << std::endl;
                return 3;
            } 

            bool handled = false;
            CommandInvocationResult result = cmd->InvokeIfMatches(parameters);
            switch (result) {
            case Success:
                handled = true;
                std::wcout << commandName << L"," << 0;
                break;
            case Failure:
                handled = true;
                std::wcout << commandName << L"," << 1;
                break;
            case IncorrectParameterCount:
                std::wcerr 
                    << L"Wrong number of parameters for " << commandName
                    << L". Expected: " << cmd->requiredParameters << " Actual: '" << (parameters.size() - 1) << "'" << std::endl;
                return 4;
            case CommandNameDoesNotMatch:
                handled = false;
                break;
            default:
                assert(false);
            }

            if (handled) { break; }
        }
    }

    return 0;
}
