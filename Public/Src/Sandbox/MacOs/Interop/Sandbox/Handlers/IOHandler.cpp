// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "IOHandler.hpp"

#pragma mark Process life cycle

static AccessCheckResult s_allowedCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Report);

AccessCheckResult IOHandler::HandleProcessFork(const IOEvent &event)
{
    if (GetPip()->AllowChildProcessesToBreakAway())
    {
        return s_allowedCheckResult;
    }

    pid_t childProcessPid = event.GetChildPid();
    if (GetSandbox()->TrackChildProcess(childProcessPid, event.GetExecutablePath(), GetProcess()))
    {
        ReportChildProcessSpawned(childProcessPid);
    }

    return s_allowedCheckResult;
}

AccessCheckResult IOHandler::HandleProcessExec(const IOEvent &event)
{
    GetProcess()->SetPath(event.GetExecutablePath());
    ReportChildProcessSpawned(GetProcess()->GetPid());
    return s_allowedCheckResult;
}

AccessCheckResult IOHandler::HandleProcessExit(const IOEvent &event)
{
    pid_t pid = event.GetPid();
    
    ReportProcessExited(pid);
    HandleProcessUntracked(pid);
    return s_allowedCheckResult;
}

AccessCheckResult IOHandler::HandleProcessUntracked(const pid_t pid)
{
    GetSandbox()->UntrackProcess(pid, GetProcess());
    if (GetPip()->GetTreeSize() == 0)
    {
        ReportProcessTreeCompleted(GetPip()->GetProcessId());
    }
    return s_allowedCheckResult;
}

#pragma mark Process I/O observation

AccessCheckResult IOHandler::HandleLookup(const IOEvent &event)
{
    return CheckAndReport(kOpMacLookup, event.GetEventPath(SRC_PATH), Checkers::CheckLookup, event.GetPid(), /*isDir*/ false);
}

AccessCheckResult IOHandler::HandleOpen(const IOEvent &event)
{
    bool isDir = S_ISDIR(event.GetMode());
    
    // When interposing, we get every open() call attempt regardless of success, open calls on
    // non-existent paths are currently treated as lookups.
    if (!event.EventPathExists())
    {
        return CheckAndReport(kOpMacLookup, event.GetEventPath(SRC_PATH), Checkers::CheckLookup, event.GetPid(), isDir);
    }
    
    CheckFunc checker = isDir ? Checkers::CheckEnumerateDir : Checkers::CheckRead;
    FileOperation op  = isDir ? kOpKAuthOpenDir : kOpKAuthReadFile;
        
    return CheckAndReport(op, event.GetEventPath(SRC_PATH), checker, event.GetPid(), isDir);
}

AccessCheckResult IOHandler::HandleClose(const IOEvent &event)
{
    if (event.FSEntryModified())
    {
        return CheckAndReport(kOpKAuthCloseModified, event.GetEventPath(SRC_PATH), Checkers::CheckWrite, event.GetPid());
    }
    
    bool isDir = S_ISDIR(event.GetMode());
    return CheckAndReport(kOpKAuthClose, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid(), isDir);
}

AccessCheckResult IOHandler::HandleLink(const IOEvent &event)
{
    return AccessCheckResult::Combine(
        CheckAndReport(kOpKAuthCreateHardlinkSource, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid()),
        CheckAndReport(kOpKAuthCreateHardlinkDest, event.GetEventPath(DST_PATH), Checkers::CheckWrite, event.GetPid()));
}

AccessCheckResult IOHandler::HandleUnlink(const IOEvent &event)
{
    bool isDir = S_ISDIR(event.GetMode());
    FileOperation operation = isDir ? kOpKAuthDeleteDir : kOpKAuthDeleteFile;
    return CheckAndReport(operation, event.GetEventPath(SRC_PATH), Checkers::CheckWrite, event.GetPid());
}

AccessCheckResult IOHandler::HandleReadlink(const IOEvent &event)
{
    return CheckAndReport(kOpMacReadlink, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid(), false);
}

AccessCheckResult IOHandler::HandleRename(const IOEvent &event)
{
    return AccessCheckResult::Combine(
        CheckAndReport(kOpKAuthMoveSource, event.GetEventPath(SRC_PATH), Checkers::CheckRead, event.GetPid()),
        CheckAndReport(kOpKAuthMoveDest, event.GetEventPath(DST_PATH), Checkers::CheckWrite, event.GetPid()));
}

AccessCheckResult IOHandler::HandleClone(const IOEvent &event)
{
    return AccessCheckResult::Combine(
        CheckAndReport(kOpMacVNodeCloneSource, event.GetEventPath(SRC_PATH), Checkers::CheckReadWrite, event.GetPid()),
        CheckAndReport(kOpMacVNodeCloneDest, event.GetEventPath(DST_PATH), Checkers::CheckReadWrite, event.GetPid()));
}

