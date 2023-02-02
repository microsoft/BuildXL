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

ReportResult AccessHandler::CreateReportFileOpAccess(FileOperation operation,
                                               PolicyResult policyResult,
                                               AccessCheckResult checkResult,
                                               pid_t processID,
                                               uint isDirectory,
                                               uint error,
                                               AccessReport &accessReport)
{
    accessReport.operation          = operation;
    accessReport.pid                = processID;
    accessReport.rootPid            = GetProcessId();
    accessReport.requestedAccess    = (DWORD)checkResult.Access;
    accessReport.status             = checkResult.GetFileAccessStatus();
    accessReport.reportExplicitly   = checkResult.Level == ReportLevel::ReportExplicit;
    accessReport.error              = error;
    accessReport.pipId              = GetPipId();
    accessReport.stats              = {0};
    accessReport.isDirectory        = isDirectory;
    accessReport.shouldReport       = checkResult.ShouldReport();
    std::fill_n(accessReport.path, MAXPATHLEN, 0);

    assert(strlen(policyResult.Path()) > 0);
    strlcpy(accessReport.path, policyResult.Path(), sizeof(accessReport.path));

    return kReported;
}

ReportResult AccessHandler::SendReport(AccessReport& report)
{
    sandbox_->SendAccessReport(report, GetPip());

    return kReported;
}

bool AccessHandler::CreateReportProcessTreeCompleted(pid_t processId, AccessReport &accessReport)
{
    accessReport.operation        = kOpProcessTreeCompleted;
    accessReport.pid              = processId;
    accessReport.rootPid          = GetProcessId();
    accessReport.requestedAccess  = 0;
    accessReport.status           = FileAccessStatus::FileAccessStatus_Allowed;
    accessReport.reportExplicitly = 0;
    accessReport.error            = 0;
    accessReport.pipId            = GetPipId();
    accessReport.stats            = {0};
    accessReport.isDirectory      = 0;
    accessReport.shouldReport     = true;
    std::fill_n(accessReport.path, MAXPATHLEN, 0);

    SetProcessPath(&accessReport);

    return kReported;
}

bool AccessHandler::CreateReportProcessExited(pid_t childPid, AccessReport &accessReport)
{
    accessReport.operation        = kOpProcessExit;
    accessReport.pid              = childPid;
    accessReport.rootPid          = GetProcessId();
    accessReport.requestedAccess  = 0;
    accessReport.status           = FileAccessStatus::FileAccessStatus_Allowed;
    accessReport.reportExplicitly = 0;
    accessReport.error            = 0;
    accessReport.pipId            = GetPipId();
    accessReport.stats            = {0};
    accessReport.isDirectory      = 0;
    accessReport.shouldReport     = true;
    std::fill_n(accessReport.path, MAXPATHLEN, 0);

    SetProcessPath(&accessReport);

    return kReported;
}

bool AccessHandler::CreateReportChildProcessSpawned(pid_t childPid, AccessReport &accessReport)
{
    accessReport.operation          = kOpProcessStart;
    accessReport.pid                = childPid;
    accessReport.rootPid            = GetProcessId();
    accessReport.requestedAccess    = (int)RequestedAccess::Read;
    accessReport.status             = FileAccessStatus::FileAccessStatus_Allowed;
    accessReport.reportExplicitly   = 0;
    accessReport.error              = 0;
    accessReport.pipId              = GetPipId();
    accessReport.stats              = {0};
    accessReport.isDirectory        = 0;
    accessReport.shouldReport       = true;
    std::fill_n(accessReport.path, MAXPATHLEN, 0);

    SetProcessPath(&accessReport);
    assert(strlen(accessReport.path) > 0);


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

AccessCheckResult AccessHandler::CheckAndCreateReportInternal(FileOperation operation,
                                                        const char *path,
                                                        CheckFunc checker,
                                                        const pid_t pid,
                                                        bool isDir,
                                                        uint error,
                                                        AccessReport &accessToReport)
{
    PolicyResult policy = PolicyForPath(IgnoreDataPartitionPrefix(path));
    AccessCheckResult result = AccessCheckResult::Invalid();
    checker(policy, isDir, &result);

    CreateReportFileOpAccess(operation, policy, result, pid, (uint)isDir, error, accessToReport);

    return result;
}
