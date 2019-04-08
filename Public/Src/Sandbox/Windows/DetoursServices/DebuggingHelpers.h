// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "stdafx.h"

// ----------------------------------------------------------------------------
// FUNCTION DECLARATIONS
// ----------------------------------------------------------------------------

std::wstring DebugStringFormatArgs(PCWSTR formattedString, va_list args);
std::wstring DebugStringFormat(PCWSTR formattedString, ...);

#if MAC_OS_LIBRARY || MAC_OS_SANDBOX
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wmissing-declarations"
#endif

void DebuggerOutputDebugString(PCWSTR text, bool shouldBreak);
void Dbg(PCWSTR format, ...);

/*

Writes a diagnostic line to the standard error channel.  The diagnostic line is either a warning
or an error.  If the manifest flag FailUnexpectedFileAccesses is true, then the output line
is an error, and otherwise is a warning.  Error lines are prefixed with "error : ", and warning
lines are prefixed with "warning : ".  The warning will also be sent to the debugger port, using
the Win32 OutputDebugString() API if a debugger is attached.

This method has no effect (aside from writing to the debugger port) if the manifest flag
DiagnosticMessagesEnabled is false.

Note that this method takes Unicode format strings (and arguments), but the text is written to
standard error as a UTF-8 string.  Most tools expect 8-bit diagnostic messages on standard error
and standard output channels, and will usually fail to interpret UTF-16 text correctly, or will
even fail entirely.  For the ASCII subset of Unicode, UTF-8 is encoded the same as ASCII, so for
many output messages, using UTF-8 is indistinguishable from using ASCII.  For messages that do
contain characters outside of ASCII, most tools will simply pass the encoded form through without
mangling it, so UTF-8 is a good "pass-through" encoding.

*/
void WriteWarningOrErrorF(PCWSTR format, ...);

void MaybeBreakOnAccessDenied();

#if MAC_OS_LIBRARY || MAC_OS_SANDBOX
#pragma clang diagnostic pop
#endif
