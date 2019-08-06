// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifdef ES_SANDBOX

#include "AccessHandler.hpp"
#include "OpNames.hpp"

bool AccessHandler::TryInitializeWithTrackedProcess(pid_t pid)
{
    SandboxedProcess *process = sandbox_->FindTrackedProcess(pid);
    if (process == nullptr || CheckDisableDetours(process->getPip()->getFamFlags()))
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
                                               pid_t processID)
{
    AccessReport report =
    {
        .operation          = operation,
        .pid                = processID,
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
    sandbox_->SendAccessReport(report, GetPip());

    return kReported;
}

bool AccessHandler::ReportProcessTreeCompleted(pid_t processId)
{
    AccessReport report =
    {
        .operation = kOpProcessTreeCompleted,
        .pid       = processId,
        .rootPid   = GetProcessId(),
        .pipId     = GetPipId(),
        .stats     = { .creationTime = creationTimestamp_ }
    };

    sandbox_->SendAccessReport(report, GetPip());
    return true;
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
    sandbox_->SendAccessReport(report, GetPip());
    return true;
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
    sandbox_->SendAccessReport(report, GetPip());
    
    return true;
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

AccessCheckResult AccessHandler::CheckAndReportInternal(FileOperation operation,
                                                        const char *path,
                                                        CheckFunc checker,
                                                        const es_message_t *msg,
                                                        bool isDir)
{    
    // 1: check operation against given policy
    PolicyResult policy = PolicyForPath(path);
    AccessCheckResult result = AccessCheckResult::Invalid();
    checker(policy, isDir, &result);
        
    // 2: skip if this access should not be reported
    if (!result.ShouldReport())
    {
        return result;
    }

    ReportFileOpAccess(operation, policy, result, audit_token_to_pid(msg->process->audit_token));
    return result;
}

#endif
