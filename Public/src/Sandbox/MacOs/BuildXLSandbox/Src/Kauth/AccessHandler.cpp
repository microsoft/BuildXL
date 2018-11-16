//
//  AccessHandler.cpp
//  AccessHandler
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include "AccessHandler.hpp"
#include "OpNames.hpp"

PolicySearchCursor AccessHandler::FindManifestRecord(const char *absolutePath,
                                                     size_t pathLength)
{
    assert(absolutePath[0] == '/');
    const char *pathWithoutRootSentinel = absolutePath + 1;

    size_t len = pathLength == -1 ? strlen(pathWithoutRootSentinel) : pathLength;

    return FindFileAccessPolicyInTreeEx(process_->getFAM().GetUnixRootNode(),
                                        pathWithoutRootSentinel,
                                        len);
}

ReportResult AccessHandler::DoReport(FileOperationContext fileOperationCtx,
                                     PolicyResult policyResult,
                                     AccessCheckResult checkResult,
                                     DWORD error,
                                     const OSSymbol *cacheKey)
{
    ReportResult status;
    if (process_->isAlreadyReported(cacheKey))
    {
        status = kSkipped;
    }
    else
    {
        AccessReport report =
        {
            .type               = ReportType_FileAccess,
            .operation          = {0},
            .pid                = proc_selfpid(),
            .rootPid            = GetProcessId(),
            .requestedAccess    = (DWORD)checkResult.RequestedAccess,
            .status             = checkResult.GetFileAccessStatus(),
            .reportExplicitly   = checkResult.ReportLevel == ReportLevel::ReportExplicit,
            .error              = error,
            .pipId              = GetPipId(),
            .desiredAccess      = fileOperationCtx.DesiredAccess,
            .shareMode          = fileOperationCtx.ShareMode,
            .disposition        = fileOperationCtx.CreationDisposition,
            .flagsAndAttributes = fileOperationCtx.FlagsAndAttributes,
            .pathId             = 0,
            .path               = {0}
        };

        strlcpy(report.operation, fileOperationCtx.Operation, sizeof(report.operation));
        strlcpy(report.path, policyResult.Path(), sizeof(report.path));
        
        bool sendSucceeded = sandbox_->SendAccessReport(GetClientPid(), report);
        status = sendSucceeded ? kReported : kFailed;
        if (sendSucceeded)
        {
            process_->addToReportCache(cacheKey);
        }
    }

    if (status == kFailed)
    {
        log_error("Failed to send report :: '%s' | PID = %d | PipId = %#llx | requested access: %d | status: %d | '%s'",
                  fileOperationCtx.Operation, GetProcessId(), GetPipId(), checkResult.RequestedAccess,
                  checkResult.GetFileAccessStatus(), policyResult.Path());
    }
    
    return status;
}

ReportResult AccessHandler::Report(FileOperationContext fileOperationCtx,
                                   PolicyResult policyResult,
                                   AccessCheckResult checkResult,
                                   DWORD error,
                                   const OSSymbol *cacheKey)
{
    if (checkResult.ShouldReport())
    {
        return DoReport(fileOperationCtx, policyResult, checkResult, error, cacheKey);
    }
    else
    {
        return kSkipped;
    }
}

bool AccessHandler::ReportProcessTreeCompleted()
{
    AccessReport report =
    {
        .type      = ReportType_ProcessData,
        .operation = OpProcessTreeCompleted,
        .pid       = proc_selfpid(),
        .rootPid   = GetProcessId(),
        .pipId     = GetPipId(),
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
        .type             = ReportType_FileAccess,
        .operation        = "ProcessExit",
        .pid              = childPid,
        .rootPid          = GetProcessId(),
        .pipId            = GetPipId(),
        .path             = "/dummy/path",
        .status           = FileAccessStatus::FileAccessStatus_Allowed,
        .reportExplicitly = 0,
        .error            = 0,
    };

    return sandbox_->SendAccessReport(GetClientPid(), report);
}

bool AccessHandler::ReportChildProcessSpwaned(pid_t childPid, const char *childProcessPath)
{
    AccessReport report =
    {
        .type               = ReportType_FileAccess,
        .operation          = "Process",
        .pid                = childPid,
        .rootPid            = GetProcessId(),
        .requestedAccess    = (int)RequestedAccess::Read,
        .status             = FileAccessStatus::FileAccessStatus_Allowed,
        .reportExplicitly   = 0,
        .error              = 0,
        .pipId              = GetPipId(),
        .desiredAccess      = GENERIC_READ,
        .shareMode          = FILE_SHARE_READ,
        .disposition        = CreationDisposition::OpenExisting,
        .flagsAndAttributes = 0,
        .pathId             = 0,
        .path               = {0}
    };

    if (childProcessPath)
    {
        strlcpy(report.path, childProcessPath, sizeof(report.path));
    }

    return sandbox_->SendAccessReport(GetClientPid(), report);
}

static DWORD InferShareMode(DWORD requestedAccess)
{
    return requestedAccess & (FILE_SHARE_WRITE | FILE_SHARE_WRITE);
}

FileOperationContext AccessHandler::ToFileContext(const char* action,
                                                  DWORD requestedAccess,
                                                  CreationDisposition disposition,
                                                  const char* path)
{
    return FileOperationContext(action, requestedAccess,
                                InferShareMode(requestedAccess),
                                disposition, 0, path);
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
