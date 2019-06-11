// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "FileAccessHelpers.h"

// ----------------------------------------------------------------------------
// DEFINES
// ----------------------------------------------------------------------------
#define EVENTLOG_INFORMATION_TYPE_ID 1001
#define EVENTLOG_ERROR_TYPE_ID 1002

// ----------------------------------------------------------------------------
// TYPE DEFINITIONS
// ----------------------------------------------------------------------------

typedef unsigned __int64 ulong;

// ----------------------------------------------------------------------------
// CONSTANTS
// ----------------------------------------------------------------------------

const GUID g_manifestGuid = { 0x7CFDBB96, 0xC3D6, 0x47CD, { 0x90, 0x26, 0x8F, 0xA8, 0x63, 0xC5, 0x2F, 0xEC } };

// ----------------------------------------------------------------------------
// INTERFACES
// ----------------------------------------------------------------------------

__interface __declspec(uuid("7CFDBB96-C3D6-47CD-9026-8FA863C52FEC")) IDetourServicesManifest;
interface IDetourServicesManifest
{
};


// ----------------------------------------------------------------------------
// FUNCTION DECLARATIONS
// ----------------------------------------------------------------------------

// Status indication for creating a detoured process; useful for preventing ambiguous error indication when a process fails to start.
// This must be in sync with CreateDetouredProcessStatus.cs
enum class CreateDetouredProcessStatus : int {
    Succeeded = 0,
    ProcessCreationFailed = 1,
    DetouringFailed = 2,
    JobAssignmentFailed = 3,
    HandleInheritanceFailed = 4,
    ProcessResumeFailed = 5,
    PayloadCopyFailed = 6,
    AddProcessToSiloFailed = 7,
	CreateProcessAttributeListFailed = 8
};

CreateDetouredProcessStatus
WINAPI
InternalCreateDetouredProcess(
    LPCWSTR lpApplicationName,
    LPWSTR lpCommandLine,
    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    BOOL bInheritHandles,
    DWORD dwCreationFlags,
    LPVOID lpEnvironment,
    LPCWSTR lpcwWorkingDirectory,
    LPSTARTUPINFOW lpStartupInfo,
    HANDLE hJob,
    DetouredProcessInjector *injector,
    LPPROCESS_INFORMATION lpProcessInformation,
    CreateProcessW_t pfCreateProcessW
);

CreateDetouredProcessStatus
WINAPI
CreateDetouredProcess(
	LPCWSTR lpcwCommandLine,
	DWORD dwCreationFlags,
	LPVOID lpEnvironment,
	LPCWSTR lpcwWorkingDirectory,
	HANDLE hStdInput, HANDLE hStdOutput, HANDLE hStdError,
	HANDLE hJob,
	DetouredProcessInjector *injector,
	bool addProcessToSilo, 
	HANDLE* phProcess, HANDLE* phThread, DWORD* pdwProcessId
);

bool
WINAPI
IsDetoursDebug();

void LogEventLogMessage(const std::wstring& a_msg,
    const WORD a_type,
    const WORD eventId,
    const std::wstring& a_name);

void RetrieveParentProcessId();

class TranslatePathTuple
{
private:
    std::wstring fromPath;
    std::wstring toPath;

public:
    TranslatePathTuple()
    {
        fromPath.assign(L"");
        toPath.assign(L"");
    }

    TranslatePathTuple(TranslatePathTuple& other)
    {
        fromPath.assign(other.fromPath);
        toPath.assign(other.toPath);
    }

    TranslatePathTuple(std::wstring& from, std::wstring& to)
    {
        fromPath.assign(from);
        toPath.assign(to);
    }

    std::wstring& GetToPath()
    {
        return toPath;
    }

    std::wstring& GetFromPath()
    {
        return fromPath;
    }
};

// CODESYNC: SubstituteProcessExecutionInfo.cs :: ShimProcessMatch class
class ShimProcessMatch
{
public:
    std::unique_ptr<wchar_t> ProcessName;
    std::unique_ptr<wchar_t> ArgumentMatch;

    // Assumes params are heap strings and takes control of their lifetime.
    ShimProcessMatch(wchar_t *processName, wchar_t *argMatch)
    {
        ProcessName = std::unique_ptr<wchar_t>(processName);
        ArgumentMatch = std::unique_ptr<wchar_t>(argMatch);
    }

    ShimProcessMatch(const ShimProcessMatch &other)
        : ShimProcessMatch(other.ProcessName.get(), other.ArgumentMatch.get())
    {}

    ShimProcessMatch& operator=(ShimProcessMatch& other)
    {
        // Implementing as a move instead of copy, just to satisfy the compiler.
        ProcessName.reset(other.ProcessName.release());
        ArgumentMatch.reset(other.ArgumentMatch.release());
    }
};
