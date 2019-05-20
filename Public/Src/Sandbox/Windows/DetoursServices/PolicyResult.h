// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include "FileAccessHelpers.h"

#if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
#include "CanonicalizedPath.h"
typedef CanonicalizedPath CanonicalizedPathType;
#else // !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
typedef PCPathChar CanonicalizedPathType;
#endif // !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)

// Result of determining an access policy for a path. This involves canonicalizing the desired path and performing a policy lookup.
class PolicyResult
{
private:
    CanonicalizedPathType m_canonicalizedPath;

    // Effective policy. If m_policySearchCursor is valid, this should agree;
    // otherwise, we may still have a policy without a cursor due to special case rules.
    FileAccessPolicy m_policy;
    PolicySearchCursor m_policySearchCursor;

    // Indicates if this is an invalid policy result (Initialize not called, or it failed).
    bool m_isIndeterminate;

public:
    // Assumes that the search was already performed and that `cursor` is pointing
    // to the correct ManifestRecord node.
    //
    // It doesn't perform any additional policy search, it simply initializes the
    // following state: m_canonicalizedPath, m_policy, m_policySearchCursor.; it 
    // also sets m_isIndeterminate to false.
    //
    // It does check the cursor to see if the search was truncated, and if so clears
    // the `FileAccessPolicy_ExactPathPolicies` bits in the stored `m_policy` field.
    void Initialize(CanonicalizedPathType path, PolicySearchCursor cursor);

    PolicyResult(const PolicyResult& other) = default;
    PolicyResult& operator=(const PolicyResult&) = default;
    
    CanonicalizedPathType Path() const        { return m_canonicalizedPath; }
    void SetPath(CanonicalizedPathType path)  { m_canonicalizedPath = path; }

#if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)

private:
    // Result of path translation.
    std::wstring m_translatedPath;

    /// Checks the file access manifest to determine a policy for the given already-canonicalized path.
    /// The policy search is resumed from the given cursor, applying searchSuffix. The path generating policySearchCursor combined with searchSuffix
    /// must be equivalent to canonicalizedPath (we are avoiding wasted work in re-traversing some prefix of canonicalizedPath in the policy tree).
    void InitializeFromCursor(CanonicalizedPathType const& canonicalizedPath, PolicySearchCursor const& policySearchCursor, PCPathChar const searchSuffix);

public:
    PolicyResult()
        : m_canonicalizedPath(),
        m_policy((FileAccessPolicy)0), m_policySearchCursor(),
        m_isIndeterminate(true) // Note that until Initialize is called, we are indeterminate.
    {
    }

    PolicyResult(PolicyResult&& other)
        : m_canonicalizedPath(std::move(other.m_canonicalizedPath)),
        m_policy(other.m_policy), m_policySearchCursor(other.m_policySearchCursor),
        m_isIndeterminate(other.m_isIndeterminate),
        m_translatedPath(other.m_translatedPath)
    {
        other.m_isIndeterminate = true;
        other.m_policy = (FileAccessPolicy)0;
        other.m_policySearchCursor = PolicySearchCursor();
        other.m_translatedPath.clear();
        
        assert(other.m_canonicalizedPath.Type == PathType::Null);
        assert(other.m_policySearchCursor.Record == nullptr);
    }

    /// Checks the file access manifest to determine a policy for the given path (not yet canonicalized).
    ///
    /// The return value indicates if policy determination succeeded, which should almost always be the case.
    /// If 'false' is returned, the caller should fail the access and report the failure with ReportIndeterminatePolicyAndSetLastError.
    bool Initialize(PCPathChar path);

    /// Checks the file access manifest to determine a policy for the given already-canonicalized path.
    void Initialize(CanonicalizedPathType const& canonicalizedPath);

    // Sends a report with FileAccessStatus_CannotDeterminePolicy and calls SetLastError to indicate failure to callers.
    // This may only be called when Initialize returned false (thus IsIndeterminate), indicating a failure to detemrine policy.
    // TODO: This is a poorly exercised and very exceptional path; for simplicity consider throwing (failfast exception?)
    void ReportIndeterminatePolicyAndSetLastError(FileOperationContext const& fileOperationContext) const;

    PCPathChar const GetTranslatedPath() const { return m_translatedPath.c_str(); }
    
    PCPathChar const GetTranslatedPathWithoutTypePrefix() const {
        switch (m_canonicalizedPath.Type) {
            case PathType::Null:
                return nullptr;
            case PathType::Win32:
                return m_translatedPath.c_str();
            case Win32Nt:
            case LocalDevice:
                return m_translatedPath.c_str() + 4;
            default:
                assert(false);
                return nullptr;
        }
    }
#else // !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
    
private:
    FileAccessManifestFlag m_famFlag;

public:
    PolicyResult(FileAccessManifestFlag famFlag)
        : m_famFlag(famFlag), m_isIndeterminate(true)
    {
    }
    
