// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "IOHandler.hpp"

#pragma mark Process life cycle

static AccessCheckResult s_allowedCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Report);

AccessCheckResult IOHandler::HandleProcessFork(const IOEvent &event, AccessReport &accessToReport)
{
    if (GetPip()->AllowChildProcessesToBreakAway())
    {
        return s_allowedCheckResult;
    }

    pid_t childProcessPid = event.GetChildPid();
    if (GetSandbox()->TrackChildProcess(childProcessPid, event.GetExecutablePath(), GetProcess()))
    {
        CreateReportChildProcessSpawned(childProcessPid, accessToReport);
    }

    return s_allowedCheckResult;
}

AccessCheckResult IOHandler::HandleProcessExec(const IOEvent &event, AccessReport &accessToReport)
{
    GetProcess()->SetPath(event.GetExecutablePath());
    CreateReportChildProcessSpawned(GetProcess()->GetPid(), accessToReport);
    return s_allowedCheckResult;
}

AccessCheckResult IOHandler::HandleProcessExit(const IOEvent &event, AccessReport &processExitReport, AccessReport &processTreeCompletedReport)
{
    pid_t pid = event.GetPid();

    CreateReportProcessExited(pid, processExitReport);
    HandleProcessUntracked(pid, processTreeCompletedReport);

    return s_allowedCheckResult;
}

AccessCheckResult IOHandler::HandleProcessUntracked(const pid_t pid, AccessReport &accessToReport)
{
    GetSandbox()->UntrackProcess(pid, GetProcess());
    if (GetPip()->GetTreeSize() == 0)
    {
        CreateReportProcessTreeCompleted(GetPip()->GetProcessId(), accessToReport);
    }
    return s_allowedCheckResult;
}

AccessCheckResult IOHandler::HandleProcessUntracked(const pid_t pid)
{
    AccessReport accessReport;

    AccessCheckResult result = HandleProcessUntracked(pid, accessReport);

    GetSandbox()->SendAccessReport(accessReport, GetPip());

    return result;
}

#pragma mark Process I/O observation

AccessCheckResult IOHandler::HandleLookup(const IOEvent &event, AccessReport &accessToReport)
{
    return CheckAndCreateReport(kOpMacLookup, event.GetEventPath(SRC_PATH), Checkers::CheckLookup, event.GetPid(), /*isDir*/ false, event.GetError(), accessToReport);
}

AccessCheckResult IOHandler::HandleOpen(const IOEvent &event, AccessReport &accessToReport)
{
    if (!event.EventPathExists())
    {
        // Some tools use open() on directories to get a file handle for other calls e.g. fchdir(), in those cases
        // the mode is reported as 0 and the path would be treated as non-existent. We try to stat the path to get a
        // correct mode otherwise fall back to reporting the file access as a lookup.
        struct stat sb;
        if (lstat(event.GetEventPath(SRC_PATH), &sb) == 0)
        {
            bool isDir = S_ISDIR(sb.st_mode);

            CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
            FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;

            return CheckAndCreateReport(op, event.GetEventPath(SRC_PATH), checker, event.GetPid(), isDir, event.GetError(), accessToReport);
        }

        return CheckAndCreateReport(kOpMacLookup, event.GetEventPath(SRC_PATH), Checkers::CheckLookup, event.GetPid(), false, event.GetError(), accessToReport);
    }

    bool isDir = S_ISDIR(event.GetMode());

    CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
    FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;

    return CheckAndCreateReport(op, event.GetEventPath(SRC_PATH), checker, event.GetPid(), isDir, event.GetError(), accessToReport);
}

AccessCheckResult IOHandler::HandleClose(const IOEvent &event, AccessReport &accessToReport)
{
    if (event.FSEntryModified())
    {
        return CheckAndCreateReport(kOpKAuthCloseModified, event.GetEventPath(SRC_PATH), Checkers::CheckWrite, event.GetPid(), /*isDir*/ false, event.GetError(), accessToReport);
    }

    bool isDir = S_ISDIR(event.GetMode());
    return CheckAndCreateReport(kOpKAuthClose, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid(), isDir, event.GetError(), accessToReport);
}

