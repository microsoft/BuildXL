// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#include "stdafx.h"

#include <algorithm>
#include <memory>

#include "DataTypes.h"
#include "DebuggingHelpers.h"
#include "DetoursHelpers.h"
#include "FileAccessHelpers.h"
#include "SendReport.h"
#include "PolicyResult.h"
#include "buildXL_mem.h"

using std::unique_ptr;

extern volatile LONG g_detoursAllocatedNoLockConcurentPoolEntries;
extern volatile LONG64 g_detoursMaxHandleHeapEntries;
extern volatile LONG64 g_detoursHandleHeapEntries;

// ----------------------------------------------------------------------------
// HELPER FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

void SendReportString(_In_z_ wchar_t const* dataString)
{
    if (g_reportFileHandle == NULL || g_reportFileHandle == INVALID_HANDLE_VALUE) {
        return;
    }

    // Increment the message sent counter.
    if (g_messageCountSemaphore != INVALID_HANDLE_VALUE)
    {
        ReleaseSemaphore(g_messageCountSemaphore, 1, nullptr);
    }

    OVERLAPPED overlapped;
    ZeroMemory(&overlapped, sizeof(OVERLAPPED));
    // This offset specifies "append".
    overlapped.Offset = 0xFFFFFFFF;
    overlapped.OffsetHigh = 0xFFFFFFFF;

    size_t reportLineLength = sizeof(wchar_t) * wcslen(dataString);
    DWORD bytesWritten;
    DWORD lastError = GetLastError();
    if (!WriteFile(g_reportFileHandle, dataString, (DWORD)reportLineLength, &bytesWritten, &overlapped))
    {
        DWORD error = GetLastError();
        Dbg(L"Failed to write file access report line: %08X. Exiting with code %d.", (int)error, DETOURS_PIPE_WRITE_ERROR_4);
        wprintf(L"Failed to write file access report line: %08X. Exiting with code %d.", (int)error, DETOURS_PIPE_WRITE_ERROR_4);
        fwprintf(stderr, L"Failed to write file access report line: %08X. Exiting with code %d.", (int)error, DETOURS_PIPE_WRITE_ERROR_4);
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PIPE_WRITE_ERROR_4, L"Failure writing message to pipe: exit(-46).", DETOURS_WINDOWS_LOG_MESSAGE_4);
    }

    SetLastError(lastError);
}

// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