    PolicyResult(FileAccessManifestFlag famFlag, CanonicalizedPathType path, PolicySearchCursor cursor)
        : PolicyResult(famFlag)
    {
        Initialize(path, cursor);
    }

    #define GEN_CHECK_FAM_FLAG_FUNC(flag_name, flag_value) inline bool flag_name() const { return Check##flag_name(m_famFlag); }
    FOR_ALL_FAM_FLAGS(GEN_CHECK_FAM_FLAG_FUNC)
    inline bool ReportAnyAccess(bool accessDenied) const { return CheckReportAnyAccess(m_famFlag, accessDenied); }

#endif // !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
    
    // Performs an access check for a read-access, based on dynamically-observed read context (existence, etc.)
    // May only be called when !IsIndeterminate().
    AccessCheckResult CheckReadAccess(RequestedReadAccess readAccessRequested, FileReadContext const& context) const;
    
    // Performs CheckRead access for and existing file.
    AccessCheckResult CheckExistingFileReadAccess() const;

    // Performs an access check for a write-access, based only on static policy in the manifest (not existence, etc.)
    // May only be called when !IsIndeterminate().
    AccessCheckResult CheckWriteAccess() const;

    // Performs an access check for creating a symlink, based only on static policy in the manifest (not existence, etc.)
    // May only be called when !IsIndeterminate().
    AccessCheckResult CheckSymlinkCreationAccess() const;

    // Performs an access check for a CreateDirectory-access, based only on static policy in the manifest (not existence, etc.)
    // May only be called when !IsIndeterminate().
    AccessCheckResult CheckCreateDirectoryAccess() const;
    
    AccessCheckResult CheckDirectoryAccess(bool enforceCreationAccess) const;

    // Determines a policy result for the combined path GetCanonicalizedPath() + pathSuffix.
    PolicyResult GetPolicyForSubpath(wchar_t const* pathSuffix) const;

    CanonicalizedPathType const& GetCanonicalizedPath() const { return m_canonicalizedPath; }
    bool AllowRead() const { return (m_policy & FileAccessPolicy_AllowRead) != 0; }
    bool AllowReadIfNonexistent() const { return (m_policy & FileAccessPolicy_AllowReadIfNonExistent) != 0; }
    bool AllowWrite() const { return (m_policy & FileAccessPolicy_AllowWrite) != 0; }
    bool AllowSymlinkCreation() const { return (m_policy & FileAccessPolicy_AllowSymlinkCreation) != 0; }
    bool AllowCreateDirectory() const { return (m_policy & FileAccessPolicy_AllowCreateDirectory) != 0; }
	bool AllowRealInputTimestamps() const { return (m_policy & FileAccessPolicy_AllowRealInputTimestamps) != 0; }
    bool ReportUsnAfterOpen() const { return (m_policy & FileAccessPolicy_ReportUsnAfterOpen) != 0; }
    bool ReportDirectoryEnumeration() const { return (m_policy & FileAccessPolicy_ReportDirectoryEnumerationAccess) != 0; }
    bool IndicateUntracked() const { return ((m_policy & FileAccessPolicy_AllowAll) == FileAccessPolicy_AllowAll) && ((m_policy & FileAccessPolicy_ReportAccess) == 0); }
    DWORD GetPathId() const { return m_policySearchCursor.IsValid() ? m_policySearchCursor.Record->GetPathId() : 0; }
    FileAccessPolicy GetPolicy() const { return m_policy; }
    USN GetExpectedUsn() const { return m_policySearchCursor.GetExpectedUsn(); }
    // Indicates if this policy is invalid (iff Initialize did not complete successfully or has not been called).
    bool IsIndeterminate() const { return m_isIndeterminate; }

    // Indicates if a file-open should have FILE_SHARE_READ implicitly added (as a hack to workaround tools accidentally
    // asking for exclusive read). We are conservative here:
    // - If the process is allowed to write the file, we leave it to their discretion (even if they did not ask for write access on a particular handle).
    // - If the access result is Warn or Deny, we leave it to their discretion (maybe the access is whitelisted, and the policy should really have AllowWrite).
    bool ShouldForceReadSharing(AccessCheckResult const& accessCheck) {
        return !AllowWrite() && accessCheck.ResultAction == ResultAction::Allow;
    }

    // Indicates if the timestamps of this file should be virtualized to a known value.
    bool ShouldOverrideTimestamps(AccessCheckResult const& accessCheck) const {
        return (accessCheck.ResultAction == ResultAction::Allow || accessCheck.ResultAction == ResultAction::Warn) && !AllowRealInputTimestamps();
    }

private:
    /// Performs common work when checking for writable access
    AccessCheckResult CreateAccessCheckResult(ResultAction result, ReportLevel reportLevel) const;
    AccessCheckResult CreateAccessCheckResult(bool isAllowed) const;
};
