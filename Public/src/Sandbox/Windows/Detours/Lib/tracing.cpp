// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------
//
// ETW-based tracing support
// Note that ETW-based tracing is a BuildXL-specific addition to the MSR library.

#include "target.h"
#include <Windows.h>
#include <Evntprov.h>
#include <stdio.h>

// This is the compilation unit which actually defines rather than declares tracing GUIDs
#include <initguid.h>
#include "tracing.h"

REGHANDLE g_DetoursTraceHandle;

void DetourInitTracing() {
    if (EventRegister(&DetoursTraceProvider, /*EnableCallback*/ NULL, /*CallbackContext*/ NULL, &g_DetoursTraceHandle) != 0) {
        OutputDebugStringW(L"Failed to initialize Detours tracing (EventRegister failed)");
    }
}

void DetourTraceStringFormat(UCHAR level, LPCWSTR format, ...) {
    DWORD error = GetLastError();

    if (g_DetoursTraceHandle == 0 || !EventProviderEnabled(g_DetoursTraceHandle, level, ~0U)) {
        SetLastError(error);
        return;
    }

    va_list argptr;
    va_start(argptr, format);

    wchar_t buf[256];
    _vsnwprintf_s(buf, _TRUNCATE, format, argptr);
    EventWriteString(g_DetoursTraceHandle, level, /*keyword*/ 0, buf);
    SetLastError(error);
}