void ReportFileAccess(
    FileOperationContext const& fileOperationContext,
    FileAccessStatus status,
    PolicyResult const& policyResult,
    AccessCheckResult const& accessCheckResult,
    DWORD error,
    USN usn,
    wchar_t const* filter)
{
    if (g_reportFileHandle == NULL || g_reportFileHandle == INVALID_HANDLE_VALUE) {
        return;
    }

    PCWSTR fileName, filterStr;

    if (policyResult.IsIndeterminate()) {
        fileName = fileOperationContext.NoncanonicalPath;
    }
    else {
        fileName = policyResult.GetCanonicalizedPath().GetPathString();
    }

    if (fileName == nullptr) {
        fileName = L"";
    }

    if (filter == nullptr || accessCheckResult.RequestedAccess != RequestedAccess::Enumerate) {
        filterStr = L"";
    }
    else {
        filterStr = filter;
    }

    if (g_currentProcessCommandLine == nullptr) {
        g_currentProcessCommandLine = L"";
    }

    size_t fileNameLength = wcslen(fileName); // in characters
    size_t filterLength = wcslen(filterStr); // in characters
    size_t fileProcessCommandLineLength = wcslen(g_currentProcessCommandLine); // in characters
    size_t operationLen = wcslen(fileOperationContext.Operation); // in characters
    size_t reportBufferSize = fileNameLength + filterLength + fileProcessCommandLineLength + operationLen + 100; // in characters

    // Adding 100 should be enough for now since the max values for the members of the message are:
    // ReportType_FileAccess – 1 char
    // g_currentProcessId – 8 chars
    // accessCheckResult.RequestedAccess – 1 char
    // status – 1 char
    // (int)(accessCheckResult.ReportLevel == ReportLevel::ReportExplicit) – 1 char(0 or 1)
    // Error – 8 chars
    // Usn – 16 chars
    // fileOperationContext.DesiredAccess – 8 chars
    // fileOperationContext.ShareMode – 8 chars
    // fileOperationContext.CreationDisposition – 8 chars,
    // fileOperationContext.FlagsAndAttributes – 8 chars
    // policyResult.IsIndeterminate() ? 0 : policyResult.GetPathId() – 8 chars
    // filename separately added
    // filterStr separately added
    // g_currentProcessCommandLine – separately added
    // 13 chars for | chars
    // 5 chars for ‘, ’  ‘ : ’ ‘\r’ ‘\n’ ‘\0’ chars
    // Total : 94 characters.

    unique_ptr<wchar_t[]> report(new wchar_t[reportBufferSize]);
    assert(report.get());

    // Only report the process command line args when the C# code has requested it and when the file operation context is "Process"
    // This way we only transmit the command line arguments once
    int constructReportResult = -1;
    if (ReportProcessArgs() && !_wcsicmp(fileOperationContext.Operation, L"Process")) {
        // The command line arguments may contain the | (pipe) character - the same character that is used here as a field separator.
        // It is important to keep the command line arguments last in this string because the C# code will 
        // check how many | chars the string contains and if there are more fields than expected, it will assume that  
        // everything after the last expected (13th) field is part of the command line arguments.
        //
        // The command line can contain newline characters. In the C# code our pipe reader performs read line, and thus it can read part of
        // the command line. Thus, the command line needs to be sanitized. This is OK because no further consumer should rely on the exact
        // form of the command line. Here, newline characters are simply replaced with space. Replacing it with space is fine because
        // it won't change the length of the string, and thus no need to resize the report buffer.
        std::wstring commandLine(g_currentProcessCommandLine);
        std::replace(commandLine.begin(), commandLine.end(), L'\r', L' ');
        std::replace(commandLine.begin(), commandLine.end(), L'\n', L' ');

        constructReportResult = swprintf_s(report.get(), reportBufferSize, L"%d,%s:%lx|%x|%x|%x|%lx|%llx|%lx|%lx|%lx|%lx|%lx|%s|%s|%s\r\n",
            ReportType_FileAccess,
            fileOperationContext.Operation,
            g_currentProcessId,
            accessCheckResult.RequestedAccess,
            status,
            (int)(accessCheckResult.ReportLevel == ReportLevel::ReportExplicit),
            error,
            usn,
            fileOperationContext.DesiredAccess,
            fileOperationContext.ShareMode,
            fileOperationContext.CreationDisposition,
            fileOperationContext.FlagsAndAttributes,
            policyResult.IsIndeterminate() ? 0 : policyResult.GetPathId(),
            fileName,
            filterStr,
            commandLine.c_str());
    }
    else
    {
        constructReportResult = swprintf_s(report.get(), reportBufferSize, L"%d,%s:%lx|%x|%x|%x|%lx|%llx|%lx|%lx|%lx|%lx|%lx|%s|%s\r\n",
            ReportType_FileAccess,
            fileOperationContext.Operation,
            g_currentProcessId,
            accessCheckResult.RequestedAccess,
            status,
            (int)(accessCheckResult.ReportLevel == ReportLevel::ReportExplicit),
            error,
            usn,
            fileOperationContext.DesiredAccess,
            fileOperationContext.ShareMode,
            fileOperationContext.CreationDisposition,
            fileOperationContext.FlagsAndAttributes,
            policyResult.IsIndeterminate() ? 0 : policyResult.GetPathId(),
            fileName,
            filterStr);
    }

    if (constructReportResult <= 0)
    {
        Dbg(L"ReportFileAccess:swprintf_s: %d <= 0", constructReportResult);
        assert(!L"ReportFileAccess:swprintf_s: %d <= 0");
    }
    else
    {
        SendReportString(report.get());
    }
}

