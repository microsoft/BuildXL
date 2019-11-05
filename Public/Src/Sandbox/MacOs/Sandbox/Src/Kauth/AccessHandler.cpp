// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "AccessHandler.hpp"
#include "OpNames.hpp"
#include "Stopwatch.hpp"

bool AccessHandler::TryInitializeWithTrackedProcess(pid_t pid)
{
    Stopwatch stopwatch;
    SandboxedProcess *process = sandbox_->FindTrackedProcess(pid);
    Timespan duration = stopwatch.lap();

    sandbox_->Counters()->findTrackedProcess += duration;

    if (process == nullptr || CheckDisableDetours(process->getPip()->getFamFlags()))
    {
        return false;
    }

    process->getPip()->Counters()->findTrackedProcess += duration;
    SetProcess(process);
    return true;
}

PolicySearchCursor AccessHandler::FindManifestRecord(const char *absolutePath, size_t pathLength)
{
    assert(absolutePath[0] == '/');
    const char *pathWithoutRootSentinel = absolutePath + 1;

    size_t len = pathLength == -1 ? strlen(pathWithoutRootSentinel) : pathLength;
    return FindFileAccessPolicyInTreeEx(GetPip()->getManifestRecord(), pathWithoutRootSentinel, len);
}

void AccessHandler::SetProcessPath(AccessReport *report)
{
    const char *procName = process_->hasPath()
        ? process_->getPath()
        : "/unknown-process"; // should never happen
    strlcpy(report->path, procName, sizeof(report->path));
}

ReportResult AccessHandler::ReportFileOpAccess(FileOperation operation,
                                               PolicyResult policyResult,
                                               AccessCheckResult checkResult,
                                               CacheRecord *cacheRecord)
{
    AccessReport report =
    {
        .operation          = operation,
        .pid                = proc_selfpid(),
        .rootPid            = GetProcessId(),
        .requestedAccess    = (DWORD)checkResult.RequestedAccess,
        .status             = checkResult.GetFileAccessStatus(),
        .reportExplicitly   = checkResult.ReportLevel == ReportLevel::ReportExplicit,
        .error              = 0,
        .pipId              = GetPipId(),
        .path               = {0},
        .stats              = { .creationTime = creationTimestamp_ }
    };

    strlcpy(report.path, policyResult.Path(), sizeof(report.path));

    bool sendSucceeded = sandbox_->SendAccessReport(report, GetPip(), cacheRecord);
    ReportResult status = sendSucceeded ? kReported : kFailed;

    if (status == kFailed)
    {
        log_error("Failed to send report :: '%s' | PID = %d | PipId = %#llx | requested access: %d | status: %d | '%s'",
                  OpNames[operation], GetProcessId(), GetPipId(), checkResult.RequestedAccess,
                  checkResult.GetFileAccessStatus(), policyResult.Path());
    }

    return status;
}

bool AccessHandler::ReportProcessTreeCompleted()
{
    AccessReport report =
    {
        .operation = kOpProcessTreeCompleted,
        .pid       = proc_selfpid(),
        .rootPid   = GetProcessId(),
        .pipId     = GetPipId(),
        .pipStats  =
        {
            .lastPathLookupElemCount = GetPip()->getLastPathLookupElemCount(),
            .lastPathLookupNodeCount = GetPip()->getLastPathLookupNodeCount(),
            .lastPathLookupNodeSize  = GetPip()->getLastPathLookupNodeSize(),
            .numCacheHits            = GetPip()->Counters()->numCacheHits.count(),
            .numCacheMisses          = GetPip()->Counters()->numCacheMisses.count(),
            .cacheRecordCount        = GetPip()->getPathCacheElemCount(),
            .cacheRecordSize         = sizeof(CacheRecord),
            .cacheNodeCount          = GetPip()->getPathCacheNodeCount(),
            .cacheNodeSize           = GetPip()->getPathCacheNodeSize(),
            .numForks                = GetPip()->Counters()->numForks.count(),
            .numHardLinkRetries      = GetPip()->Counters()->numHardLinkRetries.count(),
        },
        .stats     = { .creationTime = creationTimestamp_ }
    };

    return sandbox_->SendAccessReport(report, GetPip(), /*cacheRecord*/ nullptr);
}

bool AccessHandler::ReportProcessExited(pid_t childPid)
{
    AccessReport report =
    {
        .operation        = kOpProcessExit,
        .pid              = childPid,
        .rootPid          = GetProcessId(),
        .pipId            = GetPipId(),
        .status           = FileAccessStatus::FileAccessStatus_Allowed,
        .reportExplicitly = 0,
        .error            = 0,
        .stats            = { .creationTime = creationTimestamp_ }
    };

    SetProcessPath(&report);

    return sandbox_->SendAccessReport(report, GetPip(), nullptr);
}

bool AccessHandler::ReportChildProcessSpawned(pid_t childPid)
{
    AccessReport report =
    {
        .operation          = kOpProcessStart,
        .pid                = childPid,
        .rootPid            = GetProcessId(),
        .requestedAccess    = (int)RequestedAccess::Read,
        .status             = FileAccessStatus::FileAccessStatus_Allowed,
        .reportExplicitly   = 0,
        .error              = 0,
        .pipId              = GetPipId(),
        .path               = {0},
        .stats              = { .creationTime = creationTimestamp_ }
    };

    SetProcessPath(&report);

    return sandbox_->SendAccessReport(report, GetPip(), nullptr);
}

