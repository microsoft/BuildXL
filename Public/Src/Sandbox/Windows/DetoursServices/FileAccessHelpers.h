// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "stdafx.h"

#if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
#include "globals.h"
#include <string>
#endif // if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)

#include "DataTypes.h"
#include "PolicySearch.h"

#if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
typedef wchar_t const* StrType;
#else // !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
typedef char const* StrType;
#endif // !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)

// Represents the (semi-)static context of a detoured call's eventual access to a file. This context includes that information
// obtained directly from the calling process and the nature of the call in question (operation name, open mode, raw path, etc.)
// Note that this context is meant to live within the operation's stack; it may contain a pointer to the non-canonical path as
// passed in to the detoured call.
class FileOperationContext
{
public:
    StrType Operation;
    StrType NoncanonicalPath;
    DWORD DesiredAccess;
    DWORD ShareMode;
    DWORD CreationDisposition;
    DWORD FlagsAndAttributes;

    FileOperationContext(
        StrType lpOperation,
        DWORD dwDesiredAccess,
        DWORD dwShareMode,
        DWORD dwCreationDisposition,
        DWORD dwFlagsAndAttributes,
        StrType lpPath) :
        Operation(lpOperation),
        NoncanonicalPath(lpPath),
        DesiredAccess(dwDesiredAccess),
        ShareMode(dwShareMode),
        CreationDisposition(dwCreationDisposition),
        FlagsAndAttributes(dwFlagsAndAttributes)
    {}
    
    // Creates a call context for an operation on a path that reads existing content.
    // (this fills in convincing CreateFile-like parameters).
    static FileOperationContext CreateForRead(StrType lpOperation, StrType lpPath)
    {
        return FileOperationContext(
            lpOperation,
            GENERIC_READ,
            FILE_SHARE_READ,
            OPEN_EXISTING,
            0x08100000,
            lpPath);
    }
    
    static FileOperationContext CreateForWrite(StrType lpOperation, StrType lpPath)
    {
        return FileOperationContext(
            lpOperation,
            GENERIC_WRITE,
            FILE_SHARE_READ,
            OPEN_ALWAYS,
            0x08100000,
            lpPath);
    }

    FileOperationContext(const FileOperationContext& other) = default;
    FileOperationContext& operator=(const FileOperationContext&) = default;
};

enum FileExistence {
    Existent,
    Nonexistent,
    InvalidPath,
};

// Represents the dynamic reporting context of a file read-access. The dynamic reporting context
// includes that information obtained from actual disk access which determines whether or not the 
// access should be allowed / reported.
class FileReadContext
{
public:
    FileReadContext(FileExistence fileExistence = FileExistence::Nonexistent, bool openedDirectory = false)
        : FileExistence(fileExistence), OpenedDirectory(openedDirectory)
    {}

    FileExistence FileExistence;
    bool OpenedDirectory;

    void InferExistenceFromError(DWORD error);
    void InferExistenceFromNtStatus(NTSTATUS status);
};

enum ReportLevel {
    Ignore,
    Report,
    ReportExplicit
};

enum ResultAction {
    Allow,
    Deny,
    Warn
};

enum PathValidity {
    Valid,
    // We observed ERROR_PATH_NOT_FOUND (not ERROR_FILE_NOT_FOUND); unfortunately this is possible
    // with C:\foo\"bar" where C:\foo doesn't exist; if it did, we'd get ERROR_INVALID_NAME for "bar".
    PathComponentNotFound,
    // We observed ERROR_INVALID_NAME (so maybe we have some path like C:\foo\"bar" for an existent C:\foo).
    Invalid,
};

// Type of read access requested to produce an AccessCheckResult (via CheckReadAccess).
enum class RequestedReadAccess {
    None = 0x0,
    Read = 0x1,
    Probe = 0x4,
    Enumerate = 0x8,
    EnumerationProbe = 0x10,
    Lookup = 0x20,
};

