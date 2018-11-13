// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#include "PolicyResult.h"

PathValidity ProbePathForValidity(CanonicalizedPathType canonicalizedPath) {
#if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
    LPCWSTR path = canonicalizedPath.GetPathString();
    // Note that this unfortunately touches the disk, whereas we really just need to validate
    // that the path is parse-able on the target FS (e.g. ReFS doesn't allow stream syntax like .\A:X but NTFS does).
    DWORD maybeAttributes = GetFileAttributesW(path);

    if (maybeAttributes != INVALID_FILE_ATTRIBUTES) {
        return PathValidity::Valid;
    }

    DWORD error = ERROR_SUCCESS;
    if (maybeAttributes == INVALID_FILE_ATTRIBUTES) {
        error = GetLastError();
    }

    if (error == ERROR_PATH_NOT_FOUND) {
        // Unfortunately this will catch something like C:\foo\bar\"quoted thing" where C:\foo\bar doesn't exist.
        // If it did exist, we'd see ERROR_INVALID_NAME instead. But fortunately ERROR_PATH_NOT_FOUND is fairly
        // well an error condition - even for CreateDirectory - since file operations tend to act on leaves
        // (ERROR_FILE_NOT_FOUND denotes a leaf). Also it doesn't say *which* component didn't exist, so it is
        // fairly safe to preserve on denial.
        return PathValidity::PathComponentNotFound;
    }

    if (error == ERROR_INVALID_NAME) {
        return PathValidity::Invalid;
    }
#endif // !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)

    return PathValidity::Valid; // Optimism!
}

void PolicyResult::Initialize(CanonicalizedPathType path, PolicySearchCursor cursor)
{
    assert(IsIndeterminate());
    assert(cursor.IsValid());

    m_isIndeterminate = false;
    m_canonicalizedPath = path;
    m_policySearchCursor = cursor;

    // If the search for policy was truncated, we do not have an explicit policy in the manifest for the current path. In that case, the policy is defined 
	// by the last (directory) node that was found on the tree while looking for the full path. This is the cone policy.
    // So if the search was truncated, the policy to apply is the cone policy. Otherwise, it is the node policy.
	m_policy = m_policySearchCursor.SearchWasTruncated ? m_policySearchCursor.Record->GetConePolicy() : m_policySearchCursor.Record->GetNodePolicy();
}

AccessCheckResult PolicyResult::CheckReadAccess(RequestedReadAccess readAccessRequested, FileReadContext const& context) const
{
    assert(!IsIndeterminate());
    RequestedAccess accessRequested = static_cast<RequestedAccess>(readAccessRequested); // RequestedReadAccess is a subset of RequestedAccess.

    bool exists;
    switch (context.FileExistence)
    {
        case FileExistence::InvalidPath:
            // We silently ignore invalid paths, regardless of policy. The read operation itself has already happen (we have a context)
            // so Allow here just means 'use the authentic results and error code', rather than Deny in which we'd use our own (see CheckWriteAccess).
            return AccessCheckResult(accessRequested, ResultAction::Allow, ReportLevel::Ignore, PathValidity::Invalid);
        case FileExistence::Existent:
            exists = true;
            break;
        case FileExistence::Nonexistent:
            // Maybe we concluded non-existence due to ERROR_PATH_NOT_FOUND but the overall path is invalid. ERROR_FILE_NOT_FOUND is safe though.
            // In the former case, we might allow and report contingent upon AllowReadIfNonexistent.
            // TODO: This is inconsistent with write behavior, which sets ReportLevel::Ignore and returns ERROR_PATH_NOT_FOUND to the caller.
            exists = false;
            break;
        default:
            assert(false);
            exists = false;
    }

    // allowAccess: If true, we will have ResultAction::Allow. Otherwise we might hard-deny (::Deny) or warn (::Warn).
    // There are some special exclusions in addition to the effecive policy:
    //
    // - Accesses to a directory are always allowed (this includes probing the existence of a directory or opening a handle to it).
    //   BuildXL doesn't provide a way to declare a read/probe-dependency on a directory, and tools tend to emit many such innocuous probes. 
    //
    // - We might hard-deny or warn on access for single-file probes, but not enumeration-induced probes. 
    //   Historically we did not track enumeration and so failures / reports from enumeration probes were never evident (so doing so would be a breaking change).
    //   Note that these probes can still be reported, for example ::ReportExplicit when the Report policy is present.
    //   TODO: Revisit this if BuildXL gains a way to declare an enumeration dependency (on the directory) or probe-only dependencies (on the known contents).
    
    bool allowAccess = 
        context.OpenedDirectory ||
        (exists && AllowRead()) ||
        (!exists && AllowReadIfNonexistent()) ||
        (readAccessRequested == RequestedReadAccess::EnumerationProbe);

    ResultAction result = allowAccess 
        ? ResultAction::Allow 
        : (FailUnexpectedFileAccesses() ? ResultAction::Deny : ResultAction::Warn);

    bool explicitReport = !context.OpenedDirectory &&
        ((exists && ((m_policy & FileAccessPolicy_ReportAccessIfExistent) != 0)) ||
         (!exists && ((m_policy & FileAccessPolicy_ReportAccessIfNonExistent) != 0)));

    ReportLevel reportLevel = explicitReport 
        ? ReportLevel::ReportExplicit 
        : (ReportAnyAccess(result != ResultAction::Allow) ? ReportLevel::Report : ReportLevel::Ignore);

    if (result != ResultAction::Allow) {
        WriteWarningOrErrorF(L"Read access to file path '%s' is denied. Policy allows: 0x%08x.", GetCanonicalizedPath().GetPathString(), GetPolicy());
        MaybeBreakOnAccessDenied();
    }

    // TODO: In the deny case, we aren't ever returning PathValidity::PathComponentNotFound; so ERROR_PATH_NOT_FOUND is never returned in the Deny case.
    //       This is inconsistent with writes. Maybe ERROR_PATH_NOT_FOUND should always be allowed as a pass-through error like ERROR_INVALID_NAME.
    return AccessCheckResult(accessRequested, result, reportLevel, PathValidity::Valid);
}

