// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "stdafx.h"

#include <string>

#include "CanonicalizedPath.h"
#include "DataTypes.h"
#include "DebuggingHelpers.h"
#include "DetoursServices.h"
#include "DetoursHelpers.h"
#include "StringOperations.h"
#include "FileAccessHelpers.h"
#include "SendReport.h"
#include "buildXL_mem.h"
using std::unique_ptr;
using std::move;

// ----------------------------------------------------------------------------
// FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

AccessCheckResult AccessCheckResult::DenyOrWarn(::RequestedAccess requestedAccess) {
    return AccessCheckResult(
        requestedAccess,
        FailUnexpectedFileAccesses() ? ResultAction::Deny : ResultAction::Warn, 
        ReportAnyAccess(true) ? ReportLevel::Report : ReportLevel::Ignore);
}

// CODESYNC: BuildXL.Native.IO.Windows.FileSystemWin.IsHresultNonesixtent (in FileSystem.Win.cs)
static bool IsPathNonexistent(DWORD error)
{
    // The particular error depends on if a final or non-final path component was not found.
    // Treat "Device not ready" error (say a DVD with no disk in it) as a file not found.
    // This way a read probe on the file will result in file Nonexistent state, which will be handled 
    // properly by BuildXL. This is a fix for bug 699196.
    // Also, treat the FVE_E_LOCKED_VOLUME as file not found as well. This way a read probe on locked 
    // drive will result in file Nonexistent.
    return error == ERROR_PATH_NOT_FOUND ||
           error == ERROR_FILE_NOT_FOUND ||
           error == ERROR_NOT_READY ||
           error == FVE_E_LOCKED_VOLUME ||
           error == ERROR_BAD_PATHNAME;
}

void FileReadContext::InferExistenceFromError(DWORD error) {
    if (IsPathNonexistent(error)) {
        FileExistence = FileExistence::Nonexistent;
    }
    else if (error == ERROR_INVALID_NAME) {
        FileExistence = FileExistence::InvalidPath;
    } 
    else {
        FileExistence = FileExistence::Existent;
    }
}

void FileReadContext::InferExistenceFromNtStatus(NTSTATUS status) {
    InferExistenceFromError(RtlNtStatusToDosError(status));
}
