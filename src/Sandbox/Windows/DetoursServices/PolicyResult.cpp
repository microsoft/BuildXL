// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// --------------------------------------------------------------------

#include "PolicyResult.h"
#include "DetoursHelpers.h"
#include "SendReport.h"

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
        Dbg(L"match (special case rules.1): %s - policy: %x, pathId: %x", GetCanonicalizedPath(), Policy, PathId);
#endif // SUPER_VERBOSE
    }
    else
    {
        if (GetSpecialCaseRulesForSpecialTools(translatedSearchSuffix, searchSuffixLength, /*out*/ m_policy))
        {
#if SUPER_VERBOSE
            Dbg(L"match (special case rules.2): %s - policy: %x, pathId: %x", GetCanonicalizedPath(), Policy, PathId);
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