AccessCheckResult IOHandler::HandleLink(const IOEvent &event, AccessReport &sourceAccessToReport, AccessReport &destinationAccessToReport)
{
    bool isDir = S_ISDIR(event.GetMode());
    
    AccessCheckResult sourceResult = CheckAndCreateReport(kOpKAuthCreateHardlinkSource, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid(), isDir, event.GetError(), sourceAccessToReport);
    AccessCheckResult destResult = CheckAndCreateReport(kOpKAuthCreateHardlinkDest, event.GetEventPath(DST_PATH), Checkers::CheckWrite, event.GetPid(), isDir, event.GetError(), destinationAccessToReport);
    
    return AccessCheckResult::Combine(sourceResult, destResult);
}

AccessCheckResult IOHandler::HandleUnlink(const IOEvent &event, AccessReport &accessToReport)
{
    bool isDir = S_ISDIR(event.GetMode());
    FileOperation operation = isDir ? kOpKAuthDeleteDir : kOpKAuthDeleteFile;
    return CheckAndCreateReport(operation, event.GetEventPath(SRC_PATH), Checkers::CheckWrite, event.GetPid(), isDir, event.GetError(), accessToReport);
}

AccessCheckResult IOHandler::HandleReadlink(const IOEvent &event, AccessReport &accessToReport)
{
    return CheckAndCreateReport(kOpMacReadlink, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid(), false, event.GetError(), accessToReport);
}

AccessCheckResult IOHandler::HandleRename(const IOEvent &event, AccessReport &sourceAccessToReport, AccessReport &destinationAccessToReport)
{
    bool isDir = S_ISDIR(event.GetMode());
    
    AccessCheckResult sourceResult = CheckAndCreateReport(kOpKAuthMoveSource, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid(), isDir, event.GetError(), sourceAccessToReport);
    AccessCheckResult destResult = CheckAndCreateReport(kOpKAuthMoveDest, event.GetEventPath(DST_PATH), Checkers::CheckWrite, event.GetPid(), isDir, event.GetError(), destinationAccessToReport);

    return AccessCheckResult::Combine(sourceResult, destResult);
}

AccessCheckResult IOHandler::HandleClone(const IOEvent &event, AccessReport &sourceAccessToReport, AccessReport &destinationAccessToReport)
{
    AccessCheckResult sourceResult = CheckAndCreateReport(kOpMacVNodeCloneSource, event.GetEventPath(SRC_PATH), Checkers::CheckReadWrite, event.GetPid(), false, event.GetError(), sourceAccessToReport);
    AccessCheckResult destResult = CheckAndCreateReport(kOpMacVNodeCloneDest, event.GetEventPath(DST_PATH), Checkers::CheckReadWrite, event.GetPid(), false, event.GetError(), destinationAccessToReport);

    return AccessCheckResult::Combine(sourceResult, destResult);
}

AccessCheckResult IOHandler::HandleExchange(const IOEvent &event, AccessReport &sourceAccessToReport, AccessReport &destinationAccessToReport)
{
    AccessCheckResult sourceResult = CheckAndCreateReport(kOpKAuthCopySource, event.GetEventPath(SRC_PATH), Checkers::CheckReadWrite, event.GetPid(), /*isDir*/false, event.GetError(), sourceAccessToReport);
    AccessCheckResult destResult = CheckAndCreateReport(kOpKAuthCopyDest, event.GetEventPath(DST_PATH), Checkers::CheckReadWrite, event.GetPid(), /*isDir*/false, event.GetError(), destinationAccessToReport);

    return AccessCheckResult::Combine(sourceResult, destResult);
}

