// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "AccessHandler.hpp"

bool AccessHandler::TryInitializeWithTrackedProcess(pid_t pid)
{
    std::shared_ptr<SandboxedProcess> process = sandbox_->FindTrackedProcess(pid);
    if (process == nullptr || CheckDisableDetours(process->GetPip()->GetFamFlags()))
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
    return FindFileAccessPolicyInTreeEx(GetPip()->GetManifestRecord(), pathWithoutRootSentinel, len);
}

void AccessHandler::SetProcessPath(AccessReport *report)
{
    strlcpy(report->path, process_->GetPath(), sizeof(report->path));
}

ReportResult AccessHandler::ReportFileOpAccess(FileOperation operation,
                                               PolicyResult policyResult,
                                               AccessCheckResult checkResult,
                                               pid_t processID)
{
    AccessReport report =
    {
        .operation          = operation,
        .pid                = processID,
        .rootPid            = GetProcessId(),
        .requestedAccess    = (DWORD)checkResult.Access,
        .status             = checkResult.GetFileAccessStatus(),
        .reportExplicitly   = checkResult.Level == ReportLevel::ReportExplicit,
        .error              = 0,
        .pipId              = GetPipId(),
        .path               = {0},
        .stats              = {0}
    };

    assert(strlen(policyResult.Path()) > 0);
    strlcpy(report.path, policyResult.Path(), sizeof(report.path));
    sandbox_->SendAccessReport(report, GetPip());

    return kReported;
}

bool AccessHandler::ReportProcessTreeCompleted(pid_t processId)
{
    AccessReport report =
    {
        .operation        = kOpProcessTreeCompleted,
        .pid              = processId,
        .rootPid          = GetProcessId(),
        .requestedAccess  = 0,
        .status           = FileAccessStatus::FileAccessStatus_Allowed,
        .reportExplicitly = 0,
        .error            = 0,
        .pipId            = GetPipId(),
        .path             = {0},
        .stats            = {0}
    };

    SetProcessPath(&report);
    sandbox_->SendAccessReport(report, GetPip());

    return kReported;
}

bool AccessHandler::ReportProcessExited(pid_t childPid)
{
    AccessReport report =
    {
        .operation        = kOpProcessExit,
        .pid              = childPid,
        .rootPid          = GetProcessId(),
        .requestedAccess  = 0,
        .status           = FileAccessStatus::FileAccessStatus_Allowed,
        .reportExplicitly = 0,
        .error            = 0,
        .pipId            = GetPipId(),
        .path             = {0},
        .stats            = {0}
    };

    SetProcessPath(&report);
    sandbox_->SendAccessReport(report, GetPip());

    return kReported;
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
        .stats              = {0}
    };

    SetProcessPath(&report);
    assert(strlen(report.path) > 0);
    sandbox_->SendAccessReport(report, GetPip());

    return kReported;
}

PolicyResult AccessHandler::PolicyForPath(const char *absolutePath)
{
    PolicySearchCursor cursor = FindManifestRecord(absolutePath);
    if (!cursor.IsValid())
    {
        log_error("Invalid policy cursor for path '%s'", absolutePath);
    }

    return PolicyResult(GetPip()->GetFamFlags(), GetPip()->GetFamExtraFlags(), absolutePath, cursor);
}

static bool is_prefix(const char *s1, const char *s2)
{
    int c;
    while ((c = *s2++) != '\0')
    {
        if (c != *s1++)
        {
            return false;
        }
    }

    return true;
}

const char* AccessHandler::IgnoreDataPartitionPrefix(const char* path)
{
    const char *marker = path;
    if (is_prefix(marker, kDataPartitionPrefix))
    {
        marker += kAdjustedPrefixLength;
    }

    return marker;
}

AccessCheckResult AccessHandler::CheckAndReportInternal(FileOperation operation,
                                                        const char *path,
                                                        CheckFunc checker,
                                                        const pid_t pid,
                                                        bool isDir)
{
    PolicyResult policy = PolicyForPath(IgnoreDataPartitionPrefix(path));
    AccessCheckResult result = AccessCheckResult::Invalid();
    checker(policy, isDir, &result);

    if (!result.ShouldReport())
    {
        return result;
    }

    ReportFileOpAccess(operation, policy, result, pid);

    return result;
}