void ReportProcessDetouringStatus(
    ProcessDetouringStatus status,
    const LPCWSTR lpApplicationName,
    const LPWSTR lpCommandLine,
    const BOOL needsInjectioin,
    const HANDLE hJob,
    const BOOL disableDetours,
    const DWORD dwCreationFlags,
    const BOOL detoured,
    const DWORD error,
    const CreateDetouredProcessStatus createProcessStatus)
{
    if (g_reportFileHandle == NULL || g_reportFileHandle == INVALID_HANDLE_VALUE || !ShouldLogProcessDetouringStatus()) {
        return;
    }

    DWORD len = MAX_PATH;
    static wchar_t* errorString = L"Error getting process name: GetModuleFileNameW failed";

    unique_ptr<wchar_t[]> processName(new wchar_t[len]);
    wcscpy_s(processName.get(), len, errorString);

    // See https://msdn.microsoft.com/query/dev14.query?appId=Dev14IDEF1&l=EN-US&k=k(LIBLOADERAPI%2FGetModuleFileNameW);k(GetModuleFileNameW);k(SolutionItemsProject);k(SolutionItemsProject);k(SolutionItemsProject);k(TargetFrameworkMoniker-.NETFramework,Version%3Dv4.5.1);k(DevLang-C%2B%2B);k(TargetOS-Windows)&rd=true.
    // this function always succeeds with putting something in the processName.
    // The process name could be cut off and this will be known by testing if last error 
    // is ERROR_INSUFFICIENT_BUFFER.
    // We can't test for other failures, because very often Windows PI don't change the last error 
    // in success cases and another error code might be left over from a previous operation.
    while (true)
    {
        if (GetModuleFileNameW(NULL, processName.get(), len) == 0)
        {
            // If the the function fails, log a Dbg message and continue..
            // Otherwise it is OK to continue. Just send an "unknown" process name.
            Dbg(L"Could not get the processName. GetModuleFileNameW function failed.");
            break;
        }

        // Check need to allocate more space.
        if (GetLastError() == ERROR_INSUFFICIENT_BUFFER)
        {
            len = 2 * len;
            processName.reset(new wchar_t[len]);
            ZeroMemory(processName.get(), len * sizeof(wchar_t));
            continue;
        }

        // Gotten the process name. Break out of the loop.
        break;
    }

    size_t const reportBufferSize =
        30 /*Report ID type*/ +
        30 /*Process ID*/ +
        (30 * 10) /*4-byte int values*/ +
        12 /*Separators*/ +
        (processName != nullptr ? wcslen(processName.get()) : 10) /*processName*/ +
        (lpApplicationName != nullptr ? wcslen(lpApplicationName) : 10) /*lpApplicationName*/ +
        (lpCommandLine != nullptr ? wcslen(lpCommandLine) : 10) /*lpCommandLine*/ +
        3; /*\r\n null*/

    wchar_t* nullStringPtr = L"null";

    unique_ptr<wchar_t[]> report(new wchar_t[reportBufferSize]);

#pragma warning(suppress: 4826)
    int const constructReportResult = swprintf_s(report.get(), reportBufferSize, L"%u,%lu|%u|%s|%s|%u|%llu|%u|%u|%u|%u|%u|%s\r\n",
        ReportType_ProcessDetouringStatus,
        GetCurrentProcessId(),
        status,
        processName.get() != nullptr ? processName.get() : nullStringPtr,
        lpApplicationName != nullptr ? lpApplicationName : nullStringPtr,
        needsInjectioin ? 1 : 0,
        reinterpret_cast<unsigned long long>(hJob),
        disableDetours ? 1 : 0,
        (unsigned)dwCreationFlags,
        detoured ? 1 : 0,
        (unsigned)error,
        (unsigned)createProcessStatus,
        lpCommandLine != nullptr ? lpCommandLine : nullStringPtr);

    assert(constructReportResult > 0);

    if (constructReportResult > 0)
    {
        SendReportString(report.get());
    }
}

