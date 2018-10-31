// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------
//
// ETW-based tracing support and macros
// Note that ETW-based tracing is a BuildXL-specific addition to the MSR library.

#include "target.h"
#include <guiddef.h>

// {6BFBA0B6-A059-4ECA-B372-0B2E6A7821CC}
DEFINE_GUID(DetoursTraceProvider,
    0x6bfba0b6, 0xa059, 0x4eca, 0xb3, 0x72, 0xb, 0x2e, 0x6a, 0x78, 0x21, 0xcc);

// Required call to initialize tracing for the Detours library. Call this before using any Detours functions.
void DetourInitTracing();


#define DETOUR_TRACE_LEVEL(level, fmt, ...) DetourTraceStringFormat(level, L"[%S]" fmt, __FUNCTION__, __VA_ARGS__)

// Verbose-level traces; pre-ETW Detours trace macro usage ends up here (hence the L## shortcut for unicode).
#define __T(x) L ## x
#define _DETOUR_TRACE(fmt, ...) DETOUR_TRACE_LEVEL(5, __T(fmt), __VA_ARGS__)
#define DETOUR_TRACE(args) _DETOUR_TRACE ## args
#define _DETOUR_TRACEW(fmt, ...) DETOUR_TRACE_LEVEL(5, fmt, __VA_ARGS__)
#define DETOUR_TRACEW(args) _DETOUR_TRACEW ## args

// Non-verbose levels. Setting a level filter to 4 is fairly quiet since almost every noisy thing is at 5.
#define DETOUR_TRACE_VERBOSE(fmt, ...) DETOUR_TRACE_LEVEL(5, fmt, __VA_ARGS__)
#define DETOUR_TRACE_INFO(fmt, ...) DETOUR_TRACE_LEVEL(4, fmt, __VA_ARGS__)
#define DETOUR_TRACE_WARN(fmt, ...) DETOUR_TRACE_LEVEL(3, fmt, __VA_ARGS__)
#define DETOUR_TRACE_ERROR(fmt, ...) DETOUR_TRACE_LEVEL(2, fmt, __VA_ARGS__)

void DetourTraceStringFormat(UCHAR level, LPCWSTR format, ...);