AccessCheckResult IOHandler::HandleCreate(const IOEvent &event, AccessReport &accessToReport)
{
    CheckFunc checker = Checkers::CheckWrite;
    bool isDir = false;

    if (event.EventPathExists())
    {
        mode_t mode = event.GetMode();
        bool enforceDirectoryCreation = CheckDirectoryCreationAccessEnforcement(GetFamFlags());
        isDir = S_ISDIR(mode);

        checker =
            S_ISLNK(mode) ? Checkers::CheckCreateSymlink
                          : S_ISREG(mode)
                            ? Checkers::CheckWrite
                            : enforceDirectoryCreation
                                ? Checkers::CheckCreateDirectory
                                : Checkers::CheckCreateDirectoryNoEnforcement;
    }

    return CheckAndCreateReport(isDir ? kOpKAuthCreateDir : kOpMacVNodeCreate, event.GetEventPath(SRC_PATH), checker, event.GetPid(), isDir, event.GetError(), accessToReport);
}

AccessCheckResult IOHandler::HandleGenericWrite(const IOEvent &event, AccessReport &accessToReport)
{
    const char *path = event.GetEventPath(SRC_PATH);
    mode_t mode = event.GetMode();
    bool isDir = S_ISDIR(mode);

    return CheckAndCreateReport(kOpKAuthVNodeWrite, path, Checkers::CheckWrite, event.GetPid(), isDir, event.GetError(), accessToReport);
}

AccessCheckResult IOHandler::HandleGenericRead(const IOEvent &event, AccessReport &accessToReport)
{
    const char *path = event.GetEventPath(SRC_PATH);
    mode_t mode = event.GetMode();
    bool isDir = S_ISDIR(mode);

    if (!event.EventPathExists())
    {
        return CheckAndCreateReport(kOpMacLookup, path, Checkers::CheckLookup, event.GetPid(), false, event.GetError(), accessToReport);
    }
    else
    {
        return CheckAndCreateReport(kOpKAuthVNodeRead, path, Checkers::CheckRead, event.GetPid(), isDir, event.GetError(), accessToReport);
    }
}

AccessCheckResult IOHandler::HandleGenericProbe(const IOEvent &event, AccessReport &accessToReport)
{
    const char *path = event.GetEventPath(SRC_PATH);
    bool isDir = S_ISDIR(event.GetMode());

    if (!event.EventPathExists())
    {
        return CheckAndCreateReport(kOpMacLookup, path, Checkers::CheckLookup, event.GetPid(), false, event.GetError(), accessToReport);
    }
    else
    {
        return CheckAndCreateReport(kOpKAuthVNodeProbe, path, Checkers::CheckProbe, event.GetPid(), isDir, event.GetError(), accessToReport);
    }
}

AccessCheckResult IOHandler::HandleEvent(const IOEvent &event)
{
    AccessReportGroup report;
    AccessCheckResult result = CheckAccessAndBuildReport(event, report);

    if (report.firstReport.shouldReport)
    {
        SendReport(report.firstReport);
    }

    if (report.secondReport.shouldReport)
    {
        SendReport(report.secondReport);
    }

    return result;
}

