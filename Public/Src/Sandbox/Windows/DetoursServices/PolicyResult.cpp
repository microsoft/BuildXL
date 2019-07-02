// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "PolicyResult.h"
#include "DetoursHelpers.h"
#include "SendReport.h"
#include "FilesCheckedForAccess.h"

bool PolicyResult::Initialize(PCPathChar path)
{
    assert(m_isIndeterminate);
    assert(path);

    CanonicalizedPathType canonicalizedPath = CanonicalizedPath::Canonicalize(path);
    if (canonicalizedPath.IsNull()) {
        // This policy remains indeterminate.
        return false;
    }

    Initialize(canonicalizedPath);
    return true;
}

void PolicyResult::Initialize(CanonicalizedPathType const& canonicalizedPath)
{
    // Initializing from a canonicalized path without a cursor; use the global tree root as the start cursor, and the entire path (without the type prefix)
    // as the search 'suffix' (we aren't resuming a search - we are starting a new one).
    // For reporting it is important that we preserve the \\?\ or \??\ prefix; \\?\C: and C: are different!
    // The former refers to a device. The other is drive-relative (based on current directory of that drive).
    // But for evaluating special cases and traversing the manifest tree, we strip the prefix (the tree shouldn't have \\?\ in it for example).
    InitializeFromCursor(canonicalizedPath, g_manifestTreeRoot, nullptr);
}

void PolicyResult::InitializeFromCursor(CanonicalizedPathType const& canonicalizedPath, PolicySearchCursor const& policySearchCursor, PCPathChar const searchSuffix)
{
    assert(m_isIndeterminate);
    assert(m_canonicalizedPath.IsNull());
    assert(!canonicalizedPath.IsNull());

    // The path is already canonicalized; now we are committed to set a policy, which doesn't fail.
    // We will do so via special-case rules (no policy search or cursor) or via the policy tree (which is searched, producing a cursor). 
    m_canonicalizedPath = canonicalizedPath;

    TranslateFilePath(std::wstring(canonicalizedPath.GetPathString()), m_translatedPath, false);
    wchar_t const* translatedSearchSuffix = searchSuffix != nullptr ? searchSuffix : GetTranslatedPathWithoutTypePrefix();
    size_t searchSuffixLength = wcslen(translatedSearchSuffix);

    PolicySearchCursor newCursor = FindFileAccessPolicyInTreeEx(policySearchCursor, translatedSearchSuffix, searchSuffixLength);
    Initialize(canonicalizedPath, newCursor);

    if (GetSpecialCaseRulesForCoverageAndSpecialDevices(translatedSearchSuffix, searchSuffixLength, canonicalizedPath.Type, /*out*/ m_policy)) {
#if SUPER_VERBOSE
        Dbg(L"match (special case rules.1): %s - policySearchCursor: %x, searchSuffix: %s", canonicalizedPath.GetPathString(), policySearchCursor, searchSuffix);
#endif // SUPER_VERBOSE
    }
    else
    {
        if (GetSpecialCaseRulesForSpecialTools(translatedSearchSuffix, searchSuffixLength, /*out*/ m_policy))
        {
#if SUPER_VERBOSE
            Dbg(L"match (special case rules.2): %s - policySearchCursor: %x, searchSuffix: %s", canonicalizedPath.GetPathString(), policySearchCursor, searchSuffix);
#endif // SUPER_VERBOSE
        }
    }
}

PolicyResult PolicyResult::GetPolicyForSubpath(wchar_t const* pathSuffix) const {
    assert(!m_isIndeterminate);
    assert(!m_canonicalizedPath.IsNull());

    size_t extensionStartIndex = 0;
    CanonicalizedPathType extendedPath = m_canonicalizedPath.Extend(pathSuffix, &extensionStartIndex);

    PolicyResult subpolicy;
    if (m_policySearchCursor.IsValid()) {
        subpolicy.InitializeFromCursor(extendedPath, m_policySearchCursor, &extendedPath.GetPathString()[extensionStartIndex]);
    }
    else {
        subpolicy.Initialize(extendedPath);
    }

    return subpolicy;
}

void PolicyResult::ReportIndeterminatePolicyAndSetLastError(FileOperationContext const& fileOperationContext) const
{
    assert(IsIndeterminate());

    WriteWarningOrErrorF(L"Could not determine policy for file path '%s'.",
        fileOperationContext.NoncanonicalPath);
    MaybeBreakOnAccessDenied();

    // We certainly are not allowing an access, and are not reporting due to an explicit ask of the calling engine.
    // This is a bit odd but really only relevant to this case, and presently just informs the 'explicit report' flag.
    // TODO: Could have a ReportFileAccess overload instead.
    AccessCheckResult fakeAccessCheck = AccessCheckResult(RequestedAccess::None, ResultAction::Deny, ReportLevel::Report);

    ReportFileAccess(
        fileOperationContext,
        FileAccessStatus_CannotDeterminePolicy,
        *this,
        fakeAccessCheck,
        ERROR_SUCCESS,
        -1);
}

#if !(MAC_OS_SANDBOX) && !(MAC_OS_LIBRARY)
bool PolicyResult::AllowWrite() const {

    bool isWriteAllowedByPolicy = (m_policy & FileAccessPolicy_AllowWrite) != 0;

    // Send a special message to managed code if the policy to override allowed writes based on file existence is set
    // and the write is allowed by policy (for the latter, if the write is denied, there is nothing to override)
    if (isWriteAllowedByPolicy && OverrideAllowWriteForExistingFiles()) {
        
        // Let's check if this path was already checked for allow writes in this process. Observe this structure lifespan is the same 
        // as the current process so other child processes won't share it. 
        // But for the current process it will avoid probing the file system over and over for the same path.
        FilesCheckedForAccess* filesCheckedForWriteAccess = GetGlobalFilesCheckedForAccesses();
        
        if (filesCheckedForWriteAccess->TryRegisterPath(m_canonicalizedPath)) {
            DWORD error = GetLastError();

            // Our ultimate goal is to understand if the path represents a file that was there before the pip started (and therefore blocked for writes).
            // The existence of the file on disk before the first time the file is written will tell us that. But the problem is that knowing when is the first
            // time is not trivial: it involves sharing information across child processes.
            // So what we do is just to emit a special report line with the information of whether the access should be allowed or not, based on existence, from
            // the perspective of the running process. These special report lines are then processed outside of detours to determine the real first write attempt
            // Observe this implies that in this case we never block accesses on detours based on file existence, but generate a DFA on managed code
            bool fileExists = ExistsAsFile(m_canonicalizedPath.GetPathString());

            AccessCheckResult accessCheck = AccessCheckResult(RequestedAccess::Read, ResultAction::Allow, ReportLevel::Ignore);
            FileOperationContext operationContext = 
                FileOperationContext::CreateForRead(L"FirstAllowWriteCheckInProcess", this->GetCanonicalizedPath().GetPathString());

            ReportFileAccess(
                operationContext,
                fileExists? 
                    FileAccessStatus_Denied : 
                    FileAccessStatus_Allowed,
                *this,
                AccessCheckResult(RequestedAccess::None, ResultAction::Deny, ReportLevel::Report),
                0,
                -1);

            SetLastError(error);
        }
    }

    return isWriteAllowedByPolicy;
}
#endif