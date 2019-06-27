// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"

#include "DebuggingHelpers.h"
#include "DetouredFunctions.h"
#include "DetoursHelpers.h"
#include "DetoursServices.h"
#include "FileAccessHelpers.h"
#include "StringOperations.h"
#include "UnicodeConverter.h"
#include "SubstituteProcessExecution.h"

using std::wstring;
using std::unique_ptr;
using std::vector;

/// Runs an injected substitute shim instead of the actual child process, passing the
/// original command and arguments to the shim along with, implicitly,
/// the current working directory and environment.
static BOOL WINAPI InjectShim(
    wstring               &commandWithoutQuotes,
    wstring               &argumentsWithoutCommand,
    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    BOOL                  bInheritHandles,
    DWORD                 dwCreationFlags,
    LPVOID                lpEnvironment,
    LPCWSTR               lpCurrentDirectory,
    LPSTARTUPINFOW        lpStartupInfo,
    LPPROCESS_INFORMATION lpProcessInformation)
{
    // Create a final buffer for the original command line - we prepend the original command
    // (if present) in quotes for easier parsing in the shim, ahead of the original argument list if provided.
    // This is an over-allocation because if lpCommandLine is non-null, lpCommandLine starts with
    // the contents of lpApplicationName, which we'll remove and replace with a quoted version.
    size_t fullCmdLineSizeInChars =
        commandWithoutQuotes.length() + argumentsWithoutCommand.length() +
        4;  // Command quotes and space and trailing null
    wchar_t *fullCommandLine = new wchar_t[fullCmdLineSizeInChars];
    if (fullCommandLine == nullptr)
    {
        Dbg(L"Failure running substitute shim process - failed to allocate buffer of size %d.", fullCmdLineSizeInChars * sizeof(WCHAR));
        SetLastError(ERROR_OUTOFMEMORY);
        return FALSE;
    }

    fullCommandLine[0] = L'"';
    wcscpy_s(fullCommandLine + 1, fullCmdLineSizeInChars, commandWithoutQuotes.c_str());
    wcscat_s(fullCommandLine, fullCmdLineSizeInChars, L"\" ");
    wcscat_s(fullCommandLine, fullCmdLineSizeInChars, argumentsWithoutCommand.c_str());

    Dbg(L"Injecting substitute shim '%s' for process command line '%s'", g_substituteProcessExecutionShimPath, fullCommandLine);
    BOOL rv = Real_CreateProcessW(
        /*lpApplicationName:*/ g_substituteProcessExecutionShimPath,
        /*lpCommandLine:*/ fullCommandLine,
        lpProcessAttributes,
        lpThreadAttributes,
        bInheritHandles,
        dwCreationFlags,
        lpEnvironment,
        lpCurrentDirectory,
        lpStartupInfo,
        lpProcessInformation);

    delete[] fullCommandLine;
    return rv;
}

// https://stackoverflow.com/questions/216823/whats-the-best-way-to-trim-stdstring
static inline const wchar_t *trim_start(const wchar_t *str)
{
    while (wmemchr(L" \t\n\r", *str, 4))  ++str;
    return str;
}

// https://stackoverflow.com/questions/216823/whats-the-best-way-to-trim-stdstring
static inline const wchar_t *trim_end(const wchar_t *end)
{
    while (wmemchr(L" \t\n\r", end[-1], 4)) --end;
    return end;
}

// https://stackoverflow.com/questions/216823/whats-the-best-way-to-trim-stdstring
static inline std::wstring trim(const wchar_t *buffer, size_t len) // trim a buffer (input?)
{
    return std::wstring(trim_start(buffer), trim_end(buffer + len));
}

// https://stackoverflow.com/questions/216823/whats-the-best-way-to-trim-stdstring
static inline void trim_inplace(std::wstring& str)
{
    str.assign(trim_start(str.c_str()),
        trim_end(str.c_str() + str.length()));
}

// Returns in 'command' the command from lpCommandLine without quotes, and in commandArgs the arguments from the remainder of the string.
static const void FindApplicationNameFromCommandLine(const wchar_t *lpCommandLine, _Out_ wstring &command, _Out_ wstring &commandArgs)
{
    wstring fullCommandLine(lpCommandLine);
    if (fullCommandLine.length() == 0)
    {
        command = wstring();
        commandArgs = wstring();
        return;
    }

    if (fullCommandLine[0] == L'"')
    {
        // Find the close quote. Might not be present which means the command
        // is the full command line minus the initial quote.
        size_t closeQuoteIndex = fullCommandLine.find('"', 1);
        if (closeQuoteIndex == wstring::npos)
        {
            // No close quote. Take everything through the end of the command line as the command.
            command = fullCommandLine.substr(1);
            trim_inplace(command);
            commandArgs = wstring();
        }
        else
        {
            if (closeQuoteIndex == fullCommandLine.length() - 1)
            {
                // Quotes cover entire command line.
                command = fullCommandLine.substr(1, fullCommandLine.length() - 2);
                trim_inplace(command);
                commandArgs = wstring();
            }
            else
            {
                wstring noQuoteCommand = fullCommandLine.substr(1, closeQuoteIndex - 1);

                // Find the next delimiting space after the close double-quote.
                // For example a command like "c:\program files"\foo we need to
                // keep \foo and cut the quotes to produce c:\program files\foo
                size_t spaceDelimiterIndex = fullCommandLine.find(L' ', closeQuoteIndex + 1);
                if (spaceDelimiterIndex == wstring::npos)
                {
                    // No space, take everything through the end of the command line.
                    spaceDelimiterIndex = fullCommandLine.length();
                }

                command = (noQuoteCommand +
                    fullCommandLine.substr(closeQuoteIndex + 1, spaceDelimiterIndex - closeQuoteIndex - 1));
                trim_inplace(command);
                commandArgs = fullCommandLine.substr(spaceDelimiterIndex + 1);
                trim_inplace(commandArgs);
            }
        }
    }
    else
    {
        // No open quote, pure space delimiter.
        size_t spaceDelimiterIndex = fullCommandLine.find(' ');
        if (spaceDelimiterIndex == wstring::npos)
        {
            // No space, take everything through the end of the command line.
            spaceDelimiterIndex = fullCommandLine.length();
        }

        command = fullCommandLine.substr(0, spaceDelimiterIndex);
        commandArgs = fullCommandLine.substr(spaceDelimiterIndex + 1);
        trim_inplace(commandArgs);
    }
}