AccessCheckResult IOHandler::HandleExchange(const IOEvent &event)
{
    return AccessCheckResult::Combine(
        CheckAndReport(kOpKAuthCopySource, event.GetEventPath(SRC_PATH), Checkers::CheckReadWrite, event.GetPid()),
        CheckAndReport(kOpKAuthCopyDest, event.GetEventPath(DST_PATH), Checkers::CheckReadWrite, event.GetPid()));
}

AccessCheckResult IOHandler::HandleCreate(const IOEvent &event)
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
    
    return CheckAndReport(isDir ? kOpKAuthCreateDir : kOpMacVNodeCreate, event.GetEventPath(SRC_PATH), checker, event.GetPid(), isDir);
}

AccessCheckResult IOHandler::HandleGenericWrite(const IOEvent &event)
{
    const char *path = event.GetEventPath(SRC_PATH);
    mode_t mode = event.GetMode();
    bool isDir = S_ISDIR(mode);

    return CheckAndReport(kOpKAuthVNodeWrite, path, Checkers::CheckWrite, event.GetPid(), isDir);
}

AccessCheckResult IOHandler::HandleGenericRead(const IOEvent &event)
{
    const char *path = event.GetEventPath(SRC_PATH);
    mode_t mode = event.GetMode();
    bool isDir = S_ISDIR(mode);
    
    if (!event.EventPathExists())
    {
        return CheckAndReport(kOpMacLookup, path, Checkers::CheckLookup, event.GetPid(), false);
    }
    else
    {
        return CheckAndReport(kOpKAuthVNodeRead, path, Checkers::CheckRead, event.GetPid(), isDir);
    }
}

AccessCheckResult IOHandler::HandleGenericProbe(const IOEvent &event)
{
    const char *path = event.GetEventPath(SRC_PATH);
    mode_t mode = event.GetMode();
    bool isDir = S_ISDIR(mode);

    if (!event.EventPathExists())
    {
        return CheckAndReport(kOpMacLookup, path, Checkers::CheckLookup, event.GetPid(), false);
    }
    else
    {
        return CheckAndReport(kOpKAuthVNodeProbe, path, Checkers::CheckProbe, event.GetPid(), isDir);
    }
}

AccessCheckResult IOHandler::HandleEvent(const IOEvent &event)
{
    switch (event.GetEventType())
    {
        case ES_EVENT_TYPE_NOTIFY_EXEC:
            return HandleProcessExec(event);

        case ES_EVENT_TYPE_NOTIFY_FORK:
            return HandleProcessFork(event);

        case ES_EVENT_TYPE_NOTIFY_EXIT:
            return HandleProcessExit(event);

        case ES_EVENT_TYPE_NOTIFY_LOOKUP:
            return HandleLookup(event);

        case ES_EVENT_TYPE_NOTIFY_OPEN:
            return HandleOpen(event);

        case ES_EVENT_TYPE_NOTIFY_CLOSE:
            return HandleClose(event);

        case ES_EVENT_TYPE_NOTIFY_CREATE:
            return HandleCreate(event);

        case ES_EVENT_TYPE_NOTIFY_TRUNCATE:
        case ES_EVENT_TYPE_NOTIFY_SETATTRLIST:
        case ES_EVENT_TYPE_NOTIFY_SETEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_SETFLAGS:
        case ES_EVENT_TYPE_NOTIFY_SETOWNER:
        case ES_EVENT_TYPE_NOTIFY_SETMODE:
        case ES_EVENT_TYPE_NOTIFY_WRITE:
        case ES_EVENT_TYPE_NOTIFY_UTIMES:
        case ES_EVENT_TYPE_NOTIFY_SETTIME:
        case ES_EVENT_TYPE_NOTIFY_SETACL:
            return HandleGenericWrite(event);

        case ES_EVENT_TYPE_NOTIFY_CHDIR:
        case ES_EVENT_TYPE_NOTIFY_READDIR:
        case ES_EVENT_TYPE_NOTIFY_FSGETPATH:
            return HandleGenericRead(event);

        case ES_EVENT_TYPE_NOTIFY_GETATTRLIST:
        case ES_EVENT_TYPE_NOTIFY_GETEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_LISTEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_ACCESS:
        case ES_EVENT_TYPE_NOTIFY_STAT:
            return HandleGenericProbe(event);

        case ES_EVENT_TYPE_NOTIFY_CLONE:
            return HandleClone(event);

        case ES_EVENT_TYPE_NOTIFY_EXCHANGEDATA:
            return HandleExchange(event);

        case ES_EVENT_TYPE_NOTIFY_RENAME:
            return HandleRename(event);

        case ES_EVENT_TYPE_NOTIFY_READLINK:
            return HandleReadlink(event);

        case ES_EVENT_TYPE_NOTIFY_LINK:
            return HandleLink(event);

        case ES_EVENT_TYPE_NOTIFY_UNLINK:
            return HandleUnlink(event);
        case ES_EVENT_TYPE_LAST: 
            return AccessCheckResult::Invalid();
        default:
            std::string message("Unhandled ES event: ");
            message.append(std::to_string(event.GetEventType()));
            throw BuildXLException(message);
    }
}