AccessCheckResult PolicyResult::CreateAccessCheckResult(ResultAction result, ReportLevel reportLevel) const
{
    // We can safely assume the path is valid unless we'd otherwise deny or warn.
    PathValidity pathValidity = PathValidity::Valid;

    if (result != ResultAction::Allow) {
        pathValidity = ProbePathForValidity(m_canonicalizedPath);
        switch (pathValidity)
        {
            case PathValidity::Valid:
            case PathValidity::PathComponentNotFound:
                // The path was valid, so there's no path-validity excuse here (Deny or Warn as already determined).
                WriteWarningOrErrorF(L"Write access to file path '%s' is denied. Policy allows: 0x%08x.", GetCanonicalizedPath().GetPathString(), GetPolicy());
                MaybeBreakOnAccessDenied();
                break;
            case PathValidity::Invalid:
                // The path is at least possibly invalid, has an invalid syntax, and so don't report.
                reportLevel = ReportLevel::Ignore;
                break;
            default:
                assert(false);
        }
    }

    return AccessCheckResult(RequestedAccess::Write, result, reportLevel, pathValidity);
}

AccessCheckResult PolicyResult::CreateAccessCheckResult(bool isAllowed) const
{
    assert(!IsIndeterminate());

    ResultAction result = isAllowed
        ? ResultAction::Allow
        : (FailUnexpectedFileAccesses() ? ResultAction::Deny : ResultAction::Warn);

    ReportLevel reportLevel = ((m_policy & FileAccessPolicy_ReportAccess) != 0)
        ? ReportLevel::ReportExplicit
        : (ReportAnyAccess(result != ResultAction::Allow) ? ReportLevel::Report : ReportLevel::Ignore);

    return CreateAccessCheckResult(result, reportLevel);
}

AccessCheckResult PolicyResult::CheckExistingFileReadAccess() const { return CheckReadAccess(RequestedReadAccess::Read, FileReadContext(FileExistence::Existent)); }
AccessCheckResult PolicyResult::CheckWriteAccess() const            { return CreateAccessCheckResult(AllowWrite()); }
AccessCheckResult PolicyResult::CheckSymlinkCreationAccess() const  { return CreateAccessCheckResult(AllowSymlinkCreation()); }
AccessCheckResult PolicyResult::CheckCreateDirectoryAccess() const  { return CreateAccessCheckResult(AllowCreateDirectory()); }
AccessCheckResult PolicyResult::CheckDirectoryAccess(bool enforceCreationAccess) const
{
    return enforceCreationAccess
        ? CheckCreateDirectoryAccess()
        : CheckReadAccess(RequestedReadAccess::Probe, FileReadContext(FileExistence::Existent, true));
}