void AccessHandler::LogAccessDenied(const char *path,
                                    kauth_action_t action,
                                    const char *errorMessage)
{
    log("[ACCESS DENIED] PID: %d, PipId: %#llx, Path: '%s', Action: '%d', Description '%s'",
        proc_selfpid(), GetPipId(), path, action, errorMessage);
}

PolicyResult AccessHandler::PolicyForPath(const char *absolutePath)
{
    PolicySearchCursor cursor = FindManifestRecord(absolutePath);
    if (!cursor.IsValid())
    {
        log_error("Invalid policy cursor for path '%s'", absolutePath);
    }

    return PolicyResult(GetPip()->getFamFlags(), absolutePath, cursor);
}

#define VATTR_GET(vp, ctx, vap, attr, errno, result) \
do {                                      \
  VATTR_INIT(vap);                        \
  VATTR_WANTED(vap, attr);                \
  *(errno) = vnode_getattr(vp, vap, ctx); \
  if (*(errno) == 0) {                    \
    *(result) = (vap)->attr;              \
  }                                       \
} while (0)

static int GetUniqueFileId(const vnode_t vp, const vfs_context_t ctx, uint64_t *result)
{
    struct vnode_attr vap; int errno;
    VATTR_GET(vp, ctx, &vap, va_fileid, &errno, result);
    return errno;
}

static bool VNodeMatchesPath(vnode_t vp, vfs_context_t ctx, const char *path)
{
    if (path == nullptr)
    {
        return false;
    }

    vnode_t vpp = nullptr;
    bool result = false;
    do
    {
        int errno = vnode_lookup(path, 0, &vpp, ctx); // must release calling vnode_put
        if (errno != 0)
        {
            break;
        }

        uint64_t vp_fileid, vpp_fileid;
        errno = GetUniqueFileId(vp, ctx, &vp_fileid);
        if (errno != 0)
        {
            break;
        }

        errno = GetUniqueFileId(vpp, ctx, &vpp_fileid);
        if (errno != 0)
        {
            break;
        }

        result = vp_fileid == vpp_fileid;
    } while (0);

    if (vpp != nullptr)
    {
        vnode_put(vpp);
    }

    return result;
}

const char* AccessHandler::IgnoreCatalinaDataPartitionPrefix(const char* path)
{
    if (!sandbox_->GetConfig().enableCatalinaDataPartitionFiltering)
    {
        return path;
    }

    const char *marker = path;
    if (strprefix(marker, kCatalinaDataPartitionPrefix))
    {
        marker += kAdjustedCatalinaPrefixLength;
    }

    return marker;
}

bool AccessHandler::CheckAccess(vnode_t vp,
                                vfs_context_t ctx,
                                CheckFunc checker,
                                PolicyResult *policy,
                                AccessCheckResult *result)
{
    bool isDir = vnode_isdir(vp);
    checker(*policy, isDir, result);

    bool notAllowed = result->GetFileAccessStatus() != FileAccessStatus_Allowed;
    const char *lastLookupPath;
    // special handling for denied accesses to files with multiple hard links
    if (
        notAllowed &&                                               // access is denied for current policy
        (lastLookupPath = GetPip()->getLastLookedUpPath()) &&       // we remembered a path that was last looked up
        strncmp(lastLookupPath, policy->Path(), MAXPATHLEN) != 0 && // that path is different from the policy path
        VNodeMatchesPath(vp, ctx, lastLookupPath))                  // both paths point to the same vnode
    {
        // update policy and check again
        sandbox_->Counters()->numHardLinkRetries++;

        *policy = PolicyForPath(IgnoreCatalinaDataPartitionPrefix(lastLookupPath));
        checker(*policy, isDir, result);
        return true;
    }
    else
    {
        return false;
    }
}

AccessCheckResult AccessHandler::CheckAndReportInternal(FileOperation operation,
                                                        const char *path,
                                                        CheckFunc checker,
                                                        vfs_context_t ctx,
                                                        vnode_t vp,
                                                        bool isDir)
{    
    Stopwatch stopwatch;

    // 1: check operation against given policy
    PolicyResult policy = PolicyForPath(IgnoreCatalinaDataPartitionPrefix(path));
    AccessCheckResult result = AccessCheckResult::Invalid();
    if (vp != nullptr && ctx != nullptr)
    {
        CheckAccess(vp, ctx, checker, &policy, &result);
    }
    else
    {
        checker(policy, isDir, &result);
    }

    Timespan checkPolicyDuration       = stopwatch.lap();
    GetPip()->Counters()->checkPolicy += checkPolicyDuration;
    sandbox_->Counters()->checkPolicy += checkPolicyDuration;
    
    // 2: skip if this access should not be reported
    if (!result.ShouldReport())
    {
        return result;
    }

    // 3: check cache to see if the same access has already been reported
    CacheRecord *cacheRecord = GetPip()->cacheLookup(path);
    bool cacheHit = cacheRecord != nullptr && cacheRecord->CheckAndUpdate(&result);

    Timespan cacheLookupDuration       = stopwatch.lap();
    sandbox_->Counters()->cacheLookup += cacheLookupDuration;
    GetPip()->Counters()->cacheLookup += cacheLookupDuration;

    if (!cacheHit)
    {
        GetPip()->Counters()->numCacheMisses++;
        ReportFileOpAccess(operation, policy, result, cacheRecord);
    }
    else
    {
        GetPip()->Counters()->numCacheHits++;
    }

    return result;
}