// Access (e.g. write) requested to produce an AccessCheckResult.
enum class RequestedAccess {
    None = 0x0,
    Read = (int)RequestedReadAccess::Read,
    Write = 0x2,
    Probe = (int)RequestedReadAccess::Probe,
    Enumerate = (int)RequestedReadAccess::Enumerate,
    EnumerationProbe = (int)RequestedReadAccess::EnumerationProbe,
    Lookup = (int)RequestedReadAccess::Lookup
};

// RequestedAccess should be combinable with | since it is flags.
DEFINE_ENUM_FLAG_OPERATORS(RequestedAccess)

// Represents the result of performing an access check (applying a PolicyResult to a proposed access and context such as file existence).
class AccessCheckResult 
{
private:
    AccessCheckResult() {}

public:
    static inline AccessCheckResult Invalid() { return AccessCheckResult(); }
    
    AccessCheckResult(RequestedAccess requestedAccess, ResultAction result, ReportLevel reportLevel) :
        RequestedAccess(requestedAccess), ResultAction(result), ReportLevel(reportLevel), PathValidity(PathValidity::Valid)
    {
    }

    AccessCheckResult(RequestedAccess requestedAccess, ResultAction result, ReportLevel reportLevel, PathValidity pathValidity) :
        RequestedAccess(requestedAccess), ResultAction(result), ReportLevel(reportLevel), PathValidity(pathValidity)
    {
    }

    RequestedAccess RequestedAccess;
    ResultAction ResultAction;
    ReportLevel ReportLevel;
    PathValidity PathValidity;
    
    // Indicates if a report should be sent for this access.
    bool ShouldReport() const {
        return ReportLevel == ReportLevel::Report || ReportLevel == ReportLevel::ReportExplicit;
    }
    
    // Returns a corresponding report line status. Note that warning-level access failures (allowed to proceed) map to FileAccessStatus_Denied.
    FileAccessStatus GetFileAccessStatus() const {
        return (ResultAction != ResultAction::Allow) ? FileAccessStatus_Denied : FileAccessStatus_Allowed;
    }

    // Indicates if access to a file should be denied entirely (i.e., return an invalid handle and some error such as ERROR_ACCESS_DENIED).
    // Note that this is dependent upon the global FailUnexpectedFileAccesses() flag.
    bool ShouldDenyAccess() const {
        return ResultAction == ResultAction::Deny; // Check*Access would have set Warn if !FailUnexpectedFileAccesses().
    }

    // Returns an error code (suitable for SetLastError) that should be reported on denial (ResultAction::Deny).
    // It is an error to call this method when ResultAction is not ResultAction::Deny.
    DWORD DenialError() const {
        assert(ShouldDenyAccess());

        switch (PathValidity) {
        case PathValidity::Valid:
            return ERROR_ACCESS_DENIED;
        case PathValidity::PathComponentNotFound:
            return ERROR_PATH_NOT_FOUND;
        case PathValidity::Invalid:
            return ERROR_INVALID_NAME;
        }

        assert(false);
        return 0;
    }

    // Returns an NTSTATUS that should be reported on denial (ResultAction::Deny).
    // It is an error to call this method when ResultAction is not ResultAction::Deny.
    NTSTATUS DenialNtStatus() const {
        const NTSTATUS StatusAccessDenied = 0xC0000022L;
        const NTSTATUS StatusObjectNameInvalid = 0xC0000033L;
        const NTSTATUS StatusObjectPathNotFound = 0xC000003AL;

        assert(ShouldDenyAccess());

        switch (PathValidity) {
        case PathValidity::Valid:
            return StatusAccessDenied;
        case PathValidity::PathComponentNotFound:
            return StatusObjectPathNotFound;
        case PathValidity::Invalid:
            return StatusObjectNameInvalid;
        }

        assert(false);
        return 0;
    }

    // Returns a new AccessCheckResult that is a copy of this one, but with the specified report level.
    AccessCheckResult With(::ReportLevel newReportLevel) {
        AccessCheckResult newAccessCheck = *this;
        newAccessCheck.ReportLevel = newReportLevel;
        return newAccessCheck;
    }

