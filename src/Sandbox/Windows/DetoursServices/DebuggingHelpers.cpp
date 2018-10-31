// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#include "stdafx.h"

#include "globals.h"
#include "DebuggingHelpers.h"
#include "FileAccessHelpers.h"
#include "DetoursServices.h"
#include "DetoursHelpers.h"
#include "buildXL_mem.h"

using std::unique_ptr;

// ----------------------------------------------------------------------------
// DEFINES
// ----------------------------------------------------------------------------

#define SUPER_VERBOSE 0

// ----------------------------------------------------------------------------
// STATIC FUNCTIONS
// ----------------------------------------------------------------------------

#ifdef DETOURS_SERVICES_NATIVES_LIBRARY

std::wstring DebugStringFormatArgs(PCWSTR formattedString, va_list args) {
    std::wstring failed = std::wstring(L"Failed DebuggingHelpers::DebugStringFormatArgs");
    int neededLength = _vscwprintf(formattedString, args);

    if (neededLength < 0) 
    {
        return failed;
    }

    // (+ 1) for '\0'.
    size_t length = (size_t)neededLength + 1;

    std::unique_ptr<wchar_t[]> formatted(new wchar_t[length]);
    int actualLength = _vsnwprintf_s(&formatted[0], length, length - 1, formattedString, args);
    return actualLength < 0 ? failed : std::wstring(formatted.get());
}

std::wstring DebugStringFormat(PCWSTR formattedString, ...) {
    va_list args;
    va_start(args, formattedString);
    std::wstring result = DebugStringFormatArgs(formattedString, args);
    va_end(args);
    return result;
}

void DebuggerOutputDebugString(PCWSTR text, bool shouldBreak)
{
    if (IsDebuggerPresent()) 
    {
        // Only call OutputDebugStringW/A if a debugger is attached because the functions involve raising exception, which is considerably expensive.
        // During integration with QuickBuild, we found that, for processes using cygwin, calling OutputDebugString, when no debugger is present,
        // can non-deterministically make exception raised inside OutputDebugString not caught by the except clause (see /minkernel/kernelbase/debug.c:205). 
        // Thus, the unhandled exception itself will crash the process. At this point, we don't know what changes cygwin makes to cause this peculiar behavior.
        // For certain, cygwin.dll also calls OutputDebugString with cryptic messages before and after calling DetoursService.dll, but we don't know
        // the effect of those calls.
        // We haven't really proved the non-deterministic behavior, we just observed that the exception was not caught, the best explanation we have for that 
        // is that it was some sort of stack corruption, especially since it was x86 only.
        // x64 uses table - based exception dispatch which would be immune from stack corruption, while x86 uses fs and the stack to do SEH which would explain 
        // why we only see it on x86
        OutputDebugStringW(text);

        if (shouldBreak) 
        {
            DebugBreak();
        }
    }
}

static void WriteMessage(PCWSTR text)
{
    DebuggerOutputDebugString(text, false);

    // We are going to write to STD_ERROR_HANDLE here, so we have to ensure that what we are going to write is not WCHAR.
    // Technically it isn't ANSI here, we should use the current console code page as the encoding (which can be done via CP_ACP), however, 
    // previously it looks like we assume the console is UTF8 encoding (which it probably is not - normal windows installs use 437 by default), 
    // it just so happens that most characters are the same between those two encodings which probably makes things work
    std::string ansiBuffer;
    DWORD ansiLength = (DWORD) WideCharToMultiByte(CP_ACP, 0, text, -1, NULL, 0, NULL, NULL);
    ansiBuffer.resize(ansiLength);
    DWORD ansiLength2 = (DWORD) WideCharToMultiByte(CP_ACP, 0, text, -1, const_cast<char*>(ansiBuffer.c_str()), (int) ansiLength, NULL, NULL);
    assert(ansiLength == ansiLength2);

#if SUPER_VERBOSE
    fputs(ansiBuffer.c_str(), stderr);
#endif

    if ((g_fileAccessManifestFlags & FileAccessManifestFlag::DiagnosticMessagesEnabled) != FileAccessManifestFlag::None) {
        HANDLE stdoutHandle = GetStdHandle(STD_ERROR_HANDLE);
        DWORD bytesTransferred;
        DWORD lastError = GetLastError();
        if (!WriteFile(stdoutHandle, ansiBuffer.c_str(), (DWORD) ansiBuffer.length(), &bytesTransferred, NULL)) {
            DWORD error = GetLastError();
            Dbg(L"Failed to write to stderr: %08X; ExitCode: %d", (int)error, DETOURS_PIPE_WRITE_ERROR_1);
            wprintf(L"Error: Failed to write to stderr: %08X; ExitCode: %d", (int)error, DETOURS_PIPE_WRITE_ERROR_1);
            fwprintf(stderr, L"Error: Failed to write to stderr: %08X; ExitCode: %d", (int)error, DETOURS_PIPE_WRITE_ERROR_1);
            HandleDetoursInjectionAndCommunicationErrors(DETOURS_PIPE_WRITE_ERROR_1, L"Failure writing message to pipe: exit(-43).", DETOURS_WINDOWS_LOG_MESSAGE_1);
        }

        SetLastError(lastError);
    }
}

// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

void HandleDetoursInjectionAndCommunicationErrors(int errorCode, LPCWSTR eventLogMsgPtr, LPCWSTR eventLogMsgId)
{
    fflush(stdout);
    fflush(stderr);
    std::wstring strMsg(eventLogMsgPtr);
    WriteToInternalErrorsFile(L"%s\r\n", eventLogMsgPtr);
    LogEventLogMessage(strMsg, EVENTLOG_ERROR_TYPE, EVENTLOG_ERROR_TYPE_ID, eventLogMsgId);
    if (HardExitOnErrorInDetours())
    {
        exit(errorCode);
    }
}

void Dbg(PCWSTR format, ...)
{
    va_list args;
    va_start(args, format);
    std::wstring resultArgs = DebugStringFormatArgs(format, args);
    va_end(args);

    DebuggerOutputDebugString(resultArgs.c_str(), false);

    if (g_reportFileHandle == NULL || g_reportFileHandle == INVALID_HANDLE_VALUE) {
        return;
    }

    std::wstring report = DebugStringFormat(L"%d,", ReportType_DebugMessage);
    report.append(resultArgs);
    report.append(L"\r\n");

    PCWSTR buffer = report.c_str();

#if SUPER_VERBOSE
    fputws(buffer, stderr);
#endif

    OVERLAPPED overlapped;
    ZeroMemory(&overlapped, sizeof(OVERLAPPED));
    // This offset specifies "append".
    overlapped.Offset = 0xFFFFFFFF;
    overlapped.OffsetHigh = 0xFFFFFFFF;

    size_t bufferLength = sizeof(wchar_t) * report.length(); // The size should be in bytes.
    DWORD bytesWritten;
    DWORD lastError = GetLastError();
    if (!WriteFile(g_reportFileHandle, buffer, (DWORD)bufferLength, &bytesWritten, &overlapped))
    {
        DWORD error = GetLastError();
        Dbg(L"Failed to write Dbg diagnostics line: %08X. Exiting with code %d.", (int)error, DETOURS_PIPE_WRITE_ERROR_2);
        wprintf(L"Error: Failed to write Dbg diagnostics line: %08X. Exiting with code %d.", (int)error, DETOURS_PIPE_WRITE_ERROR_2);
        fwprintf(stderr, L"Error: Failed to write Dbg diagnostics line: %08X. Exiting with code %d.", (int)error, DETOURS_PIPE_WRITE_ERROR_2);
        HandleDetoursInjectionAndCommunicationErrors(DETOURS_PIPE_WRITE_ERROR_2, L"Failure writing message to pipe: exit(-44).", DETOURS_WINDOWS_LOG_MESSAGE_2);
    }

    SetLastError(lastError);
}

void WriteWarningOrErrorF(PCWSTR format, ...)
{
    std::wstring prefixedFormat = FailUnexpectedFileAccesses() ? std::wstring(L"error : ") : std::wstring(L"warning : ");
    prefixedFormat.append(format);
    
    va_list args;
    va_start(args, format);
    std::wstring result = DebugStringFormatArgs(prefixedFormat.c_str(), args);
    va_end(args);

    result.append(L"\r\n");
    WriteMessage(result.c_str());
}

#endif // DETOURS_SERVICES_NATIVES_LIBRARY

#ifdef BUILDXL_NATIVES_LIBRARY
void Dbg(PCWSTR format, ...)
{
    UNREFERENCED_PARAMETER(format);
}

void HandleDetoursInjectionAndCommunicationErrors(int errorCode, LPCWSTR eventLogMsgPtr, LPCWSTR eventLogMsgId)
{
    UNREFERENCED_PARAMETER(errorCode);
    UNREFERENCED_PARAMETER(eventLogMsgPtr);
    UNREFERENCED_PARAMETER(eventLogMsgId);

    fflush(stdout);
    fflush(stderr);
}
#endif // BUILDXL_NATIVES_LIBRARY

// This flag allows to attach with debugger to debug problems without having to hit
// always this DebugBreak. One can set a bpt here and set the var to true from within the debugger.
// Then DebugBreak will break.
static bool g_allowBreakOnAccessDenied = false;

void MaybeBreakOnAccessDenied()
{
    if (g_allowBreakOnAccessDenied && IsDebuggerPresent()) {
        DebugBreak();
        return;
    }

    if (g_BreakOnAccessDenied) {
#if SUPER_VERBOSE
        Dbg(L"g_BreakOnAccessDenied is true, and access was denied.  Breaking into debugger.");
#endif // SUPER_VERBOSE
        DebugBreak();
    }
}


#undef SUPER_VERBOSE
