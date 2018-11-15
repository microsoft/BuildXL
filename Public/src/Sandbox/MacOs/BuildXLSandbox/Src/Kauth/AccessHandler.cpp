//
//  AccessHandler.cpp
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "AccessHandler.hpp"
#include "OpNames.hpp"

bool AccessHandler::TryInitializeWithTrackedProcess(pid_t pid)
{
    ProcessObject *process = sandbox_->FindTrackedProcess(pid);
    if (process == nullptr || CheckDisableDetours(process->getFamFlags()))
    {
        return false;
    }

    SetProcess(process);
    return true;
}

PolicySearchCursor AccessHandler::FindManifestRecord(const char *absolutePath, size_t pathLength)
{
    assert(absolutePath[0] == '/');
    const char *pathWithoutRootSentinel = absolutePath + 1;

    size_t len = pathLength == -1 ? strlen(pathWithoutRootSentinel) : pathLength;

    return FindFileAccessPolicyInTreeEx(process_->getFAM().GetUnixRootNode(),
                                        pathWithoutRootSentinel,
                                        len);
}

ReportResult AccessHandler::ReportFileOpAccess(FileOperation operation,
                                               PolicyResult policyResult,
                                               AccessCheckResult checkResult)
{
    if (!checkResult.ShouldReport())
    {
        return kSkipped;
    }

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

    bool sendSucceeded = sandbox_->SendAccessReport(GetClientPid(), report);
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
        .stats     = { .creationTime = creationTimestamp_ }
    };

    // Dispatch ProcessTreeCompletedAckOperation to all queues and synchronize process lifetime completion
    // inside the client code through asserting all queues have reported the event successfully. This ensures
    // we have no more events left in the queue for the process in question!
    return sandbox_->BroadcastAccessReport(GetClientPid(), report);
}

bool AccessHandler::ReportProcessExited(pid_t childPid)
{
    AccessReport report =
    {
        .operation        = kOpProcessExit,
        .pid              = childPid,
        .rootPid          = GetProcessId(),
        .pipId            = GetPipId(),
        .path             = "/dummy/path",
        .status           = FileAccessStatus::FileAccessStatus_Allowed,
        .reportExplicitly = 0,
        .error            = 0,
        .stats            = { .creationTime = creationTimestamp_ }
    };

    return sandbox_->SendAccessReport(GetClientPid(), report);
}

bool AccessHandler::ReportChildProcessSpawned(pid_t childPid, const char *childProcessPath)
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

    if (childProcessPath)
    {
        strlcpy(report.path, childProcessPath, sizeof(report.path));
    }

    return sandbox_->SendAccessReport(GetClientPid(), report);
}

void AccessHandler::LogAccessDenied(const char *path,
                                    kauth_action_t action,
                                    const char *errorMessage)
{
    log_debug("[ACCESS DENIED] PID: %d, PipId: %#llx, Path: '%s', Action: '%d', Description '%s'",
              proc_selfpid(), GetPipId(), path, action, errorMessage);
}

PolicyResult AccessHandler::PolicyForPath(const char *absolutePath)
{
    PolicySearchCursor cursor = FindManifestRecord(absolutePath);
    if (!cursor.IsValid())
    {
        log_error("Invalid policy cursor for path '%s'", absolutePath);
    }

    return PolicyResult(process_->getFamFlags(), absolutePath, cursor);
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

static uint64_t GetHardLinkCount(const vnode_t vp, const vfs_context_t ctx)
{
    struct vnode_attr vap;
    int errno;
    uint64_t result = 0;

    VATTR_GET(vp, ctx, &vap, va_nlink, &errno, &result);
    return errno == 0 ? result : 0;
}

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

bool AccessHandler::CheckAccess(vnode_t vp,
                                vfs_context_t ctx,
                                CheckFunc checker,
                                PolicyResult *policy,
                                AccessCheckResult *result)
{
    bool isDir = vnode_isdir(vp);
    checker(*policy, isDir, result);

    const char *lastLookupPath;
    // special handling for denied accesses to files with multiple hard links
    if (
        result->ShouldDenyAccess() &&                               // access is denied for current policy
        GetHardLinkCount(vp, ctx) > 1 &&                            // vnode has multiple hard links to it
        (lastLookupPath = process_->getLastLookedUpPath()) &&       // we remembered a path that was last looked up
        strncmp(lastLookupPath, policy->Path(), MAXPATHLEN) != 0 && // that path is different from the policy path
        VNodeMatchesPath(vp, ctx, lastLookupPath))                  // both paths point to the same vnode
    {
        // update policy and check again
        *policy = PolicyForPath(lastLookupPath);
        checker(*policy, isDir, result);
        return true;
    }
    else
    {
        return false;
    }
}

AccessCheckResult AccessHandler::DoCheckAndReport(FileOperation operation,
                                                  const char *path,
                                                  CheckFunc checker,
                                                  vfs_context_t ctx,
                                                  vnode_t vp)
{
    PolicyResult policy = PolicyForPath(path);
    AccessCheckResult result = AccessCheckResult::Invalid();
    if (vp != nullptr && ctx != nullptr)
    {
        CheckAccess(vp, ctx, checker, &policy, &result);
    }
    else
    {
        checker(policy, /* isDir */ false, &result);
    }

    ReportFileOpAccess(operation, policy, result);

    return result;
}

AccessCheckResult AccessHandler::CheckAndReport(FileOperation operation,
                                                const char *path,
                                                CheckFunc checker,
                                                vfs_context_t ctx,
                                                vnode_t vp)
{
    // construct cache key
    char key[MAXPATHLEN + 3];
    snprintf(key, sizeof(key), "%02d,%s", (uint)operation, path);
    const OSSymbol *keySym = OSSymbol::withCString(key);

    // default result in case of cache hit
    AccessCheckResult result = AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Ignore);

    // check if alredy reported; if not, execute the check and add to cache if allowed
    if (!process_->isAlreadyReported(keySym))
    {
        result = DoCheckAndReport(operation, path, checker, ctx, vp);
        if (!result.ShouldDenyAccess())
        {
            process_->addToReportCache(keySym);
        }
    }

    // clean up and return
    OSSafeReleaseNULL(keySym);
    return result;
}