    // Combines two access checks by taking most restrictive action and highest report levels.
    static AccessCheckResult Combine(AccessCheckResult const& left, AccessCheckResult const& right) {
        ::ResultAction combinedResultAction = left.ResultAction;
        ::ReportLevel combinedReportLevel = left.ReportLevel;
        ::PathValidity combinedPathValidity = left.PathValidity;
        ::RequestedAccess combinedRequestedAccess = left.RequestedAccess | right.RequestedAccess;

        if (right.ResultAction == ResultAction::Deny) {
            combinedResultAction = ResultAction::Deny;
        }
        else if (right.ResultAction == ResultAction::Warn && combinedResultAction == ResultAction::Allow) {
            combinedResultAction = ResultAction::Warn;
        }

        if (right.ReportLevel == ReportLevel::ReportExplicit) {
            combinedReportLevel = ReportLevel::ReportExplicit;
        }
        else if (right.ReportLevel == ReportLevel::Report && combinedReportLevel == ReportLevel::Ignore) {
            combinedReportLevel = ReportLevel::Report;
        }

        if (right.PathValidity == PathValidity::Invalid) {
            combinedPathValidity = PathValidity::Invalid;
        }
        else if (right.PathValidity == PathValidity::PathComponentNotFound && combinedPathValidity == PathValidity::Valid) {
            combinedPathValidity = PathValidity::PathComponentNotFound;
        }

        return AccessCheckResult(combinedRequestedAccess, combinedResultAction, combinedReportLevel, combinedPathValidity);
    }

    // Returns an access-check with an action of Deny or Warn (based on global settings for unexpected file accesses).
    // The report level is set to Ignore. This is a useful operand for Combine.
    static AccessCheckResult DenyOrWarn(::RequestedAccess requestedAccess);

    AccessCheckResult(const AccessCheckResult& other) = default;
    AccessCheckResult& operator=(const AccessCheckResult&) = default;

#if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
    // Calls SetLastError with DenialError.
    // It is an error to call this method when ResultAction is not ResultAction::Deny.
    void SetLastErrorToDenialError() const {
        SetLastError(DenialError());
    }
#endif // !(MAC_OS_SANDBOX)
};

enum PathType {
    // No path represented.
    Null,
    // e.g. \\?\ or \??\ prefix; no canonicalization of .., . etc or use of working directory.
    Win32Nt,
    // \\.\ prefix; canonicalization of .., ., etc. is in effect, but no use of working directory. May refer to e.g. \\.\pipe rather than a drive letter.
    LocalDevice,
    // Vanilla Win32 path such as C:\foo\..\bar
    Win32
};

// ----------------------------------------------------------------------------
// INLINE FUNCTION DEFINITIONS
// ----------------------------------------------------------------------------

#if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)

#define GEN_CHECK_GLOBAL_FAM_FLAG(flag_name, flag_value) \
inline bool flag_name()         { return Check##flag_name(g_fileAccessManifestFlags); } \
inline bool Should##flag_name() { return Check##flag_name(g_fileAccessManifestFlags); }

FOR_ALL_FAM_FLAGS(GEN_CHECK_GLOBAL_FAM_FLAG)
inline bool ReportAnyAccess(bool accessDenied) { return CheckReportAnyAccess(g_fileAccessManifestFlags, accessDenied); }

inline LPCTSTR InternalDetoursErrorNotificationFile()
{
    return g_internalDetoursErrorNotificationFile.c_str();
}

inline bool IsNullOrEmptyA(LPCSTR lpFileName)
{
    return (lpFileName == NULL || lpFileName[0] == 0);
}

inline bool IsNullOrEmptyW(LPCWSTR lpFileName)
{
    return (lpFileName == NULL || lpFileName[0] == 0);
}

inline bool IsNullOrInvalidHandle(HANDLE h)
{
    return (h == NULL || h == INVALID_HANDLE_VALUE);
}

#endif // !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