static bool CommandArgsContainMatch(const wchar_t *commandArgs, const wchar_t *argMatch)
{
    if (argMatch == nullptr)
    {
        // No optional match, meaning always match.
        return true;
    }

    return wcsstr(commandArgs, argMatch) != nullptr;
}

static bool ShouldSubstituteShim(const wstring &command, const wchar_t *commandArgs)
{
    assert(g_substituteProcessExecutionShimPath != nullptr);

    // Easy cases.
    if (g_pShimProcessMatches == nullptr || g_pShimProcessMatches->empty())
    {
        // Shim everything or shim nothing if there are no matches to compare.
        return g_ProcessExecutionShimAllProcesses;
    }

    size_t commandLen = command.length();

    bool foundMatch = false;

    for (std::vector<ShimProcessMatch*>::iterator it = g_pShimProcessMatches->begin(); it != g_pShimProcessMatches->end(); ++it)
    {
        ShimProcessMatch *pMatch = *it;

        const wchar_t *processName = pMatch->ProcessName.get();
        size_t processLen = wcslen(processName);

        // lpAppName is longer than e.g. "cmd.exe", see if lpAppName ends with e.g. "\cmd.exe"
        if (processLen < commandLen)
        {
            if (command[commandLen - processLen - 1] == L'\\' &&
                _wcsicmp(command.c_str() + commandLen - processLen, processName) == 0)
            {
                if (CommandArgsContainMatch(commandArgs, pMatch->ArgumentMatch.get()))
                {
                    foundMatch = true;
                    break;
                }
            }

            continue;
        }

        if (processLen == commandLen)
        {
            if (_wcsicmp(processName, command.c_str()) == 0)
            {
                if (CommandArgsContainMatch(commandArgs, pMatch->ArgumentMatch.get()))
                {
                    foundMatch = true;
                    break;
                }
            }
        }
    }

    if (g_ProcessExecutionShimAllProcesses)
    {
        // A match means we don't want to shim - an opt-out list.
        return !foundMatch;
    }

    // An opt-in list, shim if matching.
    return foundMatch;
}

BOOL WINAPI MaybeInjectSubstituteProcessShim(
    _In_opt_    LPCWSTR               lpApplicationName,
    _In_opt_    LPCWSTR               lpCommandLine,
    _In_opt_    LPSECURITY_ATTRIBUTES lpProcessAttributes,
    _In_opt_    LPSECURITY_ATTRIBUTES lpThreadAttributes,
    _In_        BOOL                  bInheritHandles,
    _In_        DWORD                 dwCreationFlags,
    _In_opt_    LPVOID                lpEnvironment,
    _In_opt_    LPCWSTR               lpCurrentDirectory,
    _In_        LPSTARTUPINFOW        lpStartupInfo,
    _Out_       LPPROCESS_INFORMATION lpProcessInformation,
    _Out_       bool&                 injectedShim)
{
    if (g_substituteProcessExecutionShimPath != nullptr && (lpCommandLine != nullptr || lpApplicationName != nullptr))
    {
        // When lpCommandLine is null we just use lpApplicationName as the command line to parse.
        // When lpCommandLine is not null, it contains the command, possibly with quotes containing spaces,
        // as the first whitespace-delimited token; we can ignore lpApplicationName in this case.
        Dbg(L"Shim: Finding command and args from lpApplicationName='%s', lpCommandLine='%s'", lpApplicationName, lpCommandLine);
        LPCWSTR cmdLine = lpCommandLine == nullptr ? lpApplicationName : lpCommandLine;
        wstring command;
        wstring commandArgs;
        FindApplicationNameFromCommandLine(cmdLine, command, commandArgs);
        Dbg(L"Shim: Found command='%s', args='%s' from lpApplicationName='%s', lpCommandLine='%s'", command.c_str(), commandArgs.c_str(), lpApplicationName, lpCommandLine);

        if (ShouldSubstituteShim(command, commandArgs.c_str()))
        {
            // Instead of Detouring the child, run the requested shim
            // passing the original command line, but only for appropriate commands.
            injectedShim = true;
            return InjectShim(
                command,
                commandArgs,
                lpProcessAttributes,
                lpThreadAttributes,
                bInheritHandles,
                dwCreationFlags,
                lpEnvironment,
                lpCurrentDirectory,
                lpStartupInfo,
                lpProcessInformation);
        }
    }

    injectedShim = false;
    return FALSE;
}
