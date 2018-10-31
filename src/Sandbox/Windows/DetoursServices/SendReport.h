// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#pragma once

#include <windows.h>

#include "DataTypes.h"
#include "PolicyResult.h"
#include "globals.h"

// ----------------------------------------------------------------------------
// FUNCTION DECLARATIONS
// ----------------------------------------------------------------------------

void ReportFileAccess(
    FileOperationContext const& fileOperationContext,
    FileAccessStatus status,
    PolicyResult const& policyResult,
    AccessCheckResult const& accessCheckResult,
    DWORD error,
    USN usn,
	wchar_t const* filter = nullptr);

void ReportProcessData(
    IO_COUNTERS const&  ioCounters,
    FILETIME const& creationTime,
    FILETIME const& exitTime,
    FILETIME const& kernelTime,
    FILETIME const& userTime,
    DWORD const& exitCode,
    DWORD const& parentProcessId,
    LONG64 const& detoursMaxMemHeapSize);

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
    const CreateDetouredProcessStatus createProcessStatus);