void ReportProcessData(
    IO_COUNTERS const& ioCounters,
    FILETIME const& creationTime,
    FILETIME const& exitTime,
    FILETIME const& kernelTime,
    FILETIME const& userTime, 
    DWORD const& exitCode,
    DWORD const& parentProcessId,
    LONG64 const& detoursMaxMemHeapSize)
{
    if (g_reportFileHandle == NULL || g_reportFileHandle == INVALID_HANDLE_VALUE || !ShouldLogProcessData()) {
        return;
    }

    wchar_t fileName[MAX_PATH];
    if (GetModuleFileNameW(NULL, fileName, _countof(fileName)) == 0)
    {
        return;
    }

    // There is 1 32-bit report type (ReportType_ProcessData), which has a max character length of 10 characters.
    // There is 1 32-bit process ID, which has a max character length of 10 characters.
    // There are 6 64-bit values, each value has a max length of 20 characters each. These represent the IO counters,
    // There are 7 32-bit values, each value has a max length of 10 characters each. These represent the creation time, 
    // exit time, and the kernel and user mode execution times. They have a high and low DWORD value.
    // There is 1 32 bit process exit code.
    // There is 1 32 bit parent process id.
    // There are 29 separators for the "," and "|" characters. (30 values total gives us 29 separators)
    // There are 5 * 64 bit and 2 * 32 bit for detours max memory heap size * and payload size, final heap allocated, max and final HandleHeapEntries, allocated pool entries for the non-locking list.
    // There are 6 * 64 bit for the max allocated/reallocated, virtual allocated data, max realloc chunck, final and max app used heap space
    // And the length of the module file name.
    // 3 characters for "\r\n" and null.
    size_t const reportBufferSize = 
        10 /*Report ID type*/ +
        10 /*Process ID*/ +
        (20 * 6) /*IO Counters*/ +
        (10 * 7) /*Creation, exit, kernel, user times*/ +
        29 /*Separators*/ +
        wcslen(fileName) + /*Module file name*/ +
        10 /*Process exit code*/ +
        10 /*Parent process id*/ +
        120 /*Detours max memory heap size * and payload size, final heap allocated, max and final HandleHeapEntries, allocated pool entries for the non-locking list. */ +
        3; /*\r\n null*/

    unique_ptr<wchar_t[]> report(new wchar_t[reportBufferSize]);

    // Unlike the file-access, this data is only useful for analyzing the times processes
    // take in the build. So, do not crash or assert if we cannot allocate memory for the
    // report string. Just silently bail on the operation.
    if (report.get() == nullptr)
    {
        return;
    }

    int const constructReportResult = swprintf_s(report.get(), reportBufferSize, L"%u,%lu|%I64u|%I64u|%I64u|%I64u|%I64u|%I64u|%lu|%lu|%lu|%lu|%lu|%lu|%lu|%lu|%s|%lu|%lu|%I64u|%lu|%I64u|%lu|%I64u|%I64u\r\n",
        ReportType_ProcessData,
        GetCurrentProcessId(),
        ioCounters.ReadOperationCount,
        ioCounters.WriteOperationCount,
        ioCounters.OtherOperationCount,
        ioCounters.ReadTransferCount,
        ioCounters.WriteTransferCount,
        ioCounters.OtherTransferCount,
        creationTime.dwHighDateTime,
        creationTime.dwLowDateTime,
        exitTime.dwHighDateTime,
        exitTime.dwLowDateTime,
        kernelTime.dwHighDateTime,
        kernelTime.dwLowDateTime,
        userTime.dwHighDateTime,
        userTime.dwLowDateTime,
        fileName,
        exitCode,
        parentProcessId,
        (ULONG64)detoursMaxMemHeapSize,
        (ULONG)*g_manifestSizePtr,
        (ULONG64)g_detoursHeapAllocatedMemoryInBytes,
        (ULONG)g_detoursAllocatedNoLockConcurentPoolEntries,
        (ULONG64)g_detoursMaxHandleHeapEntries,
        (ULONG64)g_detoursHandleHeapEntries);

    assert(constructReportResult > 0);

    if (constructReportResult > 0)
    {
        SendReportString(report.get());
    }
}