AccessCheckResult IOHandler::CheckAccessAndBuildReport(const IOEvent &event, AccessReportGroup &accessToReportGroup)
{
    // The second report may not be set below, so prevently flag it as a no report one.
    accessToReportGroup.secondReport.shouldReport = false;

    switch (event.GetEventType())
    {
        case ES_EVENT_TYPE_AUTH_EXEC:
        case ES_EVENT_TYPE_NOTIFY_EXEC:
        {
            return HandleProcessExec(event, accessToReportGroup.firstReport);
        }
        case ES_EVENT_TYPE_NOTIFY_FORK:
            return HandleProcessFork(event, accessToReportGroup.firstReport);

        case ES_EVENT_TYPE_NOTIFY_EXIT:
            return HandleProcessExit(event, accessToReportGroup.firstReport, accessToReportGroup.secondReport);

        case ES_EVENT_TYPE_NOTIFY_LOOKUP:
            return HandleLookup(event, accessToReportGroup.firstReport);

        case ES_EVENT_TYPE_AUTH_OPEN:
        case ES_EVENT_TYPE_NOTIFY_OPEN:
        {
            return HandleOpen(event, accessToReportGroup.firstReport);
        }
        case ES_EVENT_TYPE_NOTIFY_CLOSE:
            return HandleClose(event, accessToReportGroup.firstReport);

        case ES_EVENT_TYPE_AUTH_CREATE:
        case ES_EVENT_TYPE_NOTIFY_CREATE:
        {
            return HandleCreate(event, accessToReportGroup.firstReport);
        }
        case ES_EVENT_TYPE_AUTH_TRUNCATE:
        case ES_EVENT_TYPE_NOTIFY_TRUNCATE:
        case ES_EVENT_TYPE_AUTH_SETATTRLIST:
        case ES_EVENT_TYPE_NOTIFY_SETATTRLIST:
        case ES_EVENT_TYPE_AUTH_SETEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_SETEXTATTR:
        case ES_EVENT_TYPE_AUTH_DELETEEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR:
        case ES_EVENT_TYPE_AUTH_SETFLAGS:
        case ES_EVENT_TYPE_NOTIFY_SETFLAGS:
        case ES_EVENT_TYPE_AUTH_SETOWNER:
        case ES_EVENT_TYPE_NOTIFY_SETOWNER:
        case ES_EVENT_TYPE_AUTH_SETMODE:
        case ES_EVENT_TYPE_NOTIFY_SETMODE:
        case ES_EVENT_TYPE_NOTIFY_WRITE:
        case ES_EVENT_TYPE_NOTIFY_UTIMES:
        case ES_EVENT_TYPE_NOTIFY_SETTIME:
        case ES_EVENT_TYPE_AUTH_SETACL:
        case ES_EVENT_TYPE_NOTIFY_SETACL:
            return HandleGenericWrite(event, accessToReportGroup.firstReport);

        case ES_EVENT_TYPE_NOTIFY_CHDIR:
        case ES_EVENT_TYPE_NOTIFY_READDIR:
        case ES_EVENT_TYPE_NOTIFY_FSGETPATH:
            return HandleGenericRead(event, accessToReportGroup.firstReport);

        case ES_EVENT_TYPE_AUTH_GETATTRLIST:
        case ES_EVENT_TYPE_NOTIFY_GETATTRLIST:
        case ES_EVENT_TYPE_AUTH_GETEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_GETEXTATTR:
        case ES_EVENT_TYPE_AUTH_LISTEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_LISTEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_ACCESS:
        case ES_EVENT_TYPE_NOTIFY_STAT:
            return HandleGenericProbe(event, accessToReportGroup.firstReport);

        case ES_EVENT_TYPE_AUTH_CLONE:
        case ES_EVENT_TYPE_NOTIFY_CLONE:
        {
            return HandleClone(event, accessToReportGroup.firstReport, accessToReportGroup.secondReport);
        }
        case ES_EVENT_TYPE_AUTH_EXCHANGEDATA:
        case ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA:
        {
            return HandleExchange(event, accessToReportGroup.firstReport, accessToReportGroup.secondReport);
        }
        case ES_EVENT_TYPE_AUTH_RENAME:
        case ES_EVENT_TYPE_NOTIFY_RENAME:
        {
            return HandleRename(event, accessToReportGroup.firstReport, accessToReportGroup.secondReport);
        }
        case ES_EVENT_TYPE_AUTH_READLINK:
        case ES_EVENT_TYPE_NOTIFY_READLINK:
        {
            return HandleReadlink(event, accessToReportGroup.firstReport);
        }
        case ES_EVENT_TYPE_AUTH_LINK:
        case ES_EVENT_TYPE_NOTIFY_LINK:
        {
            return HandleLink(event, accessToReportGroup.firstReport, accessToReportGroup.secondReport);
        }
        case ES_EVENT_TYPE_AUTH_UNLINK:
        case ES_EVENT_TYPE_NOTIFY_UNLINK:
        {
            return HandleUnlink(event, accessToReportGroup.firstReport);
        }
        case ES_EVENT_TYPE_LAST:
            accessToReportGroup.firstReport.shouldReport = false;
            return AccessCheckResult::Invalid();
        default:
            std::string message("Unhandled ES event: ");
            message.append(std::to_string(event.GetEventType()));
            throw BuildXLException(message);
    }
